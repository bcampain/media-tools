using System.Net;
using System.Text;
using System.Text.Json;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Infrastructure.Notifications;

// HttpClient is injected as a singleton (registered in Program.cs)
public class DiscordNotifier(HttpClient http, ILogSink log) : IDiscordNotifier
{
    // Hard-coded log tail limits to keep the total payload
    // safely under Discord's 2000-character message limit.
    private const int TailLines    = 25;
    private const int MaxTailChars = 1400;

    // Read the WebHook URL from the environment at construction time.
    private readonly string? _webhookUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");

    public async Task<int> NotifyAsync(string title, string message, string? logPath, CancellationToken ct)
    {
        if (_webhookUrl is null)
            return 0; // Disabled — Program.cs already warned at startup.

        var content = BuildContent(title, message, logPath);
        log.Info($"[discord-notify] Posting to webhook ({content.Length} chars)");

        // Discord expects { "content": "..." }
        var payload = JsonSerializer.Serialize(new { content });
        var body    = new StringContent(payload, Encoding.UTF8, "application/json");

        // Dispose the response to release the underlying connection back to the pool.
        // HttpRequestException covers transient failures (dropped connection, DNS, timeout).
        // These are non-fatal — Discord notifications should never crash the pipeline.
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(_webhookUrl, body, ct);
        }
        catch (HttpRequestException ex)
        {
            log.Warn($"[discord-notify] Failed to post to webhook: {ex.Message}");
            return 1;
        }

        using var _ = response;
        log.Info($"[discord-notify] http_code={(int)response.StatusCode}");

        // Discord returns 204 No Content on success for most webhook endpoints.
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent))
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            log.Warn($"[discord-notify] response: {responseBody}");
            return 1;
        }

        return 0;
    }

    private string BuildContent(string title, string message, string? logPath)
    {
        var sb = new StringBuilder();
        sb.Append($"**{title}**\n{message}");

        if (!string.IsNullOrEmpty(logPath))
        {
            try
            {
                // File.ReadLines streams lazily; TakeLast buffers only the trailing window.
                var tail = string.Join("\n", File.ReadLines(logPath).TakeLast(TailLines));

                if (tail.Length > MaxTailChars)
                {
                    // Build truncated tail directly to avoid an extra string allocation.
                    sb.Append("\n```\n...(log tail truncated)\n");
                    sb.Append(tail, tail.Length - MaxTailChars, MaxTailChars);
                    sb.Append("\n```");
                }
                else
                {
                    sb.Append($"\n```\n{tail}\n```");
                }
            }
            catch (IOException ex)
            {
                log.Warn($"[discord-notify] Could not read log file '{logPath}': {ex.Message}");
            }
        }

        return sb.ToString();
    }
}
