using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using KioskApp.Core;

namespace KioskApp;

// The fullscreen window people see before they log in.
// It only draws what KioskController.State says and reports button clicks back to the controller.
public partial class LockWindow : Window
{
    private readonly KioskController _controller;
    private KioskView? _lastView;
    private bool _busy;
    private int _noticeId;
    private bool _staffMode;   // true while the staff PIN / menu panels are open

    public LockWindow(KioskController controller)
    {
        InitializeComponent();

        _controller = controller;

        if (_controller.Settings.KioskMode)
        {
            // Fullscreen, always on top, no title bar, no taskbar button.
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;
            ShowInTaskbar = false;
        }

        _controller.StateChanged += Render;
        Render(_controller.State);
    }

    // ---- Drawing -----------------------------------------------------------

    private void Render(KioskState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => Render(state));
            return;
        }

        ComputerNameText.Text = state.Computer?.Name ?? (state.View == KioskView.NeedsSetup ? "Setup" : "Computer");
        OfflineBanner.Visibility = state.Offline ? Visibility.Visible : Visibility.Collapsed;

        // While staff are typing a PIN, do not switch panels underneath them.
        if (_staffMode) return;

        var enteringLogin = state.View == KioskView.Locked && _lastView != KioskView.Locked;
        _lastView = state.View;

        switch (state.View)
        {
            case KioskView.Locked:
                LoginIntro.Text = $"{state.Computer?.Name} is free.";
                ShowPanel(LoginPanel);
                if (enteringLogin) ResetLoginForm();
                break;

            case KioskView.NeedsSetup:
                if (SetupPanel.Visibility != Visibility.Visible) PairingCodeBox.Clear();
                ShowPanel(SetupPanel);
                SetupError.Text = state.KeyRejected
                    ? "The tracker no longer accepts this PC (an admin unpaired it, or it was paired again). Type a new pairing code."
                    : "";
                PairingCodeBox.Focus();
                break;

            case KioskView.Farewell:
                ShowMessage($"Thank you, {state.FarewellName}!", state.FarewellText ?? "");
                break;

            case KioskView.OutOfService:
                ShowMessage("Out of service", "This computer isn't working. Please use another one or ask a staff member.");
                break;

            case KioskView.Reserved:
                ShowMessage("Reserved", "This computer is reserved for a visitor. Please check in at the help desk.");
                break;

            case KioskView.InSession:
                ShowMessage("Logged in", ""); // this window is hidden while a session runs
                break;

            default: // Loading
                ShowMessage("Loading…", state.Offline ? "Cannot reach the tracker. Trying again…" : "");
                break;
        }
    }

    private void ShowPanel(UIElement panel)
    {
        foreach (var p in new UIElement[] { MessagePanel, LoginPanel, RegisterPanel, SetupPanel, StaffPinPanel, StaffMenuPanel })
        {
            p.Visibility = ReferenceEquals(p, panel) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShowMessage(string title, string text)
    {
        MessageTitle.Text = title;
        MessageText.Text = text;
        MessageText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        ShowPanel(MessagePanel);
    }

    private void ResetLoginForm()
    {
        LoginError.Text = "";
        LoginNotice.Visibility = Visibility.Collapsed;
    }

    // ---- Staff: register a visitor on this PC ---------------------------------

    private void RegisterVisitor_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.State.View != KioskView.Locked)
        {
            StaffMenuError.Text = "This computer is not free right now.";
            return;
        }

        FirstNameBox.Clear();
        LastNameBox.Clear();
        foreach (var panel in new[] { AgePanel, SexPanel, SectorPanel, ServicePanel })
            foreach (var chip in panel.Children.OfType<ToggleButton>())
                chip.IsChecked = false;
        RegisterError.Text = "";
        ShowPanel(RegisterPanel);
        FirstNameBox.Focus();
    }

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var registration = new KioskRegistration(
            FirstNameBox.Text,
            LastNameBox.Text,
            Selected(AgePanel).FirstOrDefault() ?? "",
            Selected(SexPanel).FirstOrDefault() ?? "",
            Selected(SectorPanel),
            Selected(ServicePanel));

        _busy = true;
        RegisterButton.IsEnabled = false;
        RegisterError.Text = "";

        var error = await _controller.RegisterAndStartAsync(registration);

        _busy = false;
        RegisterButton.IsEnabled = true;
        if (error is null) LeaveStaffMode(); // the session view takes over
        else RegisterError.Text = error;
    }

    // The API values (Tag) of the chips that are ticked.
    private static List<string> Selected(Panel panel) =>
        panel.Children.OfType<ToggleButton>()
            .Where(chip => chip.IsChecked == true)
            .Select(chip => chip.Tag?.ToString() ?? "")
            .ToList();

    // ---- Help (before logging in) -------------------------------------------

    private async void LoginHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new HelpWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _busy = true;
        LoginHelpButton.IsEnabled = false;
        LoginError.Text = "";

        var error = await _controller.RequestHelpAsync(dialog.HelpMessage);

        _busy = false;
        LoginHelpButton.IsEnabled = true;

        if (error is null) ShowLoginNotice("Help is on the way. A staff member has been notified.");
        else LoginError.Text = error;
    }

    // Shows a green message on the login screen and hides it again after 8 seconds.
    private async void ShowLoginNotice(string text)
    {
        var id = ++_noticeId;
        LoginNotice.Text = text;
        LoginNotice.Visibility = Visibility.Visible;

        await Task.Delay(TimeSpan.FromSeconds(8));

        if (id == _noticeId) LoginNotice.Visibility = Visibility.Collapsed;
    }

    // ---- Setup (first run) -------------------------------------------------

    private async void SetupButton_Click(object sender, RoutedEventArgs e) => await PairAsync();

    private async void PairingCodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await PairAsync();
    }

    private async Task PairAsync()
    {
        if (_busy) return;
        _busy = true;
        SetupError.Text = "Pairing…";
        var error = await _controller.PairAsync(PairingCodeBox.Text);
        SetupError.Text = error ?? "";
        _busy = false;
    }

    // ---- Staff exit (Ctrl+Shift+Q) -----------------------------------------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Q && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            OpenStaffPin();
        }
    }

    private void OpenStaffPin()
    {
        _staffMode = true;
        StaffMenuError.Text = "";
        PinBox.Clear();
        PinError.Text = "";
        ShowPanel(StaffPinPanel);
        PinBox.Focus();
    }

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CheckPin();
    }

    private void PinOk_Click(object sender, RoutedEventArgs e) => CheckPin();

    private void CheckPin()
    {
        if (_controller.VerifyStaffPin(PinBox.Password))
        {
            ShowPanel(StaffMenuPanel);
        }
        else
        {
            PinError.Text = "Incorrect PIN.";
            PinBox.Clear();
        }
    }

    private void StaffCancel_Click(object sender, RoutedEventArgs e) => LeaveStaffMode();

    private void ChangeComputer_Click(object sender, RoutedEventArgs e)
    {
        _controller.Unpair();
        LeaveStaffMode();
    }

    private void ExitKiosk_Click(object sender, RoutedEventArgs e) => App.Quit();

    private void LeaveStaffMode()
    {
        _staffMode = false;
        _lastView = null; // force the screen to redraw from the current state
        Render(_controller.State);
    }

    // ---- Closing -----------------------------------------------------------

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // In kiosk mode, Alt+F4 and similar must not close the login screen.
        if (_controller.Settings.KioskMode && !App.IsExiting)
        {
            e.Cancel = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e) => App.Quit();
}
