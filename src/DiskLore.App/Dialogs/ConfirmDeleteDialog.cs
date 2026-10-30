using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;

namespace DiskLore.App.Dialogs;

/// <summary>Confirmation before a recycle or a (non-critical) permanent delete. Result: true = go ahead.</summary>
public class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog() : this(null, null, false, "Recycle Bin") { }

    public ConfirmDeleteDialog(ScanNode? node, Explanation? e, bool permanent, string trashName)
    {
        DialogUi.StyleWindow(this, 480, permanent ? "Delete permanently" : "Move to " + trashName);
        if (node == null || e == null) return;

        var body = DialogUi.Body();

        body.Children.Add(DialogUi.Text(permanent ? "Delete permanently?" : $"Move to {trashName}?", 18, bold: true));

        var info = new StackPanel { Spacing = 4 };
        info.Children.Add(DialogUi.Text(node.Name, 14, bold: true));
        info.Children.Add(DialogUi.Text(node.FullPath, 12, color: "App.TextMuted"));
        info.Children.Add(DialogUi.Text(SizeFormat.Format(node.Size), 12, color: "App.TextMuted"));

        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        badgeRow.Children.Add(DialogUi.Pill(e.Badge));
        var summary = DialogUi.Text(e.Summary, 12, wrap: false);
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        badgeRow.Children.Add(summary);
        info.Children.Add(badgeRow);
        body.Children.Add(DialogUi.Card(info));

        if (e.Badge is Badge.Critical or Badge.Shrink)
            body.Children.Add(DialogUi.Text("If deleted: " + e.IfDeleted, 12, color: "Badge.Critical"));

        if (permanent)
            body.Children.Add(DialogUi.Text("This cannot be undone.", 13, bold: true, color: "Badge.Critical"));
        else
            body.Children.Add(DialogUi.Text($"You can restore it from the {trashName} until it is emptied.", 12, color: "App.TextMuted"));

        var cancel = DialogUi.MakeButton("Cancel");
        var ok = permanent
            ? DialogUi.MakeButton("Delete permanently", accentKey: "Badge.Critical")
            : DialogUi.MakeButton("Move to " + trashName, primary: true);
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => Close(true);
        body.Children.Add(DialogUi.ButtonRow(cancel, ok));

        Content = body;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close(false);
        };
        Opened += (_, _) => cancel.Focus();
    }
}
