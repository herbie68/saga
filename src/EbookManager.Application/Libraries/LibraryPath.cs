namespace EbookManager.Libraries;

internal static class LibraryPath
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Canonicalize(string directoryPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));

    public static bool Equals(string left, string right) =>
        string.Equals(Canonicalize(left), Canonicalize(right), Comparison);
}
