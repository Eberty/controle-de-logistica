using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record UserNoteRequest(
    [Required][StringLength(200)] string Title,
    [Required][StringLength(10000)] string Content,
    [StringLength(500)] string? Tags,
    bool? IsPublic = null);

public record MuralNoteDto(
    int Id,
    string Title,
    string Content,
    string Tags,
    int AuthorUserId,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
