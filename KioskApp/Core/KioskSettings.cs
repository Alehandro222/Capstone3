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

    // Which PC in the tracker this is (PC-04 = 4). Set by pairing and saved in the
    // data folder (computer-id.txt), never in kiosk.settings.json. 0 = not paired yet.
    [JsonIgnore]
    public int ComputerId { get; set; }

    // false = a normal window (easy for testing). true = fullscreen, always on top, cannot be closed.
    public bool KioskMode { get; set; }

    // Staff PIN as typed by whoever set up the PC. Only read once: on start it is replaced by
    // StaffPinHash and removed from the file, so the PIN is never left in plain text.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StaffPin { get; set; }

    // The staff PIN (Ctrl+Shift+Q), as a salted hash (see PinHasher). Empty = no PIN:
    // the shortcut then works without one while testing, and is disabled in kiosk mode.
    public string StaffPinHash { get; set; } = "";

    // The visitor phone app's address, e.g. "http://192.168.1.10:5173/#/visit". When set, the
    // lock screen shows a QR code that opens it with this PC already chosen. Empty = no QR.
    public string VisitorUrl { get; set; } = "";

    // The session box warns during the last N minutes (and offers Extend when the tracker allows it).
    public int TimeWarningMinutes { get; set; } = 10;

    // Log the user out after this many seconds with no keyboard or mouse use. 0 = never.
    public int IdleTimeoutSeconds { get; set; } = 1200;

    // Show a warning during the last N seconds before the automatic log out.
    public int IdleWarningSeconds { get; set; } = 60;

    // How often to re-check the tracker for changes made by staff.
    public int RefreshSeconds { get; set; } = 5;

    [JsonIgnore]
    public string DataDirectory { get; set; } = DefaultDataDirectory();

    // Where these settings were read from (and are saved to when the PIN changes).
    [JsonIgnore]
    public string SettingsPath { get; set; } = "";

    // Set when the plain-text PIN could not be removed from the file (shown to staff).
    [JsonIgnore]
    public string? SaveWarning { get; set; }

    [JsonIgnore]
    public bool HasStaffPin => !string.IsNullOrEmpty(StaffPinHash);

    // The address the lock screen's QR code opens: the phone app with this PC chosen.
    public string? VisitorUrlFor(int computerId)
    {
        var url = VisitorUrl?.Trim();
        if (string.IsNullOrEmpty(url) || computerId <= 0) return null;
        return url + (url.Contains('?') ? "&" : "?") + "pc=" + computerId;
    }

    // Sets a new staff PIN (stored only as a hash) and saves the settings file.
    public void SetStaffPin(string pin)
    {
        StaffPinHash = PinHasher.Hash(pin);
        StaffPin = null;
        Save();
    }

    // Writes the settings back to kiosk.settings.json. Throws if the file cannot be written.
    public void Save()
    {
        if (string.IsNullOrEmpty(SettingsPath)) throw new InvalidOperationException("No settings file to save to.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
    }

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
        settings.SettingsPath = path;

        // A PIN typed into the file in plain text: keep only its hash, and take it out of the file.
        if (!string.IsNullOrEmpty(settings.StaffPin))
        {
            if (settings.StaffPin.Length < PinHasher.MinLength)
                settings.SaveWarning = $"The staff PIN is shorter than {PinHasher.MinLength} characters. Please change it.";
            settings.StaffPinHash = PinHasher.Hash(settings.StaffPin);
            settings.StaffPin = null;
            try
            {
                settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cannot write next to the .exe: the hash is used for this run; the file keeps the
                // plain PIN until the app runs once with write access (README: Deploying).
                settings.SaveWarning = "The staff PIN is still in plain text in kiosk.settings.json: " + ex.Message
                                       + (settings.SaveWarning is null ? "" : " " + settings.SaveWarning);
            }
        }

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
