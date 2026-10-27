using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record CalendarEntryRequest(
    [Required][StringLength(10)] string DueDate,
    [StringLength(50)] string? SeiNumber,
    [Required][StringLength(200)] string Subject,
    [StringLength(5000)] string? Notes,
    [StringLength(2000)] string? SeiLink = null);

public record CalendarEntryDto(
    int Id,
    string DueDate,
    string SeiNumber,
    string SeiProcessId,
    string Subject,
    string Notes,
    int CreatedByUserId,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
