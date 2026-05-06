using Microsoft.EntityFrameworkCore;
using AssetManagement.Models;

namespace AssetManagement.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserNote> UserNotes => Set<UserNote>();
    public DbSet<LocationOption> LocationOptions => Set<LocationOption>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<UserNote>().HasIndex(n => n.UserId);
        modelBuilder.Entity<LocationOption>().HasIndex(l => l.Name).IsUnique();

    }
}
