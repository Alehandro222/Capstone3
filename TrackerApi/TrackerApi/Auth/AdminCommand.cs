using TrackerApi.Services;

namespace TrackerApi.Auth;

// Creates an admin account from the command line, for the very first sign-in:
//   dotnet run -- create-admin maria "Maria Santos"
// It asks for the password without showing it. After that, admins create
// other accounts from the dashboard.
public static class AdminCommand
{
    public static async Task RunAsync(StaffService staff, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- create-admin <username> \"<full name>\"");
            return;
        }

        var password = ReadPassword("Password (at least 8 characters): ");
        if (ReadPassword("Type it again: ") != password)
        {
            Console.WriteLine("The passwords do not match. Nothing was created.");
            return;
        }

        try
        {
            var admin = await staff.CreateFirstAdminAsync(args[0], string.Join(' ', args[1..]), password);
            Console.WriteLine($"Admin account '{admin.Username}' created. Start the API normally and sign in.");
        }
        catch (TrackerException ex)
        {
            Console.WriteLine($"Not created: {ex.Message}");
        }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";

        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true); // not echoed to the screen
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
        Console.WriteLine();
        return password.ToString();
    }
}
