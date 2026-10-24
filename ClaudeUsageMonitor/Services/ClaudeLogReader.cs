using System.IO;
using System.Text.Json;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Reads Claude Code session logs (%USERPROFILE%\.claude\projects\**\*.jsonl) and returns usage records.
/// </summary>
public sealed class ClaudeLogReader
{
    /// <summary>The folder that holds the session logs, normally %USERPROFILE%\.claude\projects.</summary>
    private readonly string _projectsRoot;

    /// <summary>
    /// Creates a reader for the default Claude Code projects folder.
    /// </summary>
    public ClaudeLogReader()
        : this(Path.Combine(ClaudePaths.ClaudeHome, "projects"))
    {
    }

    /// <summary>
    /// Creates a reader for a given projects folder.
    /// </summary>
    /// <param name="projectsRoot">The folder to scan for <c>*.jsonl</c> session logs.</param>
    public ClaudeLogReader(string projectsRoot)
    {
        _projectsRoot = projectsRoot;
    }

    /// <summary>
    /// Gets a value that indicates whether the projects folder exists.
    /// </summary>
    public bool RootExists => Directory.Exists(_projectsRoot);

    /// <summary>
    /// Reads every session log and returns one record per API response.
    /// Claude Code writes one line per content block and repeats the same usage on each,
    /// so lines that share a message ID and request ID are counted once.
    /// Malformed lines and unreadable files are skipped, never fatal.
    /// </summary>
    public IReadOnlyList<UsageRecord> ReadAll()
    {
        var byResponse = new Dictionary<string, UsageRecord>(StringComparer.Ordinal);
        var withoutId = new List<UsageRecord>();

        foreach (var file in EnumerateLogFiles())
        {
            var projectFolder = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
            var sessionId = Path.GetFileNameWithoutExtension(file);

            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    var parsed = TryParseLine(line, projectFolder, sessionId);
                    if (parsed is null)
                    {
                        continue;
                    }

                    // Later lines carry the final output_tokens for a streamed response, so the last one wins.
                    if (parsed.ResponseKey is null)
                    {
                        withoutId.Add(parsed.Record);
                    }
                    else
                    {
                        byResponse[parsed.ResponseKey] = parsed.Record;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file may be open in Claude Code right now, or be unreadable; skip it for this refresh.
            }
        }

        return byResponse.Values.Concat(withoutId).ToList();
    }

    /// <summary>
    /// Lists every <c>*.jsonl</c> file under the projects folder.
    /// Returns an empty list when the folder is missing or cannot be scanned.
    /// </summary>
    /// <returns>The full paths of the log files.</returns>
    private IEnumerable<string> EnumerateLogFiles()
    {
        if (!RootExists)
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateFiles(_projectsRoot, "*.jsonl", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder may be removed or locked during the scan; return whatever could be listed.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Parses one JSON line from a session log into a usage record.
    /// </summary>
    /// <param name="line">The raw JSON line.</param>
    /// <param name="projectFolder">The project folder that the log file belongs to.</param>
    /// <param name="fallbackSession">The session ID to use when the line does not contain one.</param>
    /// <returns>The parsed line, or null when the line has no usage block, no valid timestamp, or is malformed.</returns>
    private static ParsedLine? TryParseLine(string line, string projectFolder, string fallbackSession)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("timestamp", out var tsElement) ||
                tsElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(tsElement.GetString(), out var timestamp))
            {
                return null;
            }

            var model = message.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString() ?? "unknown"
                : "unknown";

            var sessionId = root.TryGetProperty("sessionId", out var sessionElement) && sessionElement.ValueKind == JsonValueKind.String
                ? sessionElement.GetString() ?? fallbackSession
                : fallbackSession;

            var record = new UsageRecord(
                timestamp,
                model,
                sessionId,
                projectFolder,
                ReadLong(usage, "input_tokens"),
                ReadLong(usage, "output_tokens"),
                ReadLong(usage, "cache_creation_input_tokens"),
                ReadLong(usage, "cache_read_input_tokens"));

            return new ParsedLine(ResponseKeyOf(message, root), record);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Half-written or unexpectedly shaped line; skip it.
            return null;
        }
    }

    /// <summary>
    /// Builds the key that identifies one API response, or null when the line has no message ID.
    /// </summary>
    private static string? ResponseKeyOf(JsonElement message, JsonElement root)
    {
        if (!message.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var requestId = root.TryGetProperty("requestId", out var reqElement) && reqElement.ValueKind == JsonValueKind.String
            ? reqElement.GetString()
            : null;

        return $"{idElement.GetString()}|{requestId}";
    }

    /// <summary>
    /// Reads an integer property from a JSON object.
    /// </summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or 0 when the property is missing or is not an integer.</returns>
    private static long ReadLong(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;
    }

    /// <summary>
    /// A usage record from one log line, with the key that identifies its API response.
    /// </summary>
    /// <param name="ResponseKey">The message ID and request ID key, or null when the line has no message ID.</param>
    /// <param name="Record">The usage record parsed from the line.</param>
    private sealed record ParsedLine(string? ResponseKey, UsageRecord Record);
}
