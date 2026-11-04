using System;
using System.Runtime.InteropServices;

namespace KioskApp.Core;

// "How long since the user last touched the keyboard or mouse?"
// An interface so the logic can be tested without a real keyboard.
public interface IIdleSource
{
    TimeSpan GetIdleTime();
}

// Asks Windows directly. Unlike a web page, this counts activity in ALL programs,
// so someone typing in Word or a browser is never seen as idle.
public sealed class WindowsIdleSource : IIdleSource
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime; // tick count (ms) at the last input
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    public TimeSpan GetIdleTime()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

            // Both numbers are 32-bit millisecond counters that wrap around, so subtract unchecked.
            var now = unchecked((uint)Environment.TickCount);
            var idleMs = unchecked(now - info.dwTime);
            return TimeSpan.FromMilliseconds(idleMs);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return TimeSpan.Zero; // cannot tell, so never log anyone out by mistake
        }
    }
}
