using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiskLore.App.Converters;

public static class RowConverters
{
    public static readonly IValueConverter FolderWeight =
        new FuncValueConverter<bool, FontWeight>(isFolder => isFolder ? FontWeight.Medium : FontWeight.Normal);

    public static readonly IValueConverter BoolToWidth =
        new FuncValueConverter<bool, double>(b => b ? 1 : 0);
}
