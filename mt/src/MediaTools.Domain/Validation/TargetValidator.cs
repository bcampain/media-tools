using MediaTools.Domain.Models;

namespace MediaTools.Domain.Validation;

/// <summary>
/// Validates target paths for pipeline commands and derives related paths.
///
/// Design notes:
/// - All methods are pure (no I/O, no filesystem access, no side effects).
///   Pure functions are trivial to unit test and have no hidden dependencies.
/// - Path.GetFullPath() is used to normalize paths: it resolves ".." segments,
///   collapses redundant slashes, and handles path traversal attacks in one step.
/// - Kind and TargetMode are inferred from the path structure, matching the
///   logic in the bash scripts (resolve_incoming_target / resolve_promote_target).
/// </summary>
public static class TargetValidator
{
    // HashSet for O(1) membership checks instead of O(n) array searches.
    private static readonly HashSet<string> VideoExtensions = [".mkv", ".mp4", ".m4v"];
    private static readonly HashSet<string> ValidKindSegments = ["tv", "movies", "clips"];

    /// <summary>
    /// Validates an incoming target for handbrake/pipeline commands.
    /// Target must be a subfolder or file under <paramref name="incomingRoot"/>.
    /// </summary>
    public static TargetValidationResult ValidateIncoming(string target, string incomingRoot) =>
        Validate(target, Normalize(incomingRoot));

    /// <summary>
    /// Validates a staging target for normalize/promote commands.
    /// Target must be a subfolder or file under <paramref name="stagingRoot"/>.
    /// </summary>
    public static TargetValidationResult ValidateStaging(string target, string stagingRoot) =>
        Validate(target, Normalize(stagingRoot));

    /// <summary>
    /// Derives the staging handoff path that normalize/promote receive when the full
    /// pipeline runs. The pipeline receives an incoming target; after handbrake runs,
    /// the orchestrator must pass the corresponding staging path to the next steps.
    ///
    /// For directory targets: replaces the incoming root with the staging root.
    /// For single-file targets: returns the staging kind directory instead of the file,
    ///   because the output filename ({stem}.norm.mp4) is not known until handbrake runs.
    /// </summary>
    public static string DeriveHandoffTarget(
        ValidatedTarget validated,
        string incomingRoot,
        string stagingRoot)
    {
        var normalizedIncoming = Normalize(incomingRoot);
        var normalizedStaging  = Normalize(stagingRoot);

        // relative is e.g. "tv/My Show" or "movies/Alien.mkv"
        var relative = validated.AbsolutePath[normalizedIncoming.Length..].TrimStart('/');

        if (validated.Mode == TargetMode.File)
        {
            // For a file like "movies/Alien.mkv", the handoff is just "movies" (kind dir)
            var kindSegment = relative.Split('/')[0];
            return Path.Combine(normalizedStaging, kindSegment);
        }

        return Path.Combine(normalizedStaging, relative);
    }

    // -------------------------------------------------------------------------

    private static TargetValidationResult Validate(string target, string normalizedRoot)
    {
        string normalizedTarget;
        try
        {
            normalizedTarget = Normalize(target);
        }
        catch (Exception ex)
        {
            return TargetValidationResult.Fail($"Invalid path: {target} ({ex.Message})");
        }

        // Refuse the root itself — must be a subfolder or file (prevents accidental "run everything")
        if (string.Equals(normalizedTarget, normalizedRoot, StringComparison.Ordinal))
            return TargetValidationResult.Fail(
                $"Target must be a subfolder or file under the root, not the root itself. Got: {target}");

        // Ensure target is under root — Path.GetFullPath already resolves ".." so this
        // also blocks path traversal: "/incoming/../etc/passwd" normalizes to "/etc/passwd"
        // which won't start with "/incoming/".
        var rootWithSep = normalizedRoot.TrimEnd('/') + '/';
        if (!normalizedTarget.StartsWith(rootWithSep, StringComparison.Ordinal))
            return TargetValidationResult.Fail(
                $"Target must be under {normalizedRoot}. Got: {target}");

        // Extract path segments relative to root (e.g. ["tv", "My Show"])
        var relative = normalizedTarget[rootWithSep.Length..];
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return TargetValidationResult.Fail(
                $"Target must specify a subfolder or file under the root. Got: {target}");

        // First segment must be a known kind
        var kindSegment = segments[0].ToLowerInvariant();
        if (!ValidKindSegments.Contains(kindSegment))
            return TargetValidationResult.Fail(
                $"Target path must begin with 'tv', 'movies', or 'clips' after the root. Got: '{segments[0]}'");

        var kind = kindSegment switch
        {
            "tv"     => Kind.Tv,
            "movies" => Kind.Movies,
            _        => Kind.Clips
        };

        return kind == Kind.Tv
            ? ValidateTvTarget(normalizedTarget, segments, target)
            : ValidateMoviesOrClipsTarget(normalizedTarget, kind, segments, target);
    }

    private static TargetValidationResult ValidateTvTarget(
        string normalizedTarget, string[] segments, string originalTarget)
    {
        // TV must be exactly: <root>/tv/<Show Name>  — one level, no deeper
        if (segments.Length < 2)
            return TargetValidationResult.Fail(
                $"TV target must include a show name: <root>/tv/<Show Name>. Got: {originalTarget}");

        if (segments.Length > 2)
            return TargetValidationResult.Fail(
                $"TV target must be exactly one level deep: <root>/tv/<Show Name>. Got: {originalTarget}");

        return TargetValidationResult.Ok(new ValidatedTarget(Kind.Tv, TargetMode.Dir, normalizedTarget));
    }

    private static TargetValidationResult ValidateMoviesOrClipsTarget(
        string normalizedTarget, Kind kind, string[] segments, string originalTarget)
    {
        // Movies/Clips accept the kind base dir, any subdir, or a single video file.
        // TargetMode is inferred from the file extension of the last path segment.
        var mode = segments.Length >= 2 &&
                   VideoExtensions.Contains(Path.GetExtension(segments[^1]).ToLowerInvariant())
            ? TargetMode.File
            : TargetMode.Dir;

        return TargetValidationResult.Ok(new ValidatedTarget(kind, mode, normalizedTarget));
    }

    // Normalize a path: resolve "..", collapse redundant slashes, strip trailing slash.
    private static string Normalize(string path) =>
        Path.GetFullPath(path.TrimEnd('/'));
}
