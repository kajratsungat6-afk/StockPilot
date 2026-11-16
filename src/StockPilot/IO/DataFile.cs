namespace StockPilot;

public static class DataFile
{

    public static Result<T> Load<T>(string path, Func<string, T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (string.IsNullOrWhiteSpace(path))
            return Result.Fail<T>(ErrorCodes.IoError, "File path is empty.");

        string text;
        try
        {
            text = File.ReadAllText(path); 
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
