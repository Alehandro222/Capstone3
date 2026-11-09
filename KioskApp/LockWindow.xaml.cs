using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KioskApp.Core;
using QRCoder;

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
    private string? _qrUrl;    // the address the QR image currently shows

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
        _controller.Ticked += UpdateReservedCountdown;
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
                ShowQr(state.Computer);
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
                ShowPanel(ReservedPanel);
                UpdateReservedCountdown();
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
        foreach (var p in new UIElement[]
                 { MessagePanel, LoginPanel, ReservedPanel, RegisterPanel, SetupPanel, StaffPinPanel, StaffMenuPanel, ChangePinPanel })
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

    // The lock screen's QR code: the phone app with this PC already chosen.
    // Without visitorUrl in the settings, only the written steps show.
    private void ShowQr(ComputerDto? computer)
    {
        var name = computer?.Name ?? "this computer";
        var url = computer is null ? null : _controller.Settings.VisitorUrlFor(computer.Id);

        if (url is null)
        {
            QrBox.Visibility = Visibility.Collapsed;
            LoginSteps.Text =
                "1. Scan the QR code at the entrance and fill in the registration form.\n" +
                "2. Pick this computer on your phone.\n" +
                "3. Show your QR pass at the help desk. This computer unlocks when staff scan it.";
            return;
        }

        if (url != _qrUrl)
        {
            try
            {
                QrImage.Source = MakeQr(url);
                _qrUrl = url;
            }
            catch (Exception)
            {
                QrBox.Visibility = Visibility.Collapsed; // the written steps still work
                return;
            }
        }

        QrCaption.Text = $"Scan to use {name}";
        QrBox.Visibility = Visibility.Visible;
        LoginSteps.Text =
            "1. Scan this QR code with your phone camera and fill in the registration form.\n" +
            $"2. {name} is already chosen: tap Choose {name}.\n" +
            "3. Show your QR pass at the help desk. This computer unlocks when staff scan it.";
    }

    private static BitmapImage MakeQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);

        var image = new BitmapImage();
        using var stream = new System.IO.MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // Reserved screen: time left for the visitor to get their pass scanned (updated every second).
    private void UpdateReservedCountdown()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(UpdateReservedCountdown);
            return;
        }

        var until = _controller.State.ReservedUntil;
        if (_controller.State.View != KioskView.Reserved || until is null)
        {
            ReservedCountdown.Visibility = Visibility.Collapsed;
            ReservedCountdownLabel.Visibility = Visibility.Collapsed;
            return;
        }

        var left = until.Value - DateTime.UtcNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        ReservedCountdown.Text = SessionClock.Format(left);
        ReservedCountdown.Visibility = Visibility.Visible;
        ReservedCountdownLabel.Visibility = Visibility.Visible;
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
        var error = _controller.CheckStaffPin(PinBox.Password);
        PinBox.Clear();
        if (error is null) OpenStaffMenu();
        else PinError.Text = error;
    }

    private void OpenStaffMenu(string? notice = null)
    {
        var warning = _controller.Settings.SaveWarning;
        StaffMenuError.Foreground = (System.Windows.Media.Brush)FindResource(notice is null ? "DangerBrush" : "OkBrush");
        StaffMenuError.Text = notice ?? warning ?? (_controller.Settings.HasStaffPin ? "" : "No staff PIN is set yet. Set one with Change the staff PIN.");
        ShowPanel(StaffMenuPanel);
    }

    // ---- Staff: change the PIN ---------------------------------------------

    private void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        NewPinBox.Clear();
        NewPinAgainBox.Clear();
        ChangePinError.Text = "";
        ShowPanel(ChangePinPanel);
        NewPinBox.Focus();
    }

    private void NewPinAgainBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SavePin();
    }

    private void SavePin_Click(object sender, RoutedEventArgs e) => SavePin();

    private void SavePin()
    {
        var error = _controller.ChangeStaffPin(NewPinBox.Password, NewPinAgainBox.Password);
        NewPinBox.Clear();
        NewPinAgainBox.Clear();
        if (error is null)
        {
            _controller.Settings.SaveWarning = null; // the file no longer holds a plain PIN
            OpenStaffMenu("Staff PIN changed.");
        }
        else
        {
            ChangePinError.Text = error;
            NewPinBox.Focus();
        }
    }

    private void BackToStaffMenu_Click(object sender, RoutedEventArgs e) => OpenStaffMenu();

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
