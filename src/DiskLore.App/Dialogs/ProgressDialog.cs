using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiskLore.App.Dialogs;

/// <summary>Modal "please wait" box with an indeterminate bar. It ignores user close requests; call <see cref="Finish"/>.</summary>
public class ProgressDialog : Window
{
    private bool _finished;

    public ProgressDialog() : this("Working...") { }

    public ProgressDialog(string text)
    {
        DialogUi.StyleWindow(this, 340, text);
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.BorderOnly;

        var bar = new ProgressBar { IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        bar.Themed(ProgressBar.ForegroundProperty, "App.Accent");
        bar.Themed(ProgressBar.BackgroundProperty, "App.BarTrack");

        var body = DialogUi.Body(DialogUi.Text(text, 14, bold: true), bar);
        var frame = new Border { BorderThickness = new Thickness(1), Child = body };
        frame.Themed(Border.BorderBrushProperty, "App.Border");
        Content = frame;

        Closing += (_, args) =>
        {
            if (!_finished) args.Cancel = true;
        };
    }

    public void Finish()
    {
        _finished = true;
        Close();
    }
}
