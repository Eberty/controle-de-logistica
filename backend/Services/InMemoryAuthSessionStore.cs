using System.Collections.Concurrent;
using System.Security.Cryptography;
using AssetManagement.Data;
using AssetManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Services;

public class InMemoryAuthSessionStore : IAuthSessionStore
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(12);
    private const int TokenByteSize = 32;
    private const int TokenLength = 43;

    private readonly record struct SessionEntry(int UserId, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public InMemoryAuthSessionStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string CreateSession(User user)
    {
        PurgeExpiredSessions();
        var token = GenerateToken();
        _sessions[token] = new SessionEntry(user.Id, DateTime.UtcNow.Add(SessionTtl));
        return token;
    }

    public async Task<User?> TryGetUserAsync(string token)
    {
        if (!IsValidTokenFormat(token) || !_sessions.TryGetValue(token, out var entry))
            return null;

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entry.UserId);
    }

    public void RemoveSession(string token)
    {
        _sessions.TryRemove(token, out _);
    }

    public void RemoveSessionsByUserId(int userId, string? exceptToken = null)
    {
        foreach (var (key, entry) in _sessions)
        {
            if (entry.UserId == userId && !string.Equals(key, exceptToken, StringComparison.Ordinal))
                _sessions.TryRemove(key, out _);
        }
    }

    private void PurgeExpiredSessions()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _sessions)
        {
            if (entry.ExpiresAt <= now)
                _sessions.TryRemove(key, out _);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteSize);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsValidTokenFormat(string token)
    {
        return token.Length == TokenLength
            && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    }
}
