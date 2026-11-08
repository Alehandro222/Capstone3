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
    private const string KioskKeyFileName = "kiosk-key.txt";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ApiUrl { get; set; } = "http://localhost:5080/";

    // This PC's secret key, received when staff paired it with a pairing code.
    // Saved in the data folder (kiosk-key.txt), never in kiosk.settings.json.
    [JsonIgnore]
    public string KioskKey { get; set; } = "";

    [JsonIgnore]
    public bool IsPaired => ComputerId > 0 && !string.IsNullOrEmpty(KioskKey);

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

        var keyPath = Path.Combine(dataDirectory, KioskKeyFileName);
        if (File.Exists(keyPath)) settings.KioskKey = File.ReadAllText(keyPath).Trim();

        return settings;
    }

    // Remembers which PC this is and its key. Throws if the folder cannot be written.
    public void SavePairing(int computerId, string kioskKey)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, KioskKeyFileName), kioskKey);
        File.WriteAllText(Path.Combine(DataDirectory, ComputerIdFileName), computerId.ToString());
        ComputerId = computerId;
        KioskKey = kioskKey;
    }

    // Forgets the pairing, so the setup screen shows again (also after a restart).
    public void ClearPairing()
    {
        ComputerId = 0;
        KioskKey = "";
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, ComputerIdFileName), "0");
        var keyPath = Path.Combine(DataDirectory, KioskKeyFileName);
        if (File.Exists(keyPath)) File.Delete(keyPath);
    }
}
