using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DiskLore.Core.Model;
using DiskLore.Core.Platform;

namespace DiskLore.App.ViewModels;

public sealed class DriveCardViewModel
{
    public DriveCardViewModel(DriveEntry drive, Action<DriveEntry> scan)
    {
        Drive = drive;
        ScanCommand = new RelayCommand(() => scan(drive));
    }

    public DriveEntry Drive { get; }
    public ICommand ScanCommand { get; }

    public string Root => Drive.RootPath;
    public string Title => string.IsNullOrWhiteSpace(Drive.Label) ? "Local disk" : Drive.Label;
    public string Format => Drive.Format;
    public long Used => Math.Max(0, Drive.TotalBytes - Drive.FreeBytes);
    public double UsedFraction => Drive.TotalBytes <= 0 ? 0 : Math.Clamp((double)Used / Drive.TotalBytes, 0, 1);
    public bool IsLow => Drive.TotalBytes > 0 && Drive.FreeBytes < Drive.TotalBytes / 10;
    public string UsageText => $"{SizeFormat.Format(Used)} used of {SizeFormat.Format(Drive.TotalBytes)}";
    public string FreeText => $"{SizeFormat.Format(Drive.FreeBytes)} free";
}
