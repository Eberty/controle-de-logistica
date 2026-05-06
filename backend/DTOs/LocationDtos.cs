namespace AssetManagement.DTOs;

public record LocationCreateRequest(string Name);

public record LocationUpdateRequest(string CurrentName, string NewName);

public record LocationDeleteRequest(string Name);
