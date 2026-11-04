using System;
using System.IO;
using System.Threading;
using System.Windows;
using KioskApp.Core;

namespace KioskApp;

// The application: creates the controller and the two windows, and decides which one is visible.
//   - Nobody logged in -> the fullscreen LockWindow (login screen)
//   - Someone logged in -> the LockWindow hides and a small SessionBar floats in the corner,
//     so the user can use the computer normally.
public partial class App : Application
{
    private readonly CancellationTokenSource _cts = new();
    private ApiClient? _api;
    private KioskController? _controller;
    private LockWindow? _lockWindow;
    private SessionBar? _sessionBar;

    // True once the app is really shutting down (otherwise kiosk windows refuse to close).
    public static bool IsExiting { get; private set; }

    public static void Quit()
    {
        if (IsExiting) return;
        IsExiting = true;
        Current.Shutdown();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Safety net: an unexpected error is logged instead of crashing a public computer.
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        var settings = KioskSettings.Load();
        _api = new ApiClient(settings.ApiUrl);
        _controller = new KioskController(settings, _api, new WindowsIdleSource());

        _lockWindow = new LockWindow(_controller);
        _sessionBar = new SessionBar(_controller);
        MainWindow = _lockWindow;

        _controller.StateChanged += OnStateChanged;
        _controller.ErrorOccurred += LogError;
        OnStateChanged(_controller.State);

        // Start the loop inside the dispatcher so every await resumes on the UI thread.
        _ = Dispatcher.InvokeAsync(() => _controller.RunAsync(_cts.Token));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _api?.Dispose();
        base.OnExit(e);
    }

    private void OnStateChanged(KioskState state)
    {
        if (_lockWindow is null || _sessionBar is null) return;

        if (state.View == KioskView.InSession)
        {
            if (!_sessionBar.IsVisible)
            {
                _sessionBar.PositionTopRight();
                _sessionBar.Show();
            }
            if (_lockWindow.IsVisible) _lockWindow.Hide();
        }
        else
        {
            if (_sessionBar.IsVisible) _sessionBar.Hide();
            if (!_lockWindow.IsVisible)
            {
                _lockWindow.Show();
                _lockWindow.Activate();
            }
        }
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var folder = KioskSettings.DefaultDataDirectory();
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "kiosk-errors.log"),
                $"{DateTime.Now:u} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing more we can do if even the log cannot be written.
        }
    }
}
