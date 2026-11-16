namespace StockPilot;

public static class ReportFile
{
    /// <summary>Writes text to a file atomically: first to "path.tmp", then moves it over the target.
    /// OWNER and RELEASE POINT: this method owns the FileStream and the StreamWriter; they are released (flushed and
    /// closed) at the end of the using block — on success AND when an exception is thrown inside it.
    /// On failure the temporary file is removed and the previous target file is untouched.
    /// openTemp lets a test supply its own stream to observe disposal.</summary>
    public static Result<string> Save(string path, string content, Func<string, FileStream>? openTemp = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(path))
            return Result.Fail<string>(ErrorCodes.IoError, "Output path is empty.");

        var temp = path + ".tmp";
        var open = openTemp ?? (file => new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None));
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using (var stream = open(temp))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }

            File.Move(temp, path, overwrite: true);
            return Result.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var note = TryDelete(temp) ? "" : " (the temporary file could not be removed)";
            return Result.Fail<string>(ErrorCodes.IoError, $"Cannot write '{path}': {ex.Message}{note}");
        }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
