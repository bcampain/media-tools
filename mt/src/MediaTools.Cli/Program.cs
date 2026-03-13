using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using MediaTools.App.FileSystem;
using MediaTools.App.Handbrake;
using MediaTools.App.Normalize;
using MediaTools.App.Handlers;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Manifests;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;
using MediaTools.Cli.Commands;

// ── DI container ─────────────────────────────────────────────────────────────
var services = new ServiceCollection();

// Date-only filename means each day naturally gets its own file — no explicit
// rollover logic needed. Each invocation computes today's Central date at startup.
// Format: /logs/media-tools-mt-MMddyyyy.log
var centralNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Chicago");
var cliLogPath = Path.Combine(
    "/logs",
    $"media-tools-mt-{centralNow:MMddyyyy}.log");
try { Directory.CreateDirectory("/logs"); } catch (Exception ex) { Console.Error.WriteLine($"[WARN] Could not create log directory: {ex.Message}"); }

// TeeLogSink composes ConsoleLogSink (console output) with file appending.
// Non-pipeline commands share this singleton (one file per day).
// The pipeline command creates its own run-scoped top-level sink.
services.AddSingleton<ILogSink>(new TeeLogSink(new ConsoleLogSink(), cliLogPath));

// Singleton HttpClient for DiscordNotifier
services.AddSingleton<HttpClient>();

// ManifestWriter — writes /logs/runs/{RunId}.json for the mt-dashboard to read.
// Path resolved at startup so it matches whatever --log-dir the CLI would use.
var runsDir = Path.Combine("/logs", "runs");
services.AddSingleton<IManifestWriter>(new ManifestWriter(runsDir));

// Shared file-system utilities used by native step runners.
// VideoFileScanner is stateless; a singleton is safe and avoids allocation overhead.
services.AddSingleton<VideoFileScanner>();

// NativeHandbrakeRunner replaces the old HandbrakeRunner script caller.
// It uses HandBrakeCLI directly and reports per-file progress to the manifest
// so the mt-dashboard can render a live progress bar.
services.AddSingleton<IHandbrakeRunner, NativeHandbrakeRunner>();
services.AddSingleton<INormalizeRunner, NativeNormalizeRunner>();

// "Promote" still delegates to its shell script
services.AddSingleton<IPromoteRunner,   PromoteRunner>();
services.AddSingleton<IDiscordNotifier, DiscordNotifier>();
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL")))
    Console.Error.WriteLine("[WARN] DISCORD_WEBHOOK_URL is not set — Discord notifications are disabled");

// Handlers
services.AddSingleton<HandbrakeCommandHandler>();
services.AddSingleton<NormalizeCommandHandler>();
services.AddSingleton<PromoteCommandHandler>();
services.AddSingleton<NotifyDiscordCommandHandler>();
services.AddSingleton<PipelineCommandHandler>();

var sp = services.BuildServiceProvider();

// ── Root command ─────────────────────────────────────────────────────────────
var root = new RootCommand("mt — MediaTools pipeline orchestrator");

root.AddCommand(HandbrakeCommand.Build(sp.GetRequiredService<HandbrakeCommandHandler>()));
root.AddCommand(NormalizeCommand.Build(sp.GetRequiredService<NormalizeCommandHandler>()));
root.AddCommand(PromoteCommand.Build(sp.GetRequiredService<PromoteCommandHandler>()));
root.AddCommand(NotifyDiscordCommand.Build(sp.GetRequiredService<NotifyDiscordCommandHandler>()));
root.AddCommand(PipelineCommand.Build(sp.GetRequiredService<PipelineCommandHandler>()));

// ── Middleware ────────────────────────────────────────────────────────────────
var parser = new CommandLineBuilder(root)
    .UseDefaults()
    .AddMiddleware(async (context, next) =>
    {
        // Best-effort: ensure the log directory exists before handlers run.
        // The bash scripts always write to /logs/ directly; this covers the
        // pipeline-level summary log that the C# orchestrator may write.
        var logDir = context.ParseResult.GetValueForOption(CommonOptions.LogDir) ?? "/logs";
        try { 
            Directory.CreateDirectory(logDir); 
        } catch (Exception ex) {
            Console.Error.WriteLine($"[WARN] Could not create log directory: {ex.Message}");
        }

        await next(context);
    })
    .Build();

return await parser.InvokeAsync(args);
