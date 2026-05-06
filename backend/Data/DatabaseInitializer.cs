using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Data;

public static class DatabaseInitializer
{
    public static void Initialize(AppDbContext context)
    {
        context.Database.EnsureCreated();
        EnsureUserNotesSchema(context);
        EnsureItemsSchema(context);
        EnsureLocationOptionsSchema(context);
    }

    private static void EnsureUserNotesSchema(AppDbContext context)
    {
        EnsureColumn(context, "UserNotes", "Title", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "UserNotes", "Tags", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "UserNotes", "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");

        context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_UserNotes_UserId ON UserNotes (UserId)");
        context.Database.ExecuteSqlRaw("UPDATE UserNotes SET Title = 'Anotação' WHERE Title = ''");
    }

    private static void EnsureItemsSchema(AppDbContext context)
    {
        EnsureColumn(context, "Items", "PhotoFileName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "Items", "PhotoContentType", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "Items", "IsDischarged", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(context, "Items", "DischargedAt", "TEXT NULL");
    }

    private static void EnsureLocationOptionsSchema(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS LocationOptions (
                Id INTEGER NOT NULL CONSTRAINT PK_LocationOptions PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            )
            """);
        context.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_LocationOptions_Name ON LocationOptions (Name)");
    }

    private static void EnsureColumn(AppDbContext context, string tableName, string columnName, string columnDefinition)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State == System.Data.ConnectionState.Closed;
        if (shouldClose)
            connection.Open();

        try
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({tableName})";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
            alterCommand.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose)
                connection.Close();
        }
    }
}
