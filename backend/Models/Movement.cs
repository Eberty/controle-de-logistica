namespace AssetManagement.Models;

public class Movement
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string MovementType { get; set; } = "Transfer";
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public string DestinationType { get; set; } = "Local";
    public string DestinationPerson { get; set; } = string.Empty;
    public string OriginPerson { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int PerformedByUserId { get; set; }
    public string PerformedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
