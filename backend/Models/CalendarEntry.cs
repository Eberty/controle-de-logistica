namespace AssetManagement.Models;

public class CalendarEntry
{
    public int Id { get; set; }
    public string DueDate { get; set; } = string.Empty;
    public string SeiNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
