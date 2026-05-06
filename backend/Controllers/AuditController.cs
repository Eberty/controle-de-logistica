using AssetManagement.Data;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController : AuthenticatedControllerBase
{
    private const int MaxTimelineLimit = 5000;

    public AuditController(AppDbContext context, IAuthSessionStore sessionStore)
        : base(context, sessionStore)
    {
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] int? limit)
    {
        var (ok, _, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var query = Context.AuditLogs.AsNoTracking();

        if (start.HasValue)
            query = query.Where(x => x.Timestamp >= start.Value);

        if (end.HasValue)
            query = query.Where(x => x.Timestamp <= end.Value);

        query = query.OrderByDescending(x => x.Timestamp);

        var requestedLimit = (limit.HasValue && limit.Value > 0) ? limit.Value : MaxTimelineLimit;
        var effectiveLimit = Math.Min(requestedLimit, MaxTimelineLimit);
        query = query.Take(effectiveLimit);

        var logs = await query.ToListAsync();

        return Ok(logs);
    }
}
