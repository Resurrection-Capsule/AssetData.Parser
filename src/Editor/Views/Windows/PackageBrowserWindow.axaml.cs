using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetData.Parser;
using AssetData.Parser.Editor.Models;
using AssetData.Parser.Editor.Services;
using AssetData.Parser.Editor.ViewModels;

namespace AssetData.Parser.Editor.Views.Windows;

public partial class PackageBrowserWindow : Window
{
    private PackageBrowserViewModel? ViewModel => DataContext as PackageBrowserViewModel;
    
    public PackageBrowserWindow()
    {
        InitializeComponent();
    }
    
    public PackageBrowserWindow(AssetService assetService) : this()
    {
        DataContext = new PackageBrowserViewModel(assetService);
    }
    
    /// <summary>
    /// Event raised when an asset is opened from the package.
    /// </summary>
    public event Action<EditorNode, string>? AssetOpened;
    
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        if (ViewModel != null)
        {
            ViewModel.AssetOpened += OnAssetOpened;
        }
    }
    
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        
        if (ViewModel != null)
        {
            ViewModel.AssetOpened -= OnAssetOpened;
            ViewModel.Dispose();
        }
    }
    
    private void OnAssetOpened(EditorNode root, string name)
    {
        AssetOpened?.Invoke(root, name);
    }
    
    /// <summary>
    /// Handle double-click on DataGrid to open asset.
    /// </summary>
    private async void OnDataGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Check if an entry is selected
        if (ViewModel?.SelectedEntry == null) return;
        
        // Open the selected asset
        await ViewModel.OpenSelectedAssetAsync();
    }
    
    /// <summary>
    /// Handle Open Package button click - shows file dialog.
    /// </summary>
    private async void OnOpenPackageClick(object? sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Load();
        
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open Darkspore Asset Package",
            SuggestedStartLocation = settings.GetStartFolder(StorageProvider),
            FileTypeFilter =
            [
                new FilePickerFileType("Darkspore Asset Package") { Patterns = ["AssetData_Binary.package"] }
            ]
        };
        
        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files == null || files.Count == 0) return;
        
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        
        // Verify it's AssetData_Binary.package
        var fileName = Path.GetFileName(path);
        if (!fileName.Equals("AssetData_Binary.package", StringComparison.OrdinalIgnoreCase))
        {
            // Show error - wrong file
            return;
        }
        
        settings.LastOpenDirectory = Path.GetDirectoryName(path) ?? settings.LastOpenDirectory;
        SettingsService.Save(settings);
        
        if (ViewModel != null)
        {
            await ViewModel.OpenPackageAsync(path);
        }
    }
    
    /// <summary>
    /// Open a package directly (for use from main window).
    /// </summary>
    public async Task OpenPackageAsync(string path)
    {
        if (ViewModel != null)
        {
            await ViewModel.OpenPackageAsync(path);
        }
    }
}