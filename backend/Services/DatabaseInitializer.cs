using System.Data.Common;
using AssetManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext context)
    {
        await context.Database.EnsureCreatedAsync();

        if (!context.Database.IsSqlite())
            return;

        await EnsureTablesAsync(context);

        await EnsureColumnAsync(context, "Users", "FullName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(context, "Users", "Role", "TEXT NOT NULL DEFAULT 'User'");
        await EnsureColumnAsync(context, "Users", "MilitaryId", "TEXT NOT NULL DEFAULT ''");

        await EnsureColumnAsync(context, "Items", "PhotoFileName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(context, "Items", "PhotoContentType", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(context, "Items", "IsDischarged", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(context, "Items", "DischargedAt", "TEXT NULL");
        await EnsureColumnAsync(context, "Items", "ResponsiblePerson", "TEXT NULL");

        await EnsureColumnAsync(context, "Movements", "FromCondition", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(context, "Movements", "ToCondition", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(context, "Movements", "FromIsDischarged", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(context, "Movements", "ToIsDischarged", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(context, "Movements", "DestinationItemId", "INTEGER NULL");

        await context.Database.ExecuteSqlRawAsync("""
            INSERT OR IGNORE INTO AppLocks (Name, TouchedAt)
            VALUES ('Items', CURRENT_TIMESTAMP);
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username_NoCase
            ON Users (Username COLLATE NOCASE);
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_LocationOptions_Name_NoCase
            ON LocationOptions (Name COLLATE NOCASE);
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS IX_Movements_ItemId
            ON Movements (ItemId);
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS IX_Movements_DestinationItemId
            ON Movements (DestinationItemId);
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS IX_UserNotes_UserId
            ON UserNotes (UserId);
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS IX_AuditLogs_ActorUserId_EntityType_EntityId
            ON AuditLogs (ActorUserId, EntityType, EntityId);
            """);
    }

    private static async Task EnsureTablesAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                FullName TEXT NOT NULL DEFAULT '',
                Role TEXT NOT NULL DEFAULT 'User',
                MilitaryId TEXT NOT NULL DEFAULT ''
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Items (
                Id INTEGER NOT NULL CONSTRAINT PK_Items PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                AssetTag TEXT NOT NULL,
                Nature TEXT NOT NULL,
                Location TEXT NOT NULL,
                Condition TEXT NOT NULL,
                Notes TEXT NOT NULL,
                PhotoFileName TEXT NOT NULL DEFAULT '',
                PhotoContentType TEXT NOT NULL DEFAULT '',
                IsDischarged INTEGER NOT NULL DEFAULT 0,
                DischargedAt TEXT NULL,
                ResponsiblePerson TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Movements (
                Id INTEGER NOT NULL CONSTRAINT PK_Movements PRIMARY KEY AUTOINCREMENT,
                ItemId INTEGER NOT NULL,
                DestinationItemId INTEGER NULL,
                ItemName TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                MovementType TEXT NOT NULL,
                FromLocation TEXT NOT NULL,
                ToLocation TEXT NOT NULL,
                FromCondition TEXT NOT NULL DEFAULT '',
                ToCondition TEXT NOT NULL DEFAULT '',
                FromIsDischarged INTEGER NOT NULL DEFAULT 0,
                ToIsDischarged INTEGER NOT NULL DEFAULT 0,
                DestinationType TEXT NOT NULL,
                DestinationPerson TEXT NOT NULL,
                OriginPerson TEXT NOT NULL,
                Notes TEXT NOT NULL,
                PerformedByUserId INTEGER NOT NULL,
                PerformedByUserName TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                ActorUserId INTEGER NOT NULL,
                ActorUserName TEXT NOT NULL,
                Action TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Details TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS UserNotes (
                Id INTEGER NOT NULL CONSTRAINT PK_UserNotes PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                Tags TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS LocationOptions (
                Id INTEGER NOT NULL CONSTRAINT PK_LocationOptions PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AppLocks (
                Name TEXT NOT NULL CONSTRAINT PK_AppLocks PRIMARY KEY,
                TouchedAt TEXT NOT NULL
            );
            """);
    }

    private static async Task EnsureColumnAsync(AppDbContext context, string tableName, string columnName, string definition)
    {
        var connection = context.Database.GetDbConnection();
        var closeAfterUse = connection.State != System.Data.ConnectionState.Open;

        if (closeAfterUse)
            await connection.OpenAsync();

        try
        {
            if (await ColumnExistsAsync(connection, tableName, columnName))
                return;

            var sql = $"ALTER TABLE {QuoteIdentifier(tableName)} ADD COLUMN {QuoteIdentifier(columnName)} {definition};";
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        finally
        {
            if (closeAfterUse)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (identifier.Length == 0 || identifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Identificador SQL inválido.");

        return $"\"{identifier}\"";
    }
}
