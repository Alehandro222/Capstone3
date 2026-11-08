using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;

namespace TrackerApi.Services;

public static class DbSaveExtensions
{
    // Saves, turning a broken database rule into a 409 Conflict with a clear message.
    // Example: two requests at the same moment both try to start a session on PC-04;
    // the "one active session per PC" index refuses the second one.
    public static async Task SaveOrConflictAsync(this TrackerDbContext db, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw TrackerException.Conflict(message);
        }
    }
}
