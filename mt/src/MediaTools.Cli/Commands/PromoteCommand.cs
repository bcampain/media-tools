using System.CommandLine;
using System.CommandLine.Invocation;
using MediaTools.App.Handlers;

namespace MediaTools.Cli.Commands;

public static class PromoteCommand
{
    public static readonly Option<int> RetentionDays = new(
        "--retention-days",
        () => 30,
        "Prune staging archives older than N days");

    public static readonly Option<bool> Overwrite = new(
        "--overwrite",
        () => false,
        "Overwrite existing library file if present");

    public static Command Build(PromoteCommandHandler handler)
    {
        var target = new Argument<string>("target", "Path to staged file or directory to promote");

        var cmd = new Command("promote", "Move processed media from staging to library");
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
        cmd.AddOption(RetentionDays);
        cmd.AddOption(Overwrite);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var options = new PromoteOptions(
                Target:        context.ParseResult.GetValueForArgument(target),
                IncomingRoot:  context.ParseResult.GetValueForOption(CommonOptions.IncomingRoot)!,
                StagingRoot:   context.ParseResult.GetValueForOption(CommonOptions.StagingRoot)!,
                LibraryRoot:   context.ParseResult.GetValueForOption(CommonOptions.LibraryRoot)!,
                RunId:         context.ParseResult.GetValueForOption(CommonOptions.RunId),
                DryRun:        context.ParseResult.GetValueForOption(CommonOptions.DryRun),
                Yes:           context.ParseResult.GetValueForOption(CommonOptions.Yes),
                LogDir:        context.ParseResult.GetValueForOption(CommonOptions.LogDir)!,
                Json:          context.ParseResult.GetValueForOption(CommonOptions.Json),
                Verbosity:     context.ParseResult.GetValueForOption(CommonOptions.Verbosity)!,
                RetentionDays: context.ParseResult.GetValueForOption(RetentionDays),
                Overwrite:     context.ParseResult.GetValueForOption(Overwrite)
            );

            context.ExitCode = await handler.HandleAsync(options, context.GetCancellationToken());
        });

        return cmd;
    }
}
