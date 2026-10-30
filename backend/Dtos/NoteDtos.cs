using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record UserNoteRequest(
    [Required][StringLength(200)] string Title,
    [Required][StringLength(10000)] string Content,
    string[]? Tags,
    [StringLength(100, ErrorMessage = "O número do SEI excede o limite de 100 caracteres.")] string? SeiNumber,
    int[]? RecipientUserIds,
    string? DueDate = null,
    [StringLength(2000)] string? SeiLink = null);

public record NoteRecipientDto(
    int UserId,
    string Name,
    DateTime? ReadAt,
    DateTime? CompletedAt,
    bool CompletionSeen,
    string ForwardedByName);

public record UserNoteDto(
    int Id,
    string Title,
    string Content,
    string Tags,
    string SeiNumber,
    string SeiProcessId,
    string DueDate,
    int AuthorUserId,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<NoteRecipientDto> Recipients,
    DateTime? ConcludedAt,
    string ConcludedByName);

public record ReceivedNoteDto(
    int Id,
    string Title,
    string Content,
    string Tags,
    string SeiNumber,
    string SeiProcessId,
    string DueDate,
    int AuthorUserId,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ReadAt,
    DateTime? CompletedAt,
    string ForwardedByName,
    IReadOnlyList<NoteRecipientDto> Forwards);

public record MuralResponse(
    IReadOnlyList<UserNoteDto> Sent,
    IReadOnlyList<ReceivedNoteDto> Received,
    IReadOnlyList<UserNoteDto> Concluded);

public record ForwardNoteRequest(int[]? RecipientUserIds);

public record MuralSummaryDto(int UnreadReceived, int UnseenCompletions);

public record NoteRecipientOptionDto(int Id, string Name);

public record NoteTagRequest(
    [Required][StringLength(50, ErrorMessage = "O nome da etiqueta excede o limite de 50 caracteres.")] string Name,
    [Required] string Color);

public record NoteTagDto(int Id, string Name, string Color);
