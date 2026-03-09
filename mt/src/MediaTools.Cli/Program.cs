using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using MediaTools.App.Handlers;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;
using MediaTools.Cli.Commands;

// ── DI container ─────────────────────────────────────────────────────────────
var services = new ServiceCollection();

// Date-only filename means each day naturally gets its own file — no explicit
// rollover logic needed. Each invocation computes today's date at startup.
// Format: /logs/media-tools-mt-MMddyyyy.log
var cliLogPath = Path.Combine(
    "/logs",
    $"media-tools-mt-{DateTime.UtcNow:MMddyyyy}.log");
try { Directory.CreateDirectory("/logs"); } catch (Exception ex) { Console.Error.WriteLine($"[WARN] Could not create log directory: {ex.Message}"); }

// TeeLogSink composes ConsoleLogSink (console output) with file appending.
// All handlers and runners share the same singleton — one file per day.
services.AddSingleton<ILogSink>(new TeeLogSink(new ConsoleLogSink(), cliLogPath));

// Script adapters (stubs for M1; replaced with real runners in M3)
services.AddSingleton<IHandbrakeRunner, HandbrakeRunner>();
services.AddSingleton<INormalizeRunner, NormalizeRunner>();
services.AddSingleton<IPromoteRunner,   PromoteRunner>();
services.AddSingleton<IDiscordNotifier, DiscordNotifier>();

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
