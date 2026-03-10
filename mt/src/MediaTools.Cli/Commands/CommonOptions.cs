using System.CommandLine;

namespace MediaTools.Cli.Commands;

/// <summary>
/// Single source of truth for all shared Option instances.
/// Each command adds these by reference to avoid duplicate-alias errors in System.CommandLine.
/// </summary>
public static class CommonOptions
{
    public static readonly Option<string> IncomingRoot = new(
        "--incoming-root",
        () => "/incoming",
        "Root directory for incoming media");

    public static readonly Option<string> StagingRoot = new(
        "--staging-root",
        () => "/staging",
        "Root directory for staging media");

    public static readonly Option<string> LibraryRoot = new(
        "--library-root",
        () => "/library",
        "Root directory for the media library");

    public static readonly Option<string?> RunId = new(
        "--run-id",
        () => null,
        "Pipeline run identifier (default: MMDDyyHHmmss timestamp)");

    public static readonly Option<bool> DryRun = new(
        "--dry-run",
        () => false,
        "Print what would be invoked without executing");

    public static readonly Option<bool> Yes = new(
        "--yes",
        () => false,
        "Skip confirmation prompts");

    public static readonly Option<string> LogDir = new(
        "--log-dir",
        () => "/logs",
        "Directory for pipeline-level log output");

    public static readonly Option<bool> Json = new(
        "--json",
        () => false,
        "Emit structured JSON output");

    public static readonly Option<string> Verbosity = new(
        "--verbosity",
        () => "normal",
        "Output verbosity: quiet | normal | verbose");
}
