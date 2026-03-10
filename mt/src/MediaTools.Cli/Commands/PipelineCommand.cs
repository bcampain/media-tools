using System.CommandLine;
using System.CommandLine.Invocation;
using MediaTools.App.Handlers;

namespace MediaTools.Cli.Commands;

public static class PipelineCommand
{
    public static readonly Option<bool> StopOnError = new(
        "--stop-on-error",
        () => true,
        "Halt the pipeline when any step returns a non-zero exit code");

    public static readonly Option<bool> Notify = new(
        "--notify",
        () => true,
        "Send Discord notifications at each pipeline step");

    public static readonly Option<string?> Step = new(
        "--step",
        () => null,
        "Start pipeline at this step: handbrake | normalize | promote");

    public static readonly Option<string?> Until = new(
        "--until",
        () => null,
        "Stop pipeline after this step: handbrake | normalize | promote");

    public static Command Build(PipelineCommandHandler handler)
    {
        var target = new Argument<string>(
            "target",
            "Path to process (must be under --incoming-root for tv/movies/clips)");

        var cmd = new Command("pipeline", "Run the full handbrake → normalize → promote chain");
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
        cmd.AddOption(StopOnError);
        cmd.AddOption(Notify);
        cmd.AddOption(Step);
        cmd.AddOption(Until);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var options = new PipelineOptions(
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
                StopOnError:  context.ParseResult.GetValueForOption(StopOnError),
                Notify:       context.ParseResult.GetValueForOption(Notify),
                Step:         context.ParseResult.GetValueForOption(Step),
                Until:        context.ParseResult.GetValueForOption(Until)
            );

            context.ExitCode = await handler.HandleAsync(options, context.GetCancellationToken());
        });

        return cmd;
    }
}
