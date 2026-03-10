using System.CommandLine;
using System.CommandLine.Invocation;
using MediaTools.App.Handlers;

namespace MediaTools.Cli.Commands;

public static class NormalizeCommand
{
    public static readonly Option<string> TargetI = new(
        "--target-i",
        () => "-16",
        "Integrated loudness target in LUFS (e.g. -16)");

    public static readonly Option<string> TruePeak = new(
        "--true-peak",
        () => "-1.5",
        "True peak ceiling in dBTP (e.g. -1.5)");

    public static readonly Option<string> Lra = new(
        "--lra",
        () => "11",
        "Loudness range target in LU (e.g. 11)");

    public static readonly Option<string> StereoTrack = new(
        "--stereo-track",
        () => "on",
        "Add downmixed stereo track alongside 5.1: on | off");

    public static readonly Option<bool> OnePass = new(
        "--one-pass",
        () => false,
        "Skip loudnorm measurement pass (faster, less accurate)");

    public static readonly Option<bool> Force = new(
        "--force",
        () => false,
        "Re-normalize even if output already exists");

    public static Command Build(NormalizeCommandHandler handler)
    {
        var target = new Argument<string>("target", "Path to file or directory to normalize");

        var cmd = new Command("normalize", "Normalize audio loudness using ffmpeg loudnorm");
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
        cmd.AddOption(TargetI);
        cmd.AddOption(TruePeak);
        cmd.AddOption(Lra);
        cmd.AddOption(StereoTrack);
        cmd.AddOption(OnePass);
        cmd.AddOption(Force);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var options = new NormalizeOptions(
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
                TargetI:      context.ParseResult.GetValueForOption(TargetI)!,
                TruePeak:     context.ParseResult.GetValueForOption(TruePeak)!,
                Lra:          context.ParseResult.GetValueForOption(Lra)!,
                StereoTrack:  context.ParseResult.GetValueForOption(StereoTrack)!,
                OnePass:      context.ParseResult.GetValueForOption(OnePass),
                Force:        context.ParseResult.GetValueForOption(Force)
            );

            context.ExitCode = await handler.HandleAsync(options, context.GetCancellationToken());
        });

        return cmd;
    }
}
