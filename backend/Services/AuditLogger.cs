using AssetManagement.Data;
using AssetManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Services;

public interface IAuditLogger
{
    Task LogAsync(User actor, string action, string entityType, string entityId, string summary, string details);
}

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
        var lastLog = await _context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.Timestamp)
            .FirstOrDefaultAsync();

        if (lastLog is not null
            && timestamp - lastLog.Timestamp <= TimeSpan.FromSeconds(10)
            && lastLog.ActorUserId == actor.Id
            && lastLog.Action == action
            && lastLog.EntityType == entityType
            && lastLog.EntityId == entityId
            && lastLog.Summary == summary
            && lastLog.Details == details)
        {
            return;
        }

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
