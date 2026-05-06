using AssetManagement.Models;

namespace AssetManagement.Services;

public interface IAuditLogger
{
    Task LogAsync(User actor, string action, string entityType, string entityId, string summary, string details);
}
