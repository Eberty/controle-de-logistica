using AssetManagement.Models;

namespace AssetManagement.Services;

public interface IAuthSessionStore
{
    string CreateSession(User user);
    bool TryGetUser(string token, out User? user);
    void RemoveSession(string token);
}
