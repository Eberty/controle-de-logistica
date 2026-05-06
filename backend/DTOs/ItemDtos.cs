namespace AssetManagement.DTOs;

public record ItemUpsertRequest(
    string Name,
    int Quantity,
    string AssetTag,
    string Nature,
    string Location,
    string Condition,
    string Notes,
    string? PhotoDataUrl,
    bool RemovePhoto,
    bool IsDischarged);
