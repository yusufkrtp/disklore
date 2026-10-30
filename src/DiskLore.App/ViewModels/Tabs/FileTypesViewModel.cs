using CommunityToolkit.Mvvm.ComponentModel;
using DiskLore.App.Services;
using DiskLore.Core.Model;
using DiskLore.Core.Scanning;

namespace DiskLore.App.ViewModels.Tabs;

public sealed class FileTypeRow
{
    public FileTypeRow(string extension, string label, long size, double fraction, double percentOfFolder)
    {
        Extension = extension;
        Label = label;
        Size = size;
        SizeText = SizeFormat.Format(size);
        Fraction = fraction;
        PercentText = percentOfFolder >= 10 ? $"{percentOfFolder:0}%" : percentOfFolder >= 0.1 ? $"{percentOfFolder:0.0}%" : "<0.1%";
    }

    public string Extension { get; }
    public string Label { get; }
    public long Size { get; }
    public string SizeText { get; }
    public double Fraction { get; }       // 0..1, relative to the largest row
    public string PercentText { get; }
}

public sealed partial class FileTypesViewModel : ObservableObject
{
    private int _version;

    [ObservableProperty] private IReadOnlyList<FileTypeRow> _rows = [];
    [ObservableProperty] private string _header = "File types";
    [ObservableProperty] private string _status = "Scan a drive or folder to see what kinds of files take the space.";
    [ObservableProperty] private bool _hasRows;

    // Folder the breakdown is computed for: the selected folder, the parent of a selected file, else the scan root.
    public static ScanNode? PickFolder(IAppContext ctx)
    {
        var root = ctx.Current?.Root;
        if (root == null) return null;
        var sel = ctx.Selected;
        if (sel == null) return root;
        return sel.IsDirectory ? sel : sel.Parent ?? root;
    }

    public async Task LoadAsync(IAppContext? ctx)
    {
        var version = ++_version;
        var folder = ctx == null ? null : PickFolder(ctx);
        if (folder == null)
        {
            Rows = [];
            HasRows = false;
            Header = "File types";
            Status = "Scan a drive or folder to see what kinds of files take the space.";
            return;
        }

        var name = DisplayName(folder);
        Header = $"File types in {name} ({SizeFormat.Format(folder.Size)})";
        Status = "Counting file types…";
        try
        {
            var breakdown = await Task.Run(() => SubtreeAnalyzer.ExtensionBreakdown(folder, 50));
            if (version != _version) return;
            var largest = breakdown.Count > 0 ? Math.Max(1, breakdown.Max(b => b.Size)) : 1;
            var total = Math.Max(1, folder.Size);
            Rows = breakdown
                .Select(b => new FileTypeRow(b.Name, FriendlyLabel(b.Name), b.Size, (double)b.Size / largest, 100.0 * b.Size / total))
                .ToList();
            HasRows = Rows.Count > 0;
            Status = HasRows ? "" : "There are no files in this folder.";
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            Rows = [];
            HasRows = false;
            Status = "Could not count file types: " + ex.Message;
        }
    }

    internal static string DisplayName(ScanNode folder)
        => folder.Name;

    public static string FriendlyLabel(string extension)
        => Labels.TryGetValue(extension.ToLowerInvariant(), out var label) ? label : "";

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [".vhd"] = "Virtual disk",
        [".vhdx"] = "Virtual disk",
        [".vmdk"] = "Virtual disk",
        [".vdi"] = "Virtual disk",
        [".iso"] = "Disk image",
        [".img"] = "Disk image",
        [".wim"] = "Windows image",
        [".esd"] = "Windows image",
        [".mp4"] = "Video",
        [".mkv"] = "Video",
        [".avi"] = "Video",
        [".mov"] = "Video",
        [".webm"] = "Video",
        [".mp3"] = "Audio",
        [".flac"] = "Audio",
        [".wav"] = "Audio",
        [".ogg"] = "Audio",
        [".m4a"] = "Audio",
        [".jpg"] = "Photo",
        [".jpeg"] = "Photo",
        [".png"] = "Image",
        [".gif"] = "Image",
        [".webp"] = "Image",
        [".psd"] = "Photoshop file",
        [".blend"] = "Blender scene",
        [".fbx"] = "3D model",
        [".obj"] = "3D model",
        [".glb"] = "3D model",
        [".msi"] = "Installer package",
        [".msp"] = "Installer package",
        [".exe"] = "Program",
        [".dll"] = "Library",
        [".sys"] = "Driver / system file",
        [".pak"] = "Game data",
        [".bundle"] = "Game data",
        [".uasset"] = "Game data",
        [".log"] = "Log",
        [".tmp"] = "Temporary",
        [".etl"] = "Trace log",
        [".dmp"] = "Crash dump",
        [".zip"] = "Archive",
        [".7z"] = "Archive",
        [".rar"] = "Archive",
        [".tar"] = "Archive",
        [".gz"] = "Archive",
        [".cab"] = "Archive",
        [".safetensors"] = "AI model",
        [".ckpt"] = "AI model",
        [".gguf"] = "AI model",
        [".onnx"] = "AI model",
        [".pdf"] = "Document",
        [".docx"] = "Document",
        [".xlsx"] = "Spreadsheet",
        [".pptx"] = "Presentation",
        [".db"] = "Database",
        [".sqlite"] = "Database",
        [".pyc"] = "Python cache",
        [".map"] = "Source map",
        [".(none)"] = "No extension",
        ["(none)"] = "No extension",
    };
}
