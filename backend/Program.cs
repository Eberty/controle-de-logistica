using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using AssetManagement.Data;
using AssetManagement.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstError = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .SelectMany(kvp => kvp.Value!.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Dados inválidos.";
            return new BadRequestObjectResult(new { message = firstError });
        };
    });
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IAuthSessionStore, InMemoryAuthSessionStore>();
builder.Services.Configure<PhotoStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddScoped<PhotoService>();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection para usar PostgreSQL.");
        options.UseNpgsql(connectionString);
        return;
    }

    if (!string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Database:Provider deve ser Sqlite ou PostgreSQL.");

    var databaseDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
    Directory.CreateDirectory(databaseDirectory);

    var connectionStringSqlite = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? $"Data Source={Path.Combine(databaseDirectory, "asset-management.db")}";
    options.UseSqlite(connectionStringSqlite);
});

var photoStorageProvider = builder.Configuration.GetSection("Storage").Get<PhotoStorageOptions>()?.Provider ?? "Local";
if (string.Equals(photoStorageProvider, "S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IPhotoStorage, S3PhotoStorage>();
else if (string.Equals(photoStorageProvider, "Local", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();
else
    throw new InvalidOperationException("Storage:Provider deve ser Local ou S3.");

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy
                .SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
                .AllowAnyHeader()
                .AllowAnyMethod();
        else if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        // Otherwise, production keeps cross-origin access blocked unless origins are configured.
    });
});

var trustForwardedHeaders = builder.Configuration.GetValue<bool>("Proxy:TrustForwardedHeaders");
var knownProxyNetworks = builder.Configuration.GetSection("Proxy:KnownNetworks").Get<string[]>() ?? [];
var useForwardedHeaders = trustForwardedHeaders || knownProxyNetworks.Length > 0;
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in knownProxyNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
                options.KnownIPNetworks.Add(parsedNetwork);
        }
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        }));
    options.RejectionStatusCode = 429;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await AssetManagement.Services.DatabaseInitializer.InitializeAsync(context);
}

if (useForwardedHeaders)
    app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self';";
    if (!app.Environment.IsDevelopment())
        context.Response.Headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
    await next();
});

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors("frontend");
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { message = "Ocorreu um erro inesperado. Tente novamente." });
        }
    }
});

app.MapControllers();

app.Run();
