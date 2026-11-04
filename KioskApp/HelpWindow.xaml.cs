using System;
using System.Windows;

namespace KioskApp;

// A small dialog asking what the user needs help with.
// ShowDialog() returns true when they pressed "Send request".
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => HelpText.Focus();
    }

    public string HelpMessage => HelpText.Text.Trim();

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (HelpMessage.Length == 0)
        {
            HelpError.Text = "Please describe what you need help with.";
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
