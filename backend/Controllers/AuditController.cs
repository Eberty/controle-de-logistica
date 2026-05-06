using AssetManagement.Data;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController : AuthenticatedControllerBase
{
    public AuditController(AppDbContext context, IAuthSessionStore sessionStore)
        : base(context, sessionStore)
    {
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] int? limit)
    {
        if (!TryGetAdminUser(out _, out var error))
            return error!;

        var query = Context.AuditLogs.AsNoTracking();

        if (start.HasValue)
            query = query.Where(x => x.Timestamp >= start.Value);

        if (end.HasValue)
            query = query.Where(x => x.Timestamp <= end.Value);

        query = query.OrderByDescending(x => x.Timestamp);

        if (limit.HasValue)
            query = query.Take(Math.Clamp(limit.Value, 1, 500));

        var logs = await query.ToListAsync();

        return Ok(logs);
    }
}
