using System.CommandLine;
using System.CommandLine.Invocation;
using MediaTools.App.Handlers;

namespace MediaTools.Cli.Commands;

public static class NotifyDiscordCommand
{
    public static readonly Option<string?> Log = new(
        "--log",
        () => null,
        "Path to log file whose tail will be appended to the Discord message");

    public static Command Build(NotifyDiscordCommandHandler handler)
    {
        var title   = new Argument<string>("title",   "Notification title");
        var message = new Argument<string>("message", "Notification message body");

        var cmd = new Command("notify-discord", "Send a Discord webhook notification");
        cmd.AddArgument(title);
        cmd.AddArgument(message);
        cmd.AddOption(Log);
        cmd.AddOption(CommonOptions.RunId);
        cmd.AddOption(CommonOptions.DryRun);
        cmd.AddOption(CommonOptions.LogDir);
        cmd.AddOption(CommonOptions.Json);
        cmd.AddOption(CommonOptions.Verbosity);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var options = new NotifyDiscordOptions(
                Title:     context.ParseResult.GetValueForArgument(title),
                Message:   context.ParseResult.GetValueForArgument(message),
                Log:       context.ParseResult.GetValueForOption(Log),
                RunId:     context.ParseResult.GetValueForOption(CommonOptions.RunId),
                DryRun:    context.ParseResult.GetValueForOption(CommonOptions.DryRun),
                LogDir:    context.ParseResult.GetValueForOption(CommonOptions.LogDir)!,
                Json:      context.ParseResult.GetValueForOption(CommonOptions.Json),
                Verbosity: context.ParseResult.GetValueForOption(CommonOptions.Verbosity)!
            );

            context.ExitCode = await handler.HandleAsync(options, context.GetCancellationToken());
        });

        return cmd;
    }
}
