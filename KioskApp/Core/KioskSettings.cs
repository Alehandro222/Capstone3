using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KioskApp.Core;

// Settings come from kiosk.settings.json (next to the .exe, edited by staff).
// Which computer this is gets saved separately in
// C:\ProgramData\ComputerTracker\computer-id.txt so it survives updates.
public sealed class KioskSettings
{
    private const string SettingsFileName = "kiosk.settings.json";
    private const string ComputerIdFileName = "computer-id.txt";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ApiUrl { get; set; } = "http://localhost:5080/";

    // Shared secret the tracker checks on every request (Kiosk:ApiKey on the server).
    public string KioskKey { get; set; } = "";

    // The id of this computer in the tracker (see PC-01, PC-02... on the dashboard).
    // 0 = not chosen yet; the app shows a setup screen.
    public int ComputerId { get; set; }

    // false = a normal window (easy for testing). true = fullscreen, always on top, cannot be closed.
    public bool KioskMode { get; set; }

    // PIN staff type (Ctrl+Shift+Q) to exit or change computer. In kiosk mode an empty PIN disables the shortcut.
    public string StaffPin { get; set; } = "";

    // Log the user out after this many seconds with no keyboard or mouse use. 0 = never.
    public int IdleTimeoutSeconds { get; set; } = 1200;

    // Show a warning during the last N seconds before the automatic log out.
    public int IdleWarningSeconds { get; set; } = 60;

    // How often to re-check the tracker for changes made by staff.
    public int RefreshSeconds { get; set; } = 5;

    [JsonIgnore]
    public string DataDirectory { get; set; } = DefaultDataDirectory();

    public static string DefaultDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ComputerTracker");

    public static KioskSettings Load(string? baseDirectory = null, string? dataDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        dataDirectory ??= DefaultDataDirectory();

        var settings = new KioskSettings();
        var path = Path.Combine(baseDirectory, SettingsFileName);
        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(path), JsonOptions)
                           ?? new KioskSettings();
            }
            catch (JsonException)
            {
                // A typo in the file: fall back to the defaults instead of crashing.
            }
        }

        settings.DataDirectory = dataDirectory;

        // A computer id saved by the setup screen wins over the one in the settings file.
        // (0 is saved when staff choose "change computer", so setup shows again after a restart.)
        var idPath = Path.Combine(dataDirectory, ComputerIdFileName);
        if (File.Exists(idPath) && int.TryParse(File.ReadAllText(idPath).Trim(), out var savedId) && savedId >= 0)
        {
            settings.ComputerId = savedId;
        }

        return settings;
    }

    // Remembers the chosen computer. Throws if the folder cannot be written.
    public void SaveComputerId()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, ComputerIdFileName), ComputerId.ToString());
    }
}
