using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DiskLore.Core.Explanations;

namespace DiskLore.App.Dialogs;

// Small builders shared by the delete dialogs. Colors always come from the theme brushes.
internal static class DialogUi
{
    public static T Themed<T>(this T control, AvaloniaProperty property, string resourceKey) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(resourceKey));
        return control;
    }

    public static void StyleWindow(Window w, double width, string title)
    {
        w.Title = title;
        w.Width = width;
        w.SizeToContent = SizeToContent.Height;
        w.CanResize = false;
        w.ShowInTaskbar = false;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.Themed(Window.BackgroundProperty, "App.Background");
        w.Themed(Window.ForegroundProperty, "App.Text");
    }

    public static TextBlock Text(string text, double size = 13, bool bold = false, string color = "App.Text", bool wrap = true)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        t.Themed(TextBlock.ForegroundProperty, color);
        return t;
    }

    public static Border Pill(Badge badge)
    {
        var text = new TextBlock { Text = badge.ToString().ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold };
        text.Themed(TextBlock.ForegroundProperty, "App.Background");
        var pill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2),
            Child = text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pill.Themed(Border.BackgroundProperty, "Badge." + badge);
        return pill;
    }

    public static Button MakeButton(string label, bool primary = false, string? accentKey = null)
    {
        var b = new Button
        {
            Content = label,
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(14, 6),
        };
        if (accentKey != null)
        {
            b.Themed(Button.BackgroundProperty, accentKey);
            b.Themed(Button.ForegroundProperty, "App.Background");
        }
        else if (primary)
        {
            b.Themed(Button.BackgroundProperty, "App.Accent");
            b.Themed(Button.ForegroundProperty, "App.Background");
        }
        else
        {
            b.Themed(Button.BackgroundProperty, "App.SurfaceAlt");
            b.Themed(Button.ForegroundProperty, "App.Text");
        }
        return b;
    }

    public static StackPanel ButtonRow(params Control[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    public static Border Card(Control child, string background = "App.Surface")
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            Child = child,
        };
        card.Themed(Border.BackgroundProperty, background);
        card.Themed(Border.BorderBrushProperty, "App.Border");
        return card;
    }

    public static StackPanel Body(params Control[] children)
    {
        var p = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }
}
