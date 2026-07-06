using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record MovementCreateRequest(
    int ItemId,
    int Quantity,
    [Required][StringLength(200)] string FromLocation,
    [Required][StringLength(50)] string DestinationType,
    [StringLength(200)] string? ToLocation,
    [StringLength(200)] string? DestinationPerson,
    [StringLength(100)] string? Condition,
    bool IsDischarged,
    [StringLength(5000, ErrorMessage = "As observações da transferência excedem o limite de 5000 caracteres.")] string? Notes);

public record ItemHistoryEntryDto(
    string Kind,
    int Id,
    DateTime CreatedAt,
    string PerformedByUserName,
    int? Quantity,
    string? FromLocation,
    string? ToLocation,
    string? FromCondition,
    string? ToCondition,
    bool? FromIsDischarged,
    bool? ToIsDischarged,
    string? DestinationType,
    string? DestinationPerson,
    string? OriginPerson,
    string? Notes,
    string? Action,
    string? Details
);
