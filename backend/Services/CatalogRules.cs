using System.Text.Json;

namespace AssetManagement.Services;

public static class CatalogRules
{
    private static readonly Lazy<CatalogOptions> Catalog = new(LoadCatalog);

    public static string? ResolveNature(string value) =>
        ResolveOption(Catalog.Value.ItemNatureOptions, value);

    public static string? ResolveCondition(string value) =>
        ResolveOption(Catalog.Value.ItemConditionOptions, value);

    public static string? ResolveDefaultLocation(string value) =>
        ResolveOption(Catalog.Value.LocationOptions, value);

    private static string? ResolveOption(IEnumerable<string> options, string value)
    {
        var trimmed = value.Trim();
        return options.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static CatalogOptions LoadCatalog()
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "shared", "catalog.json");
        if (!File.Exists(catalogPath))
            throw new InvalidOperationException("Arquivo compartilhado de catálogo não encontrado.");

        var json = File.ReadAllText(catalogPath);
        var catalog = JsonSerializer.Deserialize<CatalogOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (catalog is null
            || catalog.ItemNatureOptions.Length == 0
            || catalog.ItemConditionOptions.Length == 0
            || catalog.LocationOptions.Length == 0)
            throw new InvalidOperationException("Arquivo compartilhado de catálogo inválido.");

        return catalog;
    }

    private sealed class CatalogOptions
    {
        public string[] ItemNatureOptions { get; set; } = [];
        public string[] ItemConditionOptions { get; set; } = [];
        public string[] LocationOptions { get; set; } = [];
    }
}
