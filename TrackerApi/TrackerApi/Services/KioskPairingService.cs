using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Pairing connects the kiosk app on one PC to that PC's record in the tracker:
//   1. An admin creates a pairing code for PC-04 (valid 15 minutes, works once).
//   2. Staff type it into the setup screen of the kiosk app on that PC.
//   3. The app trades it for a long secret key that only works as PC-04.
// Creating a new code, or pairing again, replaces the old key, so a copied key
// can be cancelled at any time.
public class KioskPairingService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    // No 0/O or 1/I/L, so a code read off a screen is typed correctly.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;

    public KioskPairingService(TrackerDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PairingCodeDto> CreateCodeAsync(int computerId)
    {
        var computer = await _db.Computers.FindAsync(computerId)
                       ?? throw TrackerException.NotFound("Computer not found.");

        var code = NewCode();
        computer.PairingCodeHash = Secrets.Hash(Normalize(code));
        computer.PairingCodeExpiresAt = _clock.UtcNow() + CodeLifetime;
        await _db.SaveChangesAsync();

        return new PairingCodeDto(computer.Id, computer.Name, code, computer.PairingCodeExpiresAt.Value);
    }

    public async Task<KioskPairedDto> PairAsync(string? code)
    {
        const string wrong = "That pairing code is wrong or has expired. Ask an admin for a new one.";
        if (string.IsNullOrWhiteSpace(code)) throw TrackerException.BadRequest(wrong);

        var hash = Secrets.Hash(Normalize(code));
        var now = _clock.UtcNow();
        var computer = await _db.Computers.FirstOrDefaultAsync(c =>
            c.PairingCodeHash == hash && c.PairingCodeExpiresAt > now);
        if (computer is null) throw TrackerException.BadRequest(wrong);

        var key = Secrets.NewToken();
        computer.KioskKeyHash = Secrets.Hash(key);
        computer.PairedAt = now;
        computer.PairingCodeHash = null; // the code works once
        computer.PairingCodeExpiresAt = null;
        await _db.SaveChangesAsync();

        return new KioskPairedDto(computer.Id, computer.Name, key);
    }

    // Cancels the PC's key (and any unused code): its kiosk app must be paired again.
    public async Task UnpairAsync(int computerId)
    {
        var computer = await _db.Computers.FindAsync(computerId)
                       ?? throw TrackerException.NotFound("Computer not found.");
        computer.KioskKeyHash = null;
        computer.PairedAt = null;
        computer.PairingCodeHash = null;
        computer.PairingCodeExpiresAt = null;
        await _db.SaveChangesAsync();
    }

    // "ABCD-2345"
    private static string NewCode()
    {
        var chars = Enumerable.Range(0, 8)
            .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
            .ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    // "abcd 2345", "ABCD-2345" and "abcd2345" are the same code.
    private static string Normalize(string code) =>
        new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
}
