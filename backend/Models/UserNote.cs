namespace AssetManagement.Models;

public class UserNote
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string SeiNumber { get; set; } = string.Empty;
    public string SeiProcessId { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    // Legacy: conclusion is kept per recipient in UserNoteRecipients; DatabaseInitializer moves old values there.
    public DateTime? ConcludedAt { get; set; }
    public int? ConcludedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
