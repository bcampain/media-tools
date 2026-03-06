using System.CommandLine;
using System.CommandLine.Invocation;
using MediaTools.App.Handlers;

namespace MediaTools.Cli.Commands;

public static class HandbrakeCommand
{
    public static readonly Option<int> Quality = new(
        "--quality",
        () => 23,
        "HandBrakeCLI RF quality value");

    public static readonly Option<string> Preset = new(
        "--preset",
        () => "fast",
        "HandBrakeCLI encoder preset name");

    public static readonly Option<int> MaxDepth = new(
        "--max-depth",
        () => 3,
        "Directory traversal depth for finding source files");

    public static readonly Option<bool> Force = new(
        "--force",
        () => false,
        "Re-encode even if output already exists");

    public static Command Build(HandbrakeCommandHandler handler)
    {
        var target = new Argument<string>("target", "Path to file or directory to encode");

        var cmd = new Command("handbrake", "Encode video files using HandBrakeCLI");
        cmd.AddArgument(target);

        cmd.AddOption(CommonOptions.IncomingRoot);
        cmd.AddOption(CommonOptions.StagingRoot);
        cmd.AddOption(CommonOptions.LibraryRoot);
        cmd.AddOption(CommonOptions.RunId);
        cmd.AddOption(CommonOptions.DryRun);
        cmd.AddOption(CommonOptions.Yes);
        cmd.AddOption(CommonOptions.LogDir);
        cmd.AddOption(CommonOptions.Json);
        cmd.AddOption(CommonOptions.Verbosity);
        cmd.AddOption(Quality);
        cmd.AddOption(Preset);
        cmd.AddOption(MaxDepth);
        cmd.AddOption(Force);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var options = new HandbrakeOptions(
                Target:       context.ParseResult.GetValueForArgument(target),
                IncomingRoot: context.ParseResult.GetValueForOption(CommonOptions.IncomingRoot)!,
                StagingRoot:  context.ParseResult.GetValueForOption(CommonOptions.StagingRoot)!,
                LibraryRoot:  context.ParseResult.GetValueForOption(CommonOptions.LibraryRoot)!,
                RunId:        context.ParseResult.GetValueForOption(CommonOptions.RunId),
                DryRun:       context.ParseResult.GetValueForOption(CommonOptions.DryRun),
                Yes:          context.ParseResult.GetValueForOption(CommonOptions.Yes),
                LogDir:       context.ParseResult.GetValueForOption(CommonOptions.LogDir)!,
                Json:         context.ParseResult.GetValueForOption(CommonOptions.Json),
                Verbosity:    context.ParseResult.GetValueForOption(CommonOptions.Verbosity)!,
                Quality:      context.ParseResult.GetValueForOption(Quality),
                Preset:       context.ParseResult.GetValueForOption(Preset)!,
                MaxDepth:     context.ParseResult.GetValueForOption(MaxDepth),
                Force:        context.ParseResult.GetValueForOption(Force)
            );

            context.ExitCode = await handler.HandleAsync(options, context.GetCancellationToken());
        });

        return cmd;
    }
}
