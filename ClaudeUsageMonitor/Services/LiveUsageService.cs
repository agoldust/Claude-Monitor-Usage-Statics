using System.Text.Json;
using ClaudeUsageMonitor.Models;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Fetches live plan limits for the signed-in claude.ai account through a <see cref="ClaudeWebSession"/>.
/// </summary>
public sealed class LiveUsageService
{
    private readonly ClaudeWebSession _session;

    /// <summary>Creates the service on top of an initialized or initializing web session.</summary>
    public LiveUsageService(ClaudeWebSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Fetches the organization list, picks one, and fetches its usage limits.
    /// Never throws; every failure is returned as a <see cref="LiveUsageResult"/>.
    /// </summary>
    /// <param name="preferredOrganizationId">The organization to use when the account has several, such as the one Claude Code uses. May be null.</param>
    public async Task<LiveUsageResult> FetchAsync(string? preferredOrganizationId)
    {
        try
        {
            var (orgStatus, orgBody) = await _session.GetAsync("/api/organizations");
            if (orgStatus is 401 or 403)
            {
                return new LiveUsageResult(LiveUsageStatus.SignedOut, Message: "Not signed in to claude.ai.");
            }

            if (orgStatus != 200)
            {
                return new LiveUsageResult(LiveUsageStatus.Error, Message: $"claude.ai returned status {orgStatus} for the account.");
            }

            var organization = PickOrganization(orgBody, preferredOrganizationId);
            if (organization is null)
            {
                return new LiveUsageResult(LiveUsageStatus.Error, Message: "No organization was found for this account.");
            }

            var (usageStatus, usageBody) = await _session.GetAsync($"/api/organizations/{organization.Value.Id:D}/usage");
            if (usageStatus is 401 or 403)
            {
                return new LiveUsageResult(LiveUsageStatus.SignedOut, Message: "The claude.ai session has expired.");
            }

            if (usageStatus != 200)
            {
                return new LiveUsageResult(LiveUsageStatus.Error, Message: $"claude.ai returned status {usageStatus} for usage.");
            }

            var (limits, extra, spend) = LiveUsageParser.Parse(usageBody);
            return new LiveUsageResult(LiveUsageStatus.Ok,
                new LiveUsage(limits, organization.Value.Name, DateTimeOffset.Now, extra, spend));
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return new LiveUsageResult(LiveUsageStatus.RuntimeMissing,
                Message: "Live data needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft, then restart the app.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or TimeoutException or KeyNotFoundException)
        {
            return new LiveUsageResult(LiveUsageStatus.Error, Message: ex.Message);
        }
    }

    /// <summary>
    /// Picks the preferred organization when it is in the list, otherwise the first one with chat access, otherwise the first one.
    /// Only IDs that parse as GUIDs are accepted, so a response cannot inject text into the next request path.
    /// </summary>
    private static (Guid Id, string? Name)? PickOrganization(string json, string? preferredId)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<(Guid Id, string? Name, bool HasChat)>();
        foreach (var org in doc.RootElement.EnumerateArray())
        {
            if (org.ValueKind != JsonValueKind.Object ||
                !org.TryGetProperty("uuid", out var uuid) || uuid.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(uuid.GetString(), out var id))
            {
                continue;
            }

            var name = org.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var hasChat = org.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array &&
                          caps.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "chat");
            candidates.Add((id, name, hasChat));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (Guid.TryParse(preferredId, out var preferred))
        {
            var match = candidates.FirstOrDefault(c => c.Id == preferred);
            if (match.Id != Guid.Empty)
            {
                return (match.Id, match.Name);
            }
        }

        var pick = candidates.FirstOrDefault(c => c.HasChat);
        if (pick.Id == Guid.Empty)
        {
            pick = candidates[0];
        }

        return (pick.Id, pick.Name);
    }
}
