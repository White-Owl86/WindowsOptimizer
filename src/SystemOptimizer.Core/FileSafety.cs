namespace SystemOptimizer.Core;

public static class FileSafety
{
    public static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsWithin(string path, string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string full = Path.GetFullPath(path);
        return full.Equals(root, PathComparison) || full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    public static void RejectLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Symbolic links/junctions are not allowed: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}

public record CleanupResult(int Deleted, int Skipped, int Failed, long BytesDeleted);

public static class TempCleaner
{
    public static CleanupResult Clean(string root, DateTime cutoffUtc, IReadOnlyList<string>? protectedPaths = null)
    {
        string fullRoot = Path.GetFullPath(root);
        if (Path.TrimEndingDirectorySeparator(fullRoot).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullRoot)!), FileSafety.PathComparison))
            throw new ArgumentException("Refusing to clean a drive/filesystem root.", nameof(root));
        FileSafety.RejectLinks(fullRoot);
        if (!Directory.Exists(fullRoot)) return new(0, 0, 0, 0);
        int deleted = 0, skipped = 0, failed = 0;
        long bytes = 0;
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.TryPop(out var directory))
        {
            if (protectedPaths?.Any(path => FileSafety.IsWithin(directory, path)) == true)
            { skipped++; continue; }
            try
            {
                FileSafety.RejectLinks(directory);
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    try
                    {
                        FileSafety.RejectLinks(entry);
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                        if (protectedPaths?.Any(path => FileSafety.IsWithin(entry, path)) == true ||
                            File.GetLastWriteTimeUtc(entry) >= cutoffUtc)
                        { skipped++; continue; }
                        long length = new FileInfo(entry).Length;
                        File.Delete(entry);
                        deleted++;
                        bytes += length;
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { failed++; }
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { failed++; }
        }
        return new(deleted, skipped, failed, bytes);
    }
}
