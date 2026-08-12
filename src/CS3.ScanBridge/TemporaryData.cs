namespace CS3.ScanBridge;

public sealed class TemporaryData
{
    public TemporaryData(string? tempPath = null)
    {
        Root = Path.GetFullPath(Path.Combine(tempPath ?? Path.GetTempPath(), "CS3ScanBridge"));
    }

    public string Root { get; }

    public string CreateScanDirectory()
    {
        Directory.CreateDirectory(Root);
        var directory = Path.GetFullPath(Path.Combine(Root, Guid.NewGuid().ToString("N")));
        EnsureInsideRoot(directory);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void DeleteScanDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        EnsureInsideRoot(fullPath);
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
    }

    public void DeleteAbandonedDirectories(TimeSpan minimumAge)
    {
        if (!Directory.Exists(Root)) return;
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var fullPath = Path.GetFullPath(directory);
            EnsureInsideRoot(fullPath);
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(fullPath) >= minimumAge)
            {
                try { Directory.Delete(fullPath, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void EnsureInsideRoot(string path)
    {
        var rootWithSeparator = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Temporary path is outside the CS3 Scan Bridge temporary root.");
    }
}
