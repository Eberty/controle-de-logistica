namespace AssetManagement.DTOs;

public record UserNoteRequest(string Title, string Content, string? Tags);
