using AssetManagement.Models;

namespace AssetManagement.Services;

public interface IAuthSessionStore
{
    string CreateSession(User user);
    Task<User?> TryGetUserAsync(string token);
    void RemoveSession(string token);
    void RemoveSessionsByUserId(int userId, string? exceptToken = null);
}
