using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AssetData.Parser;
using AssetData.Parser.Editor.Models;
using AssetData.Parser.Editor.Services;
using AssetData.Parser.Editor.ViewModels;
using AssetData.Parser.Editor.Views.Windows;

namespace AssetData.Parser.Editor;

public partial class MainWindow : Window
{
    public static readonly StyledProperty<GridLength> KeyColWidthProperty =
        AvaloniaProperty.Register<MainWindow, GridLength>(nameof(KeyColWidth), new GridLength(280));
    public static readonly StyledProperty<GridLength> TypeColWidthProperty =
        AvaloniaProperty.Register<MainWindow, GridLength>(nameof(TypeColWidth), new GridLength(160));

    public GridLength KeyColWidth
    {
        get => GetValue(KeyColWidthProperty);
        set => SetValue(KeyColWidthProperty, value);
    }

    public GridLength TypeColWidth
    {
        get => GetValue(TypeColWidthProperty);
        set => SetValue(TypeColWidthProperty, value);
    }

    // Converters for Vector component visibility
    public static readonly IValueConverter GreaterThan2Converter = 
        new FuncValueConverter<int, bool>(count => count > 2);
    
    public static readonly IValueConverter GreaterThan3Converter = 
        new FuncValueConverter<int, bool>(count => count > 3);

    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private PackageBrowserWindow? _packageBrowser;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Selected) && ViewModel?.Selected != null)
        {
            SelectAndRevealNode(ViewModel.Selected);
        }
    }

    private void SelectAndRevealNode(EditorNode node)
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null) return;
        
        ExpandPath(node);
        if (!Equals(tv.SelectedItem, node)) 
            tv.SelectedItem = node;
        
        var container = FindContainer(tv, node);
        container?.BringIntoView();
    }

    private void ExpandPath(EditorNode node)
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null) return;
        
        var stack = new Stack<EditorNode>();
        var parent = node.Parent;
        while (parent != null)
        {
            stack.Push(parent);
            parent = parent.Parent;
        }
        
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var container = FindContainer(tv, current);
            if (container != null) 
                container.IsExpanded = true;
        }
    }

    private TreeViewItem? FindContainer(ItemsControl root, object item)
    {
        foreach (var c in root.GetRealizedContainers())
        {
            if (c is TreeViewItem tvi)
            {
                if (Equals(tvi.DataContext, item))
                    return tvi;

                var child = FindContainer(tvi, item);
                if (child != null)
                    return child;
            }
        }
        return null;
    }

    private void ExpandSubTree(TreeViewItem item)
    {
        item.IsExpanded = true;
        foreach (var c in item.GetRealizedContainers())
            if (c is TreeViewItem tvi)
                ExpandSubTree(tvi);
    }

    private void CollapseSubTree(TreeViewItem item)
    {
        foreach (var c in item.GetRealizedContainers())
            if (c is TreeViewItem tvi)
                CollapseSubTree(tvi);
        item.IsExpanded = false;
    }

    private void CollapseAll()
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null) return;
        foreach (var c in tv.GetRealizedContainers())
            if (c is TreeViewItem tvi)
                CollapseSubTree(tvi);
    }

    private void ExpandAll()
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null) return;
        foreach (var c in tv.GetRealizedContainers())
            if (c is TreeViewItem tvi)
                ExpandSubTree(tvi);
    }

    private void OnContextExpandAll(object? sender, RoutedEventArgs e) => ExpandAll();
    private void OnContextCollapseAll(object? sender, RoutedEventArgs e) => CollapseAll();

    private void OnContextExpandNode(object? sender, RoutedEventArgs e)
    {
        var tvi = (sender as Control)?
            .GetLogicalAncestors()
            .OfType<TreeViewItem>()
            .FirstOrDefault();
        if (tvi != null) ExpandSubTree(tvi);
    }

    private void OnContextCollapseNode(object? sender, RoutedEventArgs e)
    {
        var tvi = (sender as Control)?
            .GetLogicalAncestors()
            .OfType<TreeViewItem>()
            .FirstOrDefault();
        if (tvi != null) CollapseSubTree(tvi);
    }

    private void ToggleFindBar()
    {
        var bar = this.FindControl<Border>("FindBar");
        if (bar != null) bar.IsVisible = !bar.IsVisible;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        
        var settings = SettingsService.Load();
        if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }
        if (settings.IsMaximized) 
            WindowState = WindowState.Maximized;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F:
                    ToggleFindBar();
                    e.Handled = true;
                    return;
                case Key.P:
                    OnOpenPackageClick(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
            }
        }
        
        if (e.Key == Key.Escape)
        {
            var bar = this.FindControl<Border>("FindBar");
            if (bar is { IsVisible: true })
            {
                bar.IsVisible = false;
                e.Handled = true;
            }
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = SettingsService.Load();
        settings.IsMaximized = WindowState == WindowState.Maximized;
        if (!settings.IsMaximized)
        {
            settings.WindowWidth = Bounds.Width;
            settings.WindowHeight = Bounds.Height;
        }
        SettingsService.Save(settings);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && sender is TreeView tv)
        {
            ViewModel.Selected = tv.SelectedItem as EditorNode;
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Load();
        
        // Get supported file types from Core
        var assetService = ServiceLocator.Get<AssetService>();
        var extensions = assetService.SupportedExtensions
            .Select(ext => $"*.{ext}")
            .ToList();
        
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open Asset",
            SuggestedStartLocation = settings.GetStartFolder(StorageProvider),
            FileTypeFilter =
            [
                new FilePickerFileType("Darkspore Assets") { Patterns = extensions },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files == null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            await ShowMessageBox("Please select a local file from disk.");
            return;
        }

        settings.LastOpenDirectory = Path.GetDirectoryName(path) ?? settings.LastOpenDirectory;
        SettingsService.Save(settings);

        try
        {
            // Load file and update root node name to show filename instead of type
            await ViewModel!.LoadFileAsync(path);
            
            // Update root node name to filename without extension
            if (ViewModel.Roots.Count > 0)
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                ViewModel.Roots[0].Name = fileName;
            }
        }
        catch (Exception ex)
        {
            await ShowMessageBox($"Failed to load file:\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
        }
    }

    private async void OnOpenPackageClick(object? sender, RoutedEventArgs e)
    {
        var assetService = ServiceLocator.Get<AssetService>();
        
        // Create or reuse package browser window
        if (_packageBrowser == null || !_packageBrowser.IsVisible)
        {
            _packageBrowser = new PackageBrowserWindow(assetService);
            _packageBrowser.AssetOpened += OnPackageAssetOpened;
        }
        
        _packageBrowser.Show();
        _packageBrowser.Activate();
    }
    
    private void OnPackageAssetOpened(EditorNode root, string assetName)
    {
        if (ViewModel == null) return;
        
        ViewModel.SetRoot(root);
        
        // Set name to filename without extension
        var fileNameWithoutExt = assetName;
        var lastDot = assetName.LastIndexOf('.');
        if (lastDot > 0)
            fileNameWithoutExt = assetName[..lastDot];
        
        root.Name = fileNameWithoutExt;
        ViewModel.CurrentFilePath = $"[Package] {assetName}";
        ViewModel.IsDirty = false;
        
        // Bring main window to front
        Activate();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Roots.Count == 0)
        {
            await ShowMessageBox("No asset loaded to save.");
            return;
        }
        
        var options = new FilePickerSaveOptions
        {
            Title = "Export as XML",
            DefaultExtension = "xml",
            FileTypeChoices =
            [
                new FilePickerFileType("XML File") { Patterns = ["*.xml"] }
            ]
        };
        
        var file = await StorageProvider.SaveFilePickerAsync(options);
        if (file == null) return;
        
        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        
        try
        {
            await ViewModel!.ExportXmlAsync(path);
            await ShowMessageBox($"Exported to:\n{path}");
        }
        catch (Exception ex)
        {
            await ShowMessageBox($"Failed to export:\n{ex.Message}");
        }
    }

    private async Task ShowMessageBox(string text)
    {
        var textBox = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#404040")),
            Background = new SolidColorBrush(Color.Parse("#1a1a1a")),
            Padding = new Thickness(8),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12
        };

        var scrollViewer = new ScrollViewer
        {
            Content = textBox,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var copyBtn = new Button { Content = "Copy", MinWidth = 80 };
        var okBtn = new Button { Content = "OK", MinWidth = 80 };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { copyBtn, okBtn }
        };

        var panel = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true,
            Children = { buttonPanel, scrollViewer }
        };
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        buttonPanel.Margin = new Thickness(0, 12, 0, 0);

        var dlg = new Window
        {
            Title = "Info",
            Width = 700,
            Height = 350,
            MinWidth = 400,
            MinHeight = 200,
            CanResize = true,
            Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        copyBtn.Click += async (_, _) =>
        {
            if (dlg.Clipboard != null)
                await dlg.Clipboard.SetTextAsync(text);
        };
        okBtn.Click += (_, _) => dlg.Close();

        await dlg.ShowDialog(this);
    }

    private void OnExpandClick(object? sender, RoutedEventArgs e)
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null || ViewModel?.Selected == null) return;
        var c = FindContainer(tv, ViewModel.Selected);
        if (c != null) c.IsExpanded = true;
    }

    private void OnCollapseClick(object? sender, RoutedEventArgs e)
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null || ViewModel?.Selected == null) return;
        var c = FindContainer(tv, ViewModel.Selected);
        if (c != null) c.IsExpanded = false;
    }

    private void OnExpandBranchClick(object? sender, RoutedEventArgs e)
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null || ViewModel?.Selected == null) return;
        ExpandPath(ViewModel.Selected);
        var c = FindContainer(tv, ViewModel.Selected);
        if (c != null) ExpandSubTree(c);
    }

    private void OnCollapseBranchClick(object? sender, RoutedEventArgs e)
    {
        var tv = this.FindControl<TreeView>("Tree");
        if (tv == null || ViewModel?.Selected == null) return;
        var c = FindContainer(tv, ViewModel.Selected);
        if (c != null) CollapseSubTree(c);
    }

    private void OnExpandAllClick(object? sender, RoutedEventArgs e) => ExpandAll();
    private void OnCollapseAllClick(object? sender, RoutedEventArgs e) => CollapseAll();
}