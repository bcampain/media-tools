using MediaTools.Domain.Validation;

namespace MediaTools.App.FileSystem;

/// <summary>
/// Discovers video files under a root path up to a configurable depth.
/// Shared by the native Handbrake runner today; normalize and promote native
/// implementations will reuse this when they're built.
///
/// Design notes:
/// - Files are returned sorted (by full path) so processing order is deterministic
///   and reproducible across runs.
/// - Hidden files and directories (names starting with '.') are skipped to match
///   the bash script behaviour (which uses find with default settings that skip
///   dotfiles when properly filtered).
/// - maxDepth mirrors the --max-depth argument of the original handbrake_mp4 script.
///   Depth 1 = only files directly inside rootPath.
///   Depth 2 = files inside rootPath and one level of subdirectory, etc.
/// </summary>
public class VideoFileScanner
{
    // Delegate to TargetValidator so the set of recognised extensions stays in
    // one place. Both validation (what the user can pass as --target) and
    // discovery (what files we scan) agree on which formats are supported.
    private static readonly HashSet<string> VideoExtensions = TargetValidator.VideoExtensions;

    /// <summary>
    /// Scans <paramref name="rootPath"/> recursively up to <paramref name="maxDepth"/>
    /// and returns a sorted list of video file paths.
    /// </summary>
    public IReadOnlyList<string> Scan(string rootPath, int maxDepth)
    {
        if (!Directory.Exists(rootPath))
            return [];

        var results = new List<string>();
        ScanDirectory(rootPath, currentDepth: 1, maxDepth, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void ScanDirectory(
        string directory,
        int currentDepth,
        int maxDepth,
        List<string> results)
    {
        // Enumerate files at this level
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            // Skip hidden files
            if (Path.GetFileName(file).StartsWith('.'))
                continue;

            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (VideoExtensions.Contains(ext))
                results.Add(file);
        }

        // Recurse into subdirectories if we haven't hit maxDepth
        if (currentDepth >= maxDepth)
            return;

        foreach (var subDir in Directory.EnumerateDirectories(directory))
        {
            // Skip hidden directories (e.g. .Trashes, .TemporaryItems)
            if (Path.GetFileName(subDir).StartsWith('.'))
                continue;

            ScanDirectory(subDir, currentDepth + 1, maxDepth, results);
        }
    }

    /// <summary>
    /// Convenience: scans a single file path, validating it's a video file.
    /// Returns a single-element list if valid, empty list otherwise.
    /// Used when the target is a file rather than a directory.
    /// </summary>
    public IReadOnlyList<string> ScanSingleFile(string filePath)
    {
        // Check the cheap extension guard before the I/O call.
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (!VideoExtensions.Contains(ext))
            return [];

        return File.Exists(filePath) ? [filePath] : [];
    }
}
