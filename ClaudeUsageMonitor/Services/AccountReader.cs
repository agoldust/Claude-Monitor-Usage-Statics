using System.IO;
using System.Text.Json;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Reads account and plan details from the local Claude Code files.
/// Access tokens and refresh tokens are never copied out of the credentials file.
/// </summary>
public sealed class AccountReader
{
    /// <summary>
    /// Reads the account details from the account file and the credentials file.
    /// A missing or unreadable file leaves its fields empty.
    /// </summary>
    /// <returns>The account details that could be read.</returns>
    public AccountInfo Read()
    {
        var info = new AccountInfo();

        var oauth = ReadOauthAccount();
        if (oauth is not null)
        {
            info = info with
            {
                Email = GetString(oauth.Value, "emailAddress"),
                FullName = GetString(oauth.Value, "fullName") ?? GetString(oauth.Value, "displayName"),
                OrganizationName = GetString(oauth.Value, "organizationName"),
                OrganizationRole = GetString(oauth.Value, "organizationRole"),
                OrganizationType = GetString(oauth.Value, "organizationType"),
                BillingType = GetString(oauth.Value, "billingType"),
                OrganizationRateLimitTier = GetString(oauth.Value, "organizationRateLimitTier"),
                RateLimitTier = GetString(oauth.Value, "userRateLimitTier"),
                AccountCreatedAt = GetDate(oauth.Value, "accountCreatedAt"),
                SubscriptionCreatedAt = GetDate(oauth.Value, "subscriptionCreatedAt"),
                ExtraUsageEnabled = GetBool(oauth.Value, "hasExtraUsageEnabled")
            };
        }

        var creds = ReadCredentialsSummary();
        if (creds is not null)
        {
            info = info with
            {
                SubscriptionType = GetString(creds.Value, "subscriptionType"),
                SubscriptionTypeSource = GetString(creds.Value, "subscriptionTypeSource"),
                RateLimitTier = GetString(creds.Value, "rateLimitTier") ?? info.RateLimitTier,
                CredentialsExpireAt = GetUnixMillis(creds.Value, "expiresAt")
            };
        }

        return info;
    }

    /// <summary>
    /// Reads the <c>oauthAccount</c> block from %USERPROFILE%\.claude.json.
    /// </summary>
    /// <returns>A copy of the block, or null when it is missing.</returns>
    private static JsonElement? ReadOauthAccount()
    {
        var root = ReadJson(ClaudePaths.AccountFile);
        if (root is null || !root.Value.TryGetProperty("oauthAccount", out var account) ||
            account.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return account.Clone();
    }

    /// <summary>
    /// Reads the non-secret fields of the <c>claudeAiOauth</c> block in the credentials file.
    /// Token values are never copied.
    /// </summary>
    /// <returns>An object that holds only the non-secret fields, or null when the block is missing.</returns>
    private static JsonElement? ReadCredentialsSummary()
    {
        var root = ReadJson(ClaudePaths.CredentialsFile);
        if (root is null || !root.Value.TryGetProperty("claudeAiOauth", out var oauth) ||
            oauth.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Copy only the non-secret fields into a fresh object so tokens never leave this method.
        using var summary = new MemoryStream();
        using (var writer = new Utf8JsonWriter(summary))
        {
            writer.WriteStartObject();
            foreach (var name in new[] { "subscriptionType", "subscriptionTypeSource", "rateLimitTier", "expiresAt" })
            {
                if (oauth.TryGetProperty(name, out var value))
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        summary.Position = 0;
        using var doc = JsonDocument.Parse(summary);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Parses a JSON file.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The root element, or null when the file is missing, unreadable or not valid JSON.</returns>
    private static JsonElement? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a string property from a JSON object.
    /// </summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The string value, or null when the property is missing or is not a string.</returns>
    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads a boolean property from a JSON object.
    /// </summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The boolean value, or null when the property is missing or is not a boolean.</returns>
    private static bool? GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    /// <summary>
    /// Reads a date-time string property from a JSON object.
    /// </summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The parsed date, or null when the property is missing or cannot be parsed.</returns>
    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        return DateTimeOffset.TryParse(GetString(element, name), out var date) ? date : null;
    }

    /// <summary>
    /// Reads a Unix time in milliseconds from a JSON object.
    /// </summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The converted time, or null when the property is missing or outside the supported range.</returns>
    private static DateTimeOffset? GetUnixMillis(JsonElement element, string name)
    {
        // Valid range for DateTimeOffset.FromUnixTimeMilliseconds: years 0001 to 9999.
        const long minMillis = -62135596800000L;
        const long maxMillis = 253402300799999L;

        if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var millis) &&
            millis >= minMillis && millis <= maxMillis)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis);
        }

        return null;
    }
}
