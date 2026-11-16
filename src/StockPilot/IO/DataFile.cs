namespace StockPilot;

public static class DataFile
{
    /// <summary>Reads a file and parses it. Nothing is changed by this method (read-only), so a failure leaves no partial state.
    /// FileMissing: no such file/folder. IoError: file cannot be read. MalformedData: invalid JSON.
    /// Every failure names the file.</summary>
    public static Result<T> Load<T>(string path, Func<string, T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (string.IsNullOrWhiteSpace(path))
            return Result.Fail<T>(ErrorCodes.IoError, "File path is empty.");

        string text;
        try
        {
            text = File.ReadAllText(path); // convenience API: opens and closes the file itself
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Result.Fail<T>(ErrorCodes.FileMissing, $"File not found: '{path}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<T>(ErrorCodes.IoError, $"Cannot read '{path}': {ex.Message}");
        }

        try
        {
            return Result.Ok(parse(text));
        }
        catch (JsonException ex)
        {
            return Result.Fail<T>(ErrorCodes.MalformedData, $"Malformed JSON in '{path}': {ex.Message}");
        }
    }
}
