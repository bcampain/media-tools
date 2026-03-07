namespace MediaTools.App.Handlers;

/// <summary>
/// Utilities shared by all command handlers.
/// Kept intentionally small — only things repeated in every handler live here.
/// </summary>
internal static class HandlerHelpers
{
    /// <summary>
    /// Exit code returned for validation failures, matching the bash script contract (exit 2).
    /// </summary>
    internal const int ValidationExitCode = 2;

    /// <summary>
    /// Wraps a path in double-quotes if it contains spaces, for readable plan output.
    /// </summary>
    internal static string Q(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    /// <summary>
    /// Prompts "Proceed? [y/N]" and returns true if the user confirms, false if they cancel.
    /// Logging of the "Cancelled" message is left to the caller since it knows the command name.
    /// </summary>
    internal static bool Confirm()
    {
        Console.Write("Proceed? [y/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer == "y" || answer == "yes";
    }
}
