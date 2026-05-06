namespace AssetManagement.DTOs;

public record AuthLoginRequest(string Username, string Password);

public record InitialAdminRequest(string Password);

public record AuthRegisterRequest(
    string Username,
    string Password,
    string FullName,
    bool IsAdmin,
    string MilitaryId,
    string AdminUsername,
    string AdminPassword);

public record UserResponse(
    int Id,
    string Username,
    string FullName,
    string Role,
    string MilitaryId);

public record UserUpdateRequest(
    string Username,
    string FullName,
    string MilitaryId,
    bool IsAdmin,
    string? CurrentPassword,
    string? Password,
    string? AdminPassword);

public record AuthLoginResponse(string Token, UserResponse User);
