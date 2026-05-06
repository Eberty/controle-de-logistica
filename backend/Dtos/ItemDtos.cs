using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record ItemUpsertRequest(
    [Required][StringLength(300)] string Name,
    int Quantity,
    [StringLength(50)] string? AssetTag,
    [Required][StringLength(100)] string Nature,
    [Required][StringLength(200)] string Location,
    [Required][StringLength(100)] string Condition,
    [StringLength(5000, ErrorMessage = "O campo de observações excede o limite de 5.000 caracteres.")] string? Notes,
    // 1.5 MB in bytes is roughly 2,000,000 base64 chars plus the data URL prefix.
    // 2,500,000 leaves extra room and remains safe on 32-bit and 64-bit runtimes.
    // DecodePhotoDataUrl still enforces the final 1.5 MB byte-size limit.
    [StringLength(2_500_000, ErrorMessage = "A foto enviada excede o tamanho permitido.")] string? PhotoDataUrl,
    bool RemovePhoto,
    bool IsDischarged);
