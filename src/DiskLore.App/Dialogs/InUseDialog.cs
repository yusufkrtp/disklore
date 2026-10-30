using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DiskLore.Core.Platform;

namespace DiskLore.App.Dialogs;

/// <summary>Lists the processes that hold files open. Result: true = "Delete anyway".</summary>
public class InUseDialog : Window
{
    public InUseDialog() : this(Array.Empty<LockingProcess>()) { }

    public InUseDialog(IReadOnlyList<LockingProcess> processes)
    {
        DialogUi.StyleWindow(this, 480, "Item in use");

        var body = DialogUi.Body();
        body.Children.Add(DialogUi.Text("Some files are in use", 18, bold: true));
        body.Children.Add(DialogUi.Text(
            "These programs have files open inside the item. Deleting now can make them lose data or crash, " +
            "and locked files may not be deleted.", 13, color: "App.TextMuted"));

        var list = new StackPanel { Spacing = 6 };
        foreach (var p in processes)
        {
            var line = new StackPanel { Spacing = 0 };
            line.Children.Add(DialogUi.Text(p.Name, 13, bold: true));
            var detail = $"PID {p.Pid}";
            if (!string.IsNullOrWhiteSpace(p.AppName) && !string.Equals(p.AppName, p.Name, StringComparison.OrdinalIgnoreCase))
                detail = p.AppName + "  -  " + detail;
            line.Children.Add(DialogUi.Text(detail, 11, color: "App.TextMuted"));
            list.Children.Add(line);
        }
        var scroll = new ScrollViewer { MaxHeight = 220, Content = list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        body.Children.Add(DialogUi.Card(scroll));

        var cancel = DialogUi.MakeButton("Cancel", primary: true);
        var anyway = DialogUi.MakeButton("Delete anyway", accentKey: "Badge.Critical");
        cancel.Click += (_, _) => Close(false);
        anyway.Click += (_, _) => Close(true);
        body.Children.Add(DialogUi.ButtonRow(cancel, anyway));

        Content = body;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close(false);
        };
        Opened += (_, _) => cancel.Focus();
    }
}
