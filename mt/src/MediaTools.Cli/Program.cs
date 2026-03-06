using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using MediaTools.App.Handlers;
using MediaTools.Infrastructure.Logging;
using MediaTools.Scripts;
using MediaTools.Scripts.Stubs;
using MediaTools.Cli.Commands;

// ── DI container ─────────────────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddSingleton<ILogSink, ConsoleLogSink>();

// Script adapters (stubs for M1; replaced with real runners in M3)
services.AddSingleton<IHandbrakeRunner, StubHandbrakeRunner>();
services.AddSingleton<INormalizeRunner, StubNormalizeRunner>();
services.AddSingleton<IPromoteRunner,   StubPromoteRunner>();
services.AddSingleton<IDiscordNotifier, StubDiscordNotifier>();

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
        try { Directory.CreateDirectory(logDir); } catch { /* non-fatal */ }

        await next(context);
    })
    .Build();

return await parser.InvokeAsync(args);
