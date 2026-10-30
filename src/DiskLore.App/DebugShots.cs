#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DiskLore.App.Dialogs;
using DiskLore.App.Themes;
using DiskLore.App.ViewModels;
using DiskLore.App.Views;
using DiskLore.Core.Model;

namespace DiskLore.App;

// Debug-only deterministic screenshots: renders the window to a PNG without needing focus.
// Switches: --screenshot <png> [--theme <id>] [--tab <n>] [--select <path>] [--search <text>]
//           [--window settings|confirm|critical] [--delay <ms>] [--size WxH]
//           [--no-ai] [--no-auto-ai] [--ask <path>] [--probe <path>] [--shell on|off|status] [--log <file>]
// --no-ai disables Claude completely (no auto run, no Ask button); --no-auto-ai only skips the automatic run.
internal static class DebugShots
{
    public static string? Output;
    public static string? Theme;
    public static int? Tab;
    public static string? Select;
    public static string? Search;
    public static string? WindowKind;
    public static int Delay = 1500;
    public static (int W, int H)? Size;
    public static bool NoAi;
    public static bool NoAutoAi;
    public static string? Ask;
    public static string? ShellMode;
    public static string? Probe;
    public static string? LogFile;

    public static bool Active => Output != null || ShellMode != null || LogFile != null;

    public static void Log(string line)
    {
        if (LogFile == null) return;
        try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine); }
        catch (Exception) { }
    }

    public static string[] Parse(string[] args)
    {
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i])
            {
                case "--screenshot": Output = Next(); break;
                case "--theme": Theme = Next(); break;
                case "--tab": Tab = int.Parse(Next()); break;
                case "--select": Select = Next(); break;
                case "--search": Search = Next(); break;
                case "--window": WindowKind = Next(); break;
                case "--no-ai": NoAi = true; break;
                case "--no-auto-ai": NoAutoAi = true; break;
                case "--ask": Ask = Next(); break;
                case "--shell": ShellMode = Next(); break;
                case "--probe": Probe = Next(); break;
                case "--log": LogFile = Next(); break;
                case "--delay": Delay = int.Parse(Next()); break;
                case "--size":
                    var p = Next().Split('x');
                    Size = (int.Parse(p[0]), int.Parse(p[1]));
                    break;
                default: rest.Add(args[i]); break;
            }
        }
        return rest.ToArray();
    }

    public static async void Run(MainWindow window, MainViewModel vm)
    {
        try
        {
            Log("started; startup path = " + (App.StartupPath ?? "(none)"));
            if (ShellMode != null) RunShell(vm);
            if (Theme != null) ThemeManager.Apply(Theme);
            if (Size is { } s) { window.Width = s.W; window.Height = s.H; }
            var started = DateTime.UtcNow;
            // Wait for the scan (if any) and the first paint.
            while (App.StartupPath != null && !vm.HasResult && string.IsNullOrEmpty(vm.ScanError)
                   && (DateTime.UtcNow - started).TotalSeconds < 240)
                await Task.Delay(250);
            if (vm.Current != null)
                Log($"scan done: {vm.Current.Root.FullPath} size={vm.Current.Root.Size} files={vm.Current.Root.FileCount} folders={vm.Current.Root.FolderCount}");
            await vm.SnapshotTask;
            await Task.Delay(800);

            if (vm.Current != null && Select != null)
            {
                var node = Find(vm.Current.Root, Select);
                if (node != null) vm.Navigate(node);
            }
            if (Probe != null && vm.Current != null && Find(vm.Current.Root, Probe) is { } probeNode)
                foreach (var c in probeNode.Children.Where(c => c.IsDirectory && c.Size >= 20L << 20))
                {
                    var pe = vm.Explain(c);
                    Log($"probe {c.FullPath} size={c.Size} source={pe.Source} badge={pe.Badge}");
                }
            if (Ask != null && vm.Current != null && Find(vm.Current.Root, Ask) is { } askNode)
            {
                vm.Navigate(askNode);
                Log($"asking Claude about {askNode.FullPath}; before: source={vm.Explain(askNode).Source} badge={vm.Explain(askNode).Badge} aiAvailable={vm.AiAvailable}");
                await vm.AskClaudeAsync(askNode);
                Log("ask finished: " + vm.AiStatus + "; source = " + vm.Explain(askNode).Source);
            }
            if (Tab is { } t) vm.TabIndex = t;
            if (Search != null)
            {
                vm.SearchText = Search;
                await vm.SearchCommand.ExecuteAsync(null);
            }
            await Task.Delay(Delay);

            Window target = window;
            if (WindowKind == "settings")
            {
                var w = new SettingsWindow(vm);
                w.Show(window);
                await Task.Delay(700);
                target = w;
            }
            else if (WindowKind == "critical" && vm.Selected != null)
            {
                var n = vm.Selected;
                var w = new CriticalDeleteDialog(n, vm.Explain(n));
                w.Show(window);
                await Task.Delay(700);
                target = w;
            }
            else if (WindowKind == "confirm" && vm.Selected != null)
            {
                var n = vm.Selected;
                var w = new ConfirmDeleteDialog(n, vm.Explain(n), true, "Recycle Bin");
                w.Show(window);
                await Task.Delay(700);
                target = w;
            }

            if (Output != null) Save(target, Output);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("screenshot failed: " + ex);
        }
        finally
        {
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
    }

    // Toggles Explorer integration through the same view-model path the Settings checkbox uses.
    private static void RunShell(MainViewModel vm)
    {
        var settings = new SettingsViewModel(vm);
        _ = settings.ShellEnabled;   // the checkbox binding reads the current state first
        if (ShellMode == "on") settings.ShellEnabled = true;
        else if (ShellMode == "off") settings.ShellEnabled = false;
        Log($"shell integration: supported={settings.ShellSupported} enabled={settings.ShellEnabled} exe={Environment.ProcessPath} status={vm.StatusText}");
    }

    private static ScanNode? Find(ScanNode root, string path)
    {
        var node = root;
        if (string.Equals(node.FullPath.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return node;
        foreach (var part in path.Substring(Math.Min(path.Length, root.FullPath.Length)).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (node == null) return null;
        }
        return node;
    }

    private static void Save(Window w, string file)
    {
        var scale = w.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(w.Bounds.Width * scale), (int)Math.Ceiling(w.Bounds.Height * scale));
        using var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        rtb.Render(w);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        rtb.Save(file);
    }
}
#endif
