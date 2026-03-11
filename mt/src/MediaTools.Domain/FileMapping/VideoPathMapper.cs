namespace MediaTools.Domain.FileMapping;

/// <summary>
/// Pure static helpers for mapping video file paths between pipeline roots.
///
/// Design notes:
/// - All methods are pure (no I/O, no filesystem access).
/// - Shared by the native handbrake runner today; normalize and promote native
///   implementations can reuse these mappings when they're implemented.
/// - The output extension is always .mp4 for handbrake (the script is handbrake_mp4).
/// - Path.GetFullPath() is NOT used here because these methods operate on arbitrary
///   root combinations; callers are expected to pass pre-validated, normalized paths.
/// </summary>
public static class VideoPathMapper
{
    // The .norm.mp4 suffix is the handoff contract between step 1 (handbrake) and
    // step 2 (normalize_audio). The normalize script searches for *.norm.mp4 files
    // and renames them to *.mp4 after processing. Promote then operates on plain *.mp4.
    private const string NormMp4Extension = ".norm.mp4";

    /// <summary>
    /// Maps an incoming video file path to its expected staging output path.
    ///
    /// The output uses a .norm.mp4 extension so the normalize_audio script can
    /// distinguish "awaiting normalization" files from already-normalized ones.
    ///
    /// Example:
    ///   input:        /incoming/movies/Alien.mkv
    ///   incomingRoot: /incoming
    ///   stagingRoot:  /staging
    ///   → /staging/movies/Alien.norm.mp4
    ///
    /// Example (TV):
    ///   input:        /incoming/tv/Breaking Bad/Season 1/S01E01.mkv
    ///   incomingRoot: /incoming
    ///   stagingRoot:  /staging
    ///   → /staging/tv/Breaking Bad/Season 1/S01E01.norm.mp4
    /// </summary>
    public static string MapHandbrakeOutput(string inputFile, string incomingRoot, string stagingRoot)
    {
        var relative = Path.GetRelativePath(
            incomingRoot.TrimEnd(Path.DirectorySeparatorChar),
            inputFile);

        // Path.ChangeExtension replaces from the last dot onward, so
        // "Movie.mkv" → "Movie.norm.mp4" as intended.
        var withNormExt = Path.ChangeExtension(relative, NormMp4Extension);
        return Path.Combine(stagingRoot.TrimEnd(Path.DirectorySeparatorChar), withNormExt);
    }

    /// <summary>
    /// Maps a staging file path to its expected library output path.
    /// Used by the promote step when it gains a native implementation.
    ///
    /// Example:
    ///   input:       /staging/movies/Alien.mp4
    ///   stagingRoot: /staging
    ///   libraryRoot: /library
    ///   → /library/movies/Alien.mp4
    /// </summary>
    public static string MapPromoteOutput(string stagingFile, string stagingRoot, string libraryRoot)
    {
        var relative = Path.GetRelativePath(
            stagingRoot.TrimEnd(Path.DirectorySeparatorChar),
            stagingFile);

        return Path.Combine(libraryRoot.TrimEnd(Path.DirectorySeparatorChar), relative);
    }
}
