namespace AssetManagement.Models;

public class UserNoteRecipient
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CompletionSeenAt { get; set; }
}
