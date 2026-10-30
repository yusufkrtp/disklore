using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DiskLore.Core.Explanations;

namespace DiskLore.App.Controls;

// Small colored label for a Badge. Colors follow the theme brushes Badge.* and update when the theme changes.
public class BadgePill : Border
{
    public static readonly StyledProperty<Badge> BadgeProperty =
        AvaloniaProperty.Register<BadgePill, Badge>(nameof(Badge), Badge.Unknown);

    private readonly TextBlock _text = new()
    {
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    private IDisposable? _subscription;

    public BadgePill()
    {
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(7, 1);
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        BorderThickness = new Thickness(1);
        Child = _text;
        Apply();
    }

    public Badge Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public static string LabelFor(Badge badge) => badge switch
    {
        Badge.Critical => "CRITICAL",
        Badge.Shrink => "SHRINK",
        Badge.App => "APP",
        Badge.Check => "CHECK",
        Badge.Safe => "SAFE",
        Badge.Leftover => "LEFTOVER",
        Badge.User => "USER",
        _ => "?",
    };

    public static string BrushKeyFor(Badge badge) => "Badge." + badge;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BadgeProperty) Apply();
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Apply();
    }

    private void Apply()
    {
        _text.Text = LabelFor(Badge);
        _subscription?.Dispose();
        var key = BrushKeyFor(Badge);
        _subscription = this.GetResourceObservable(key).Subscribe(new BrushObserver(this));
    }

    private sealed class BrushObserver(BadgePill pill) : IObserver<object?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void OnNext(object? value)
        {
            if (value is ISolidColorBrush solid)
            {
                pill._text.Foreground = solid;
                pill.Background = new SolidColorBrush(solid.Color, 0.16);
                pill.BorderBrush = new SolidColorBrush(solid.Color, 0.45);
            }
            else if (value is IBrush brush)
            {
                pill._text.Foreground = brush;
                pill.Background = null;
                pill.BorderBrush = brush;
            }
        }
    }
}
