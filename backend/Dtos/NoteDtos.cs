using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record UserNoteRequest(
    [Required][StringLength(200)] string Title,
    [Required][StringLength(10000)] string Content,
    [StringLength(500)] string? Tags);
