namespace AssetManagement.Models;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string AssetTag { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string PhotoFileName { get; set; } = string.Empty;
    public string PhotoContentType { get; set; } = string.Empty;
    public bool IsDischarged { get; set; }
    public DateTime? DischargedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
