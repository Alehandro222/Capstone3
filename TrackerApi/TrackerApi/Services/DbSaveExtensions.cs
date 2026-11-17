using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrackerApi.Data;

namespace TrackerApi.Services;

public static class DbSaveExtensions
{
    // Saves, turning a broken database rule into a 409 Conflict with a clear message.
    // Example: two requests at the same moment both try to start a session on PC-04;
    // the "one active session per PC" index refuses the second one.
    // Only a unique-index clash becomes 409; any other database error (a dropped connection,
    // a timeout) is thrown as it is, so it is logged and not mistaken for "just taken".
    public static async Task SaveOrConflictAsync(this TrackerDbContext db, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw TrackerException.Conflict(message);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException pg => pg.SqlState == PostgresErrorCodes.UniqueViolation,
        SqliteException lite => lite.SqliteExtendedErrorCode is 2067 or 1555, // UNIQUE, PRIMARY KEY
        _ => false
    };
}
