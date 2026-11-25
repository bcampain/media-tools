using System.Text.RegularExpressions;

namespace MediaTools.Domain.FileMapping;

/// <summary>
/// Pure helpers for cleaning up incoming filenames before the pipeline processes them.
/// to avoid issues in SRT external subtitle processing
/// </summary>
public static partial class FileNameSanitizer
{
    // Strip commas and trim superfluous whitespace
    [GeneratedRegex(@"\s*(,\s*)+")]
    private static partial Regex CommaRun();

    /// <summary>
    /// Returns <paramref name="fileName"/> with commas (and the whitespace around them)
    /// collapsed to a single space, trimming any space left at the edges of the stem.
    /// Returns the input unchanged when it contains no commas.
    ///
    /// Example: "S01E01 - Hello, World.mkv" → "S01E01 - Hello World.mkv"
    /// </summary>
    public static string RemoveCommas(string fileName)
    {
        // Fast path: most files have no commas, so skip the regex entirely.
        if (!fileName.Contains(',')) return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext  = Path.GetExtension(fileName);   // includes the dot, e.g. ".mkv"

        return CommaRun().Replace(stem, " ").Trim() + ext;
    }
}
