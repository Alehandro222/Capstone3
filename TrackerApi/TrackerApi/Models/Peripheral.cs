namespace TrackerApi.Models;

public enum PeripheralType
{
    Mouse,
    Keyboard,
    Headset,
    Microphone,
    DrawingTablet,
    Webcam,
    Other
}

public class Peripheral
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PeripheralType Type { get; set; } = PeripheralType.Other;

    // "int?" is a nullable int. null = not plugged into any computer.
    public int? ComputerId { get; set; }
    public Computer? Computer { get; set; }

    // True while someone is actively using it (headset, tablet, mic...).
    public bool IsInUse { get; set; }
    public DateTime? InUseSince { get; set; }
}
