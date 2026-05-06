namespace AssetManagement.Models;

public class Movement
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int? DestinationItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string MovementType { get; set; } = "Transferência";
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public string FromCondition { get; set; } = string.Empty;
    public string ToCondition { get; set; } = string.Empty;
    public bool FromIsDischarged { get; set; }
    public bool ToIsDischarged { get; set; }
    public string DestinationType { get; set; } = "Local";
    public string DestinationPerson { get; set; } = string.Empty;
    public string OriginPerson { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int PerformedByUserId { get; set; }
    public string PerformedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
