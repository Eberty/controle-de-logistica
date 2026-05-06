using System.ComponentModel.DataAnnotations;

namespace AssetManagement.Dtos;

public record AuthLoginRequest(
    [Required][StringLength(100)] string Username,
    [Required][StringLength(200)] string Password);

public record InitialAdminRequest([Required][StringLength(200)] string Password);

public record AuthRegisterRequest(
    [Required]
    [StringLength(100)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "O usuário deve conter apenas letras, números, ponto, hífen ou underline.")]
    string Username,
    [Required][StringLength(200)] string Password,
    [Required][StringLength(200)] string FullName,
    bool IsAdmin,
    [Required][StringLength(50)] string MilitaryId,
    [Required][StringLength(100)] string AdminUsername,
    [Required][StringLength(200)] string AdminPassword);

public record UserResponse(
    int Id,
    string Username,
    string FullName,
    string Role,
    string MilitaryId);

public record UserUpdateRequest(
    [Required]
    [StringLength(100)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "O usuário deve conter apenas letras, números, ponto, hífen ou underline.")]
    string Username,
    [Required][StringLength(200)] string FullName,
    [Required][StringLength(50)] string MilitaryId,
    bool IsAdmin,
    [StringLength(200)] string? CurrentPassword,
    [StringLength(200)] string? Password,
    [StringLength(200)] string? AdminPassword);

public record AuthLoginResponse(string Token, UserResponse User);
