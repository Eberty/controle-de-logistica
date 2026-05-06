using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record LocationCreateRequest([Required][StringLength(200)] string Name);

public record LocationUpdateRequest(
    [Required][StringLength(200)] string CurrentName,
    [Required][StringLength(200)] string NewName);

public record LocationDeleteRequest([Required][StringLength(200)] string Name);
