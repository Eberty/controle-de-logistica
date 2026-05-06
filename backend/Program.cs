using Microsoft.EntityFrameworkCore;
using AssetManagement.Data;
using AssetManagement.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IAuthSessionStore, InMemoryAuthSessionStore>();

var databaseDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(databaseDirectory);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? $"Data Source={Path.Combine(databaseDirectory, "asset-management.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DatabaseInitializer.Initialize(context);
}

app.UseCors("frontend");

app.MapControllers();

app.Run();
