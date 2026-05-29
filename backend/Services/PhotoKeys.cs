namespace AssetManagement.Services;

public static class PhotoKeys
{
    public static string? TryGetSafeFileName(string? key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/');
        var fileName = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
            return null;

        return fileName;
    }

    public static string GetSafeFileName(string key) =>
        TryGetSafeFileName(key) ?? throw new InvalidOperationException("Identificador da foto inválido.");
}
