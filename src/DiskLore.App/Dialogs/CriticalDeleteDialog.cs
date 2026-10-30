using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.Dialogs;

/// <summary>
/// Red confirmation for a permanent delete of a Critical or Shrink item. The user has to type the exact
/// item name (case-sensitive) before the button turns on. Result: true = delete.
/// </summary>
public class CriticalDeleteDialog : Window
{
    public CriticalDeleteDialog() : this(null, null) { }

    public CriticalDeleteDialog(ScanNode? node, Explanation? e)
    {
        DialogUi.StyleWindow(this, 520, "Delete permanently");
        if (node == null || e == null) return;

        var headerText = DialogUi.Text(
            e.Badge == Badge.Critical ? "This item is critical" : "This item should be shrunk, not deleted",
            18, bold: true, color: "App.Background");
        var header = new Border { Padding = new Thickness(20, 14), Child = headerText };
        header.Themed(Border.BackgroundProperty, "Badge.Critical");

        var body = DialogUi.Body();

        var info = new StackPanel { Spacing = 4 };
        info.Children.Add(DialogUi.Text(node.Name, 14, bold: true));
        info.Children.Add(DialogUi.Text(node.FullPath, 12, color: "App.TextMuted"));
        info.Children.Add(DialogUi.Text(SizeFormat.Format(node.Size), 12, color: "App.TextMuted"));
        body.Children.Add(DialogUi.Card(info));

        body.Children.Add(DialogUi.Text("If deleted", 12, bold: true, color: "Badge.Critical"));
        body.Children.Add(DialogUi.Text(e.IfDeleted, 13));

        if (!string.IsNullOrWhiteSpace(e.SaferAlternative))
        {
            body.Children.Add(DialogUi.Text("Safer alternative", 12, bold: true, color: "Badge.Safe"));
            body.Children.Add(DialogUi.Text(e.SaferAlternative!, 13));
        }

        body.Children.Add(DialogUi.Text("This cannot be undone.", 13, bold: true, color: "Badge.Critical"));
        body.Children.Add(DialogUi.Text($"Type \"{node.Name}\" to confirm:", 12, color: "App.TextMuted"));

        var input = new TextBox { PlaceholderText = node.Name };
        body.Children.Add(input);

        var cancel = DialogUi.MakeButton("Cancel");
        var ok = DialogUi.MakeButton("Delete permanently", accentKey: "Badge.Critical");
        ok.IsEnabled = false;
        input.TextChanged += (_, _) => ok.IsEnabled = string.Equals(input.Text, node.Name, StringComparison.Ordinal);
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) =>
        {
            if (ok.IsEnabled) Close(true);
        };
        body.Children.Add(DialogUi.ButtonRow(cancel, ok));

        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(body);
        Content = root;

        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close(false);
        };
        Opened += (_, _) => input.Focus();
    }
}
