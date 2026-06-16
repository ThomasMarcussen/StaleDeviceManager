using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace StaleDeviceManager;

/// <summary>
/// Standard Circle of Bytes "About" box. Built in code to avoid an extra XAML file.
/// </summary>
public class AboutWindow : Window
{
    public AboutWindow()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        Title = "About";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(0) };

        // Header band
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x3D, 0x5C)),
            Padding = new Thickness(18, 16, 18, 16)
        };
        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = "Stale Device Manager",
            Foreground = Brushes.White,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = $"Version {version}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0xD8, 0xE8)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        header.Child = headerStack;
        root.Children.Add(header);

        // Body
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

        body.Children.Add(new TextBlock
        {
            Text = "Find, disable, and delete stale device records across on-premises "
                 + "Active Directory, Entra ID, and Intune.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        body.Children.Add(MakeRow("Author:", "Thomas Marcussen"));
        body.Children.Add(MakeRow("Company:", "ThomasMarcussen.com"));
        body.Children.Add(MakeLinkRow("Email:", "Thomas@ThomasMarcussen.com", "mailto:Thomas@ThomasMarcussen.com"));
        body.Children.Add(MakeLinkRow("Web:", "ThomasMarcussen.com", "https://thomasmarcussen.com"));

        body.Children.Add(new TextBlock
        {
            Text = $"© {2026} ThomasMarcussen.com. All rights reserved.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            FontSize = 11,
            Margin = new Thickness(0, 16, 0, 0)
        });

        var ok = new Button
        {
            Content = "Close",
            Width = 90,
            Height = 30,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        ok.Click += (_, _) => Close();
        body.Children.Add(ok);

        root.Children.Add(body);
        Content = root;
    }

    private static UIElement MakeRow(string label, string value)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sp.Children.Add(new TextBlock { Text = label, Width = 70, FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = value });
        return sp;
    }

    private static UIElement MakeLinkRow(string label, string text, string uri)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sp.Children.Add(new TextBlock { Text = label, Width = 70, FontWeight = FontWeights.SemiBold });

        var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(uri) };
        link.RequestNavigate += OnNavigate;
        sp.Children.Add(new TextBlock(link));
        return sp;
    }

    private static void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
