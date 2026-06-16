using System.Windows;
using System.Windows.Controls;

namespace StaleDeviceManager;

/// <summary>
/// Modal dialog that requires the operator to type an exact phrase (e.g. DELETE)
/// before the action is allowed. Built in code to avoid an extra XAML file.
/// </summary>
public static class ConfirmDialog
{
    public static bool Prompt(Window owner, string title, string message, string requiredWord)
    {
        var win = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var input = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var ok = new Button
        {
            Content = "Confirm",
            Width = 100,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
            Background = System.Windows.Media.Brushes.Firebrick,
            Foreground = System.Windows.Media.Brushes.White
        };
        var cancel = new Button { Content = "Cancel", Width = 100, Height = 30, IsCancel = true };

        // Only enable Confirm when the exact (case-sensitive) word is typed.
        input.TextChanged += (_, _) => ok.IsEnabled = input.Text == requiredWord;

        var result = false;
        ok.Click += (_, _) => { result = true; win.Close(); };
        cancel.Click += (_, _) => { result = false; win.Close(); };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        win.Content = root;
        input.Focus();
        win.ShowDialog();
        return result;
    }
}
