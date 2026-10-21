namespace AssetManagement.Models;

public class UserNote
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string SeiNumber { get; set; } = string.Empty;
    // Legacy column from the public mural; always false now. Existing SQLite databases keep it as NOT NULL without a default.
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
