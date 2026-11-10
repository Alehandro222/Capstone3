using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KioskApp.Core;

namespace KioskApp;

// The small always-on-top box in the corner while someone is logged in.
// It shows who is logged in, a timer, the idle warning, and the Help and Log out buttons.
public partial class SessionBar : Window
{
    private readonly KioskController _controller;
    private KioskState _state;
    private bool _busy;
    private int _noticeId;

    public SessionBar(KioskController controller)
    {
        InitializeComponent();

        _controller = controller;
        _state = controller.State;

        _controller.StateChanged += Render;
        _controller.Ticked += UpdateTimer;
    }

    // Puts the bar in the top-right corner of the screen (the user can drag it elsewhere).
    public void PositionTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Top + 16;
    }

    private void Render(KioskState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => Render(state));
            return;
        }

        _state = state;
        if (state.Session is null) return;

        UserText.Text = state.Session.UserName;
        PurposeText.Text = state.Session.Purpose;

        if (state.IdleSecondsLeft is int seconds)
        {
            WarningText.Text = $"Are you still there? You will be logged out in {seconds} seconds. Move the mouse or press a key to stay.";
            WarningBanner.Visibility = Visibility.Visible;
        }
        else
        {
            WarningBanner.Visibility = Visibility.Collapsed;
        }

        UpdateTimer();
    }

    // Every second: the time left, and in the last minutes a warning (and Extend, when allowed).
    private void UpdateTimer()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => UpdateTimer());
            return;
        }

        var session = _state.Session;
        if (session is null) return;

        if (session.ScheduledEndAt == default)
        {
            // An older tracker without end times: show how long they have been on instead.
            var elapsed = _controller.NowUtc - session.StartedAtUtc;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            TimerText.Text = SessionClock.Format(elapsed);
            TimerLabel.Text = "logged in";
            TimeBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var left = SessionClock.Remaining(session, _controller.NowUtc);
        var phase = SessionClock.Phase(left, _controller.Settings.TimeWarningMinutes);
        TimerText.Text = SessionClock.Format(left);
        TimerLabel.Text = session.ExtensionCount > 0 ? $"time left (extended {session.ExtensionCount}×)" : "time left";

        var warn = phase != TimePhase.Normal;
        var brush = (Brush)FindResource(phase == TimePhase.Final ? "DangerBrush" : warn ? "WarnBrush" : "PrimaryBrush");
        Frame.BorderBrush = brush;
        Frame.BorderThickness = new Thickness(warn ? 4 : 2);
        TimerText.Foreground = warn ? brush : (Brush)FindResource("TextBrush");

        if (!warn)
        {
            TimeBanner.Visibility = Visibility.Collapsed;
            return;
        }

        TimeText.Text = left <= TimeSpan.Zero
            ? "Your time is up. This computer will lock in a moment."
            : $"Your session ends in {SessionClock.Words(left)}. Please save your work and sign out of your accounts.";
        ExtendButton.Visibility = session.CanExtend ? Visibility.Visible : Visibility.Collapsed;
        ExtendNote.Text = session.CanExtend ? "" : session.CannotExtendReason ?? "";
        ExtendNote.Visibility = string.IsNullOrEmpty(ExtendNote.Text) ? Visibility.Collapsed : Visibility.Visible;
        TimeBanner.Visibility = Visibility.Visible;
    }

    private async void ExtendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        var error = await _controller.ExtendAsync();
        SetBusy(false);

        if (error is null) ShowNotice("Extended by 1 hour.", isError: false);
        else ShowNotice(error, isError: true);
    }

    private async void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new HelpWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;

        SetBusy(true);
        var error = await _controller.RequestHelpAsync(dialog.HelpMessage);
        SetBusy(false);

        if (error is null) ShowNotice("Help is on the way. A staff member has been notified.", isError: false);
        else ShowNotice(error, isError: true);
    }

    private async void LogOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var answer = MessageBox.Show(
            this,
            "Log out now?\n\nPlease save your work and sign out of your accounts (email, social media, etc.) first.",
            "Log out",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true);
        var error = await _controller.LogOutAsync();
        SetBusy(false);

        if (error is not null) ShowNotice(error, isError: true);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        HelpButton.IsEnabled = !busy;
        LogOutButton.IsEnabled = !busy;
        ExtendButton.IsEnabled = !busy;
    }

    // Shows a message under the timer and hides it again after 8 seconds.
    private async void ShowNotice(string text, bool isError)
    {
        var id = ++_noticeId;
        NoticeText.Text = text;
        NoticeText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "OkBrush");
        NoticeText.Visibility = Visibility.Visible;

        await Task.Delay(TimeSpan.FromSeconds(8));

        if (id == _noticeId) NoticeText.Visibility = Visibility.Collapsed;
    }

    // Lets the user drag the bar by clicking anywhere on it that is not a button.
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Alt+F4 must not remove the bar (and with it the Log out button).
        if (!App.IsExiting) e.Cancel = true;
    }
}
