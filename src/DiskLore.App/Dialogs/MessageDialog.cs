using Avalonia.Controls;
using Avalonia.Input;

namespace DiskLore.App.Dialogs;

/// <summary>A plain themed message with one OK button.</summary>
public class MessageDialog : Window
{
    public MessageDialog() : this("", "") { }

    public MessageDialog(string title, string message)
    {
        DialogUi.StyleWindow(this, 460, title);

        var ok = DialogUi.MakeButton("OK", primary: true);
        ok.Click += (_, _) => Close(true);

        var body = DialogUi.Body(
            DialogUi.Text(title, 18, bold: true),
            DialogUi.Text(message, 13),
            DialogUi.ButtonRow(ok));
        Content = body;

        KeyDown += (_, args) =>
        {
            if (args.Key is Key.Escape or Key.Enter) Close(true);
        };
        Opened += (_, _) => ok.Focus();
    }
}
