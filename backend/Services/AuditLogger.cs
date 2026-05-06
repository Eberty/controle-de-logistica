using AssetManagement.Data;
using AssetManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Services;

public class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _context;

    public AuditLogger(AppDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(User actor, string action, string entityType, string entityId, string summary, string details)
    {
        var timestamp = DateTime.UtcNow;

        _context.AuditLogs.Add(new AuditLog
        {
            Timestamp = timestamp,
            ActorUserId = actor.Id,
            ActorUserName = actor.Username,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            Details = details
        });

        await _context.SaveChangesAsync();
    }
}
