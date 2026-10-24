using System.Globalization;
using System.Text.Json;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Turns the JSON from the claude.ai usage endpoint into <see cref="LiveUsage"/> parts.
/// The endpoint is not a public API, so every field is optional and anything unrecognized is skipped.
/// </summary>
public static class LiveUsageParser
{
    private static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    private static bool _shapeLogged;

    // Label, time window, and the top-level object that holds extra fields, for each known limit kind.
    private static readonly Dictionary<string, (string Label, TimeSpan? Window, string? TopLevelKey)> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["session"] = ("Current session (5-hour)", SessionWindow, "five_hour"),
            ["weekly_all"] = ("Weekly (all models)", WeeklyWindow, "seven_day"),
            ["weekly_opus"] = ("Weekly (Opus)", WeeklyWindow, "seven_day_opus"),
            ["weekly_sonnet"] = ("Weekly (Sonnet)", WeeklyWindow, "seven_day_sonnet"),
            ["five_hour"] = ("Current session (5-hour)", SessionWindow, "five_hour"),
            ["seven_day"] = ("Weekly (all models)", WeeklyWindow, "seven_day"),
            ["seven_day_opus"] = ("Weekly (Opus)", WeeklyWindow, "seven_day_opus"),
            ["seven_day_sonnet"] = ("Weekly (Sonnet)", WeeklyWindow, "seven_day_sonnet"),
            ["seven_day_oauth_apps"] = ("Weekly (OAuth apps)", WeeklyWindow, "seven_day_oauth_apps")
        };

    /// <summary>
    /// Parses the usage JSON into limits, extra usage and spend.
    /// </summary>
    /// <param name="json">The response body from the usage endpoint.</param>
    /// <returns>The limits (in the order claude.ai lists them), extra usage, and spend. Missing parts are empty or null.</returns>
    /// <exception cref="JsonException">The body is not valid JSON.</exception>
    public static (IReadOnlyList<LimitUsage> Limits, ExtraUsageInfo? Extra, SpendInfo? Spend) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (Array.Empty<LimitUsage>(), null, null);
        }

        LogShapeOnce(root);

        var limits = ParseLimitsList(root);
        if (limits.Count == 0)
        {
            limits = ParseTopLevelObjects(root);
        }

        return (limits, ParseExtraUsage(root), ParseSpend(root));
    }

    private static List<LimitUsage> ParseLimitsList(JsonElement root)
    {
        var result = new List<LimitUsage>();
        if (!root.TryGetProperty("limits", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || GetDouble(item, "percent") is not { } percent)
            {
                continue;
            }

            var kind = GetString(item, "kind") ?? "limit";
            var group = GetString(item, "group");
            var (label, window, topKey) = Describe(kind, group);

            // Dollar amounts and lock reasons live in the matching top-level object, such as "seven_day".
            JsonElement? details = topKey is not null && root.TryGetProperty(topKey, out var d) && d.ValueKind == JsonValueKind.Object
                ? d
                : null;

            result.Add(new LimitUsage(
                label,
                Clamp(percent),
                GetDate(item, "resets_at"),
                GetString(item, "severity"),
                GetBool(item, "is_active"),
                window,
                details is { } u ? GetDecimal(u, "used_dollars") : null,
                details is { } l ? GetDecimal(l, "limit_dollars") : null,
                details is { } r ? GetString(r, "locked_reason") : null));
        }

        return result;
    }

    private static List<LimitUsage> ParseTopLevelObjects(JsonElement root)
    {
        var result = new List<LimitUsage>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("extra_usage", StringComparison.OrdinalIgnoreCase) ||
                property.Value.ValueKind != JsonValueKind.Object ||
                GetDouble(property.Value, "utilization") is not { } percent)
            {
                continue;
            }

            var (label, window, _) = Describe(property.Name, null);
            result.Add(new LimitUsage(
                label,
                Clamp(percent),
                GetDate(property.Value, "resets_at"),
                null,
                null,
                window,
                GetDecimal(property.Value, "used_dollars"),
                GetDecimal(property.Value, "limit_dollars"),
                GetString(property.Value, "locked_reason")));
        }

        return result;
    }

    private static ExtraUsageInfo? ParseExtraUsage(JsonElement root)
    {
        if (!root.TryGetProperty("extra_usage", out var extra) || extra.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Amounts are scaled by decimal_places when it is given. This is inferred from the field names
        // and has not been checked against an account with extra usage turned on.
        var scale = GetDouble(extra, "decimal_places") is { } places && places is >= 0 and <= 6
            ? (decimal)Math.Pow(10, places)
            : 1m;

        return new ExtraUsageInfo(
            GetBool(extra, "is_enabled") == true,
            GetDouble(extra, "utilization") is { } p ? Clamp(p) : null,
            GetDecimal(extra, "used_credits") / scale,
            GetDecimal(extra, "monthly_limit") / scale,
            GetString(extra, "currency"),
            GetString(extra, "disabled_reason"));
    }

    private static SpendInfo? ParseSpend(JsonElement root)
    {
        if (!root.TryGetProperty("spend", out var spend) || spend.ValueKind != JsonValueKind.Object ||
            ReadMoney(spend, "used") is not { } used)
        {
            return null;
        }

        var limit = ReadMoney(spend, "limit");
        var percent = GetDouble(spend, "percent");
        return new SpendInfo(used.Amount, limit?.Amount, used.Currency, percent is { } p ? Clamp(p) : null);
    }

    /// <summary>Reads a money object such as {"amount_minor":1234,"currency":"USD","exponent":2}.</summary>
    private static (decimal Amount, string Currency)? ReadMoney(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object ||
            GetDecimal(money, "amount_minor") is not { } minor)
        {
            return null;
        }

        var exponent = GetDouble(money, "exponent") is { } e && e is >= 0 and <= 6 ? (int)e : 2;
        return (minor / (decimal)Math.Pow(10, exponent), GetString(money, "currency") ?? "USD");
    }

    private static (string Label, TimeSpan? Window, string? TopLevelKey) Describe(string kind, string? group)
    {
        if (Known.TryGetValue(kind, out var known))
        {
            return known;
        }

        var window = group?.ToLowerInvariant() switch
        {
            "session" => SessionWindow,
            "weekly" => WeeklyWindow,
            _ => kind.StartsWith("seven_day", StringComparison.OrdinalIgnoreCase) ? WeeklyWindow : (TimeSpan?)null
        };

        var words = kind.Replace('_', ' ').Trim();
        var label = words.Length == 0 ? "Limit" : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words);
        return (label, window, null);
    }

    /// <summary>
    /// Logs the names of the fields that have values, once per run, so a change in the endpoint is easy to spot.
    /// Only field names are logged, never values.
    /// </summary>
    private static void LogShapeOnce(JsonElement root)
    {
        if (_shapeLogged)
        {
            return;
        }

        _shapeLogged = true;
        var present = root.EnumerateObject()
            .Where(p => p.Value.ValueKind is not JsonValueKind.Null)
            .Select(p => p.Name);
        DebugLog.Write($"Usage fields with values: {string.Join(", ", present)}");

        if (root.TryGetProperty("limits", out var list) && list.ValueKind == JsonValueKind.Array &&
            list.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first)
        {
            DebugLog.Write($"Limit item fields: {string.Join(", ", first.EnumerateObject().Select(p => p.Name))}");
        }
    }

    private static double Clamp(double percent) => Math.Clamp(percent, 0, 100);

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)
            ? d
            : null;
    }

    private static decimal? GetDecimal(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d)
            ? d
            : null;
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        return DateTimeOffset.TryParse(GetString(element, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
