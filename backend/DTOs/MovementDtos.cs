namespace AssetManagement.DTOs;

public record MovementCreateRequest(
    int ItemId,
    int Quantity,
    string FromLocation,
    string DestinationType,
    string? ToLocation,
    string? DestinationPerson,
    string? Notes);
