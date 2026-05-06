using System.Collections.Concurrent;
using AssetManagement.Data;
using AssetManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Services;

public class InMemoryAuthSessionStore : IAuthSessionStore
{
    private readonly ConcurrentDictionary<string, int> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public InMemoryAuthSessionStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string CreateSession(User user)
    {
        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = user.Id;
        return token;
    }

    public bool TryGetUser(string token, out User? user)
    {
        user = null;

        if (!_sessions.TryGetValue(token, out var userId))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        user = context.Users.AsNoTracking().FirstOrDefault(x => x.Id == userId);
        return user != null;
    }

    public void RemoveSession(string token)
    {
        _sessions.TryRemove(token, out _);
    }
}
