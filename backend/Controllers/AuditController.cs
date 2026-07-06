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

        var startUtc = NormalizeToUtc(start);
        var endUtc = NormalizeEndOfDayToUtc(end);

        var query = Context.AuditLogs.AsNoTracking();

        if (startUtc.HasValue)
            query = query.Where(x => x.Timestamp >= startUtc.Value);

        if (endUtc.HasValue)
            query = query.Where(x => x.Timestamp <= endUtc.Value);

        query = query.OrderByDescending(x => x.Timestamp);

        var requestedLimit = (limit.HasValue && limit.Value > 0) ? limit.Value : MaxTimelineLimit;
        var effectiveLimit = Math.Min(requestedLimit, MaxTimelineLimit);
        query = query.Take(effectiveLimit);

        var logs = await query.ToListAsync();

        return Ok(logs);
    }

    private static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static DateTime? NormalizeEndOfDayToUtc(DateTime? value)
    {
        var normalized = NormalizeToUtc(value);
        if (!normalized.HasValue)
            return null;

        return normalized.Value.TimeOfDay == TimeSpan.Zero
            ? normalized.Value.AddDays(1).AddTicks(-1)
            : normalized.Value;
    }
}
