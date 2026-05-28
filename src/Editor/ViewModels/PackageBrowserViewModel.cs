using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetData.Parser;
using AssetData.Parser.Editor.Models;
using AssetData.Parser.Model;

namespace AssetData.Parser.Editor.ViewModels;

/// <summary>Represents an entry in the DBPF package browser (from Catalog).</summary>
public partial class PackageEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _type = string.Empty;
    [ObservableProperty] private long _compileTime;
    [ObservableProperty] private int _version;
    [ObservableProperty] private uint _typeCrc;
    [ObservableProperty] private uint _dataCrc;
    [ObservableProperty] private string _sourceFile = string.Empty;
    [ObservableProperty] private List<string> _tags = new();

    public string FullName => string.IsNullOrEmpty(Type) ? Name : $"{Name}.{Type}";
    public string CompileTimeFormatted => DateTimeOffset.FromUnixTimeSeconds(CompileTime).ToString("yyyy-MM-dd HH:mm:ss");
    public string TagsDisplay => Tags.Count == 0 ? "" : string.Join(", ", Tags);
}

/// <summary>
/// ViewModel for the DBPF Package Browser window. Uses Catalog for optimized asset listing,
/// and adapts the parser's L1 tree into the editor's L2 tree before exposing it.
/// </summary>
public partial class PackageBrowserViewModel : ObservableObject
{
    private static readonly Regex CatalogPattern = new(@"^catalog_\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private DbpfReader? _dbpf;
    private readonly AssetService _assetService;

    public ObservableCollection<PackageEntryViewModel> AllEntries { get; } = [];
    public ObservableCollection<PackageEntryViewModel> FilteredEntries { get; } = [];
    public ObservableCollection<string> TypeFilters { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPackageOpen))]
    private string _packagePath = string.Empty;

    [ObservableProperty] private string _packageName = string.Empty;
    [ObservableProperty] private int _totalEntries;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedTypeFilter = "All";
    [ObservableProperty] private PackageEntryViewModel? _selectedEntry;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "No package loaded";

    public bool IsPackageOpen => _dbpf != null;

    /// <summary>Raised when the user opens an asset — payload is the editor-facing tree.</summary>
    public event Action<EditorNode, string>? AssetOpened;

    public PackageBrowserViewModel() : this(new AssetService()) { }

    public PackageBrowserViewModel(AssetService assetService)
    {
        _assetService = assetService;
    }

    public async Task OpenPackageAsync(string path)
    {
        if (!File.Exists(path)) return;

        IsLoading = true;
        StatusText = "Loading package...";

        try
        {
            _dbpf?.Dispose();
            AllEntries.Clear();
            FilteredEntries.Clear();
            TypeFilters.Clear();

            _dbpf = await Task.Run(() => new DbpfReader(path));
            PackagePath = path;
            PackageName = Path.GetFileName(path);

            var registryDir = Path.Combine(Path.GetDirectoryName(path) ?? "", "registries");
            if (Directory.Exists(registryDir))
                _dbpf.LoadRegistries(registryDir);

            StatusText = "Loading catalog...";

            var entries = await LoadCatalogAsync();

            if (entries == null || entries.Count == 0)
            {
                StatusText = "Error: Could not find or parse catalog_*.bin";
                return;
            }

            foreach (var entry in entries)
                AllEntries.Add(entry);

            TotalEntries = AllEntries.Count;

            var types = AllEntries
                .Select(e => e.Type)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t);

            TypeFilters.Add("All");
            foreach (var type in types)
                TypeFilters.Add(type);

            SelectedTypeFilter = "All";
            ApplyFilter();

            StatusText = $"Loaded {TotalEntries:N0} assets from catalog";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<List<PackageEntryViewModel>?> LoadCatalogAsync()
    {
        if (_dbpf == null) return null;

        byte[]? catalogData = null;
        for (int i = 131; i <= 150; i++)
        {
            var data = _dbpf.GetAsset($"catalog_{i}.bin");
            if (data != null && data.Length > 0)
            {
                catalogData = data;
                break;
            }
        }

        if (catalogData == null)
        {
            var data = _dbpf.GetAsset("catalog_0.bin");
            if (data != null && data.Length > 0)
                catalogData = data;
        }

        if (catalogData == null)
            return null;

        return await Task.Run(() => ParseCatalog(catalogData));
    }

    private List<PackageEntryViewModel>? ParseCatalog(byte[] data)
    {
        try
        {
            var catalogRoot = _assetService.Parser.Parse(data, "Catalog", 8) as StructValue;
            if (catalogRoot is null) return null;

            var entriesArray = catalogRoot.Children
                .OfType<ArrayValue>()
                .FirstOrDefault(n => n.Name == "entries");

            if (entriesArray == null)
                return null;

            var entries = new List<PackageEntryViewModel>();

            foreach (var entryNode in entriesArray.Children.OfType<StructValue>())
            {
                var vm = new PackageEntryViewModel();

                foreach (var field in entryNode.Children)
                {
                    switch (field.Name)
                    {
                        case "assetNameWType" when field is StringValue sv:
                            var parts = sv.Value.Split('.', 2);
                            vm.Name = parts[0];
                            vm.Type = parts.Length > 1 ? parts[1] : "";
                            break;

                        case "compileTime" when field is NumberValue nv:
                            vm.CompileTime = (long)nv.Value;
                            break;

                        case "version" when field is NumberValue nv:
                            vm.Version = (int)nv.Value;
                            break;

                        case "typeCrc" when field is NumberValue nv:
                            vm.TypeCrc = (uint)nv.Value;
                            break;

                        case "dataCrc" when field is NumberValue nv:
                            vm.DataCrc = (uint)nv.Value;
                            break;

                        case "sourceFileNameWType" when field is StringValue sv:
                            vm.SourceFile = sv.Value;
                            break;

                        case "tags" when field is ArrayValue av:
                            foreach (var tagNode in av.Children.OfType<StringValue>())
                            {
                                if (!string.IsNullOrWhiteSpace(tagNode.Value))
                                    vm.Tags.Add(tagNode.Value);
                            }
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(vm.Name))
                    entries.Add(vm);
            }

            return entries;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    public async Task OpenSelectedAssetAsync()
    {
        if (_dbpf == null || SelectedEntry == null) return;

        IsLoading = true;
        StatusText = $"Loading {SelectedEntry.FullName}...";

        try
        {
            var data = _dbpf.GetAsset(SelectedEntry.FullName);
            if (data == null)
            {
                StatusText = $"Failed to read asset: {SelectedEntry.FullName}";
                return;
            }

            var fileType = _assetService.GetFileType(SelectedEntry.Type);
            if (fileType == null)
            {
                StatusText = $"Unknown asset type: {SelectedEntry.Type}";
                return;
            }

            Console.WriteLine($"Loading {SelectedEntry.FullName}:");
            Console.WriteLine($"  Data size: {data.Length} bytes");
            Console.WriteLine($"  Type: {SelectedEntry.Type}");
            Console.WriteLine($"  Root struct: {fileType.RootStruct}");
            Console.WriteLine($"  Header size: {fileType.HeaderSize}");

            if (data.Length < fileType.HeaderSize)
            {
                StatusText = $"Error: Asset data ({data.Length} bytes) is smaller than header size ({fileType.HeaderSize} bytes)";
                return;
            }

            AssetValue value;
            try
            {
                value = await Task.Run(() =>
                    _assetService.Parser.Parse(data, fileType.RootStruct, fileType.HeaderSize));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine($"Parse failed with header size {fileType.HeaderSize}, trying alternatives...");

                int[] alternativeHeaders = [0, 4, 8, 12, 16];
                value = null!;
                Exception? lastException = ex;

                foreach (var altHeader in alternativeHeaders)
                {
                    if (altHeader == fileType.HeaderSize) continue;
                    if (data.Length < altHeader) continue;

                    try
                    {
                        Console.WriteLine($"  Trying header size: {altHeader}");
                        value = await Task.Run(() =>
                            _assetService.Parser.Parse(data, fileType.RootStruct, altHeader));
                        Console.WriteLine($"  Success with header size {altHeader}!");
                        break;
                    }
                    catch (Exception altEx)
                    {
                        Console.WriteLine($"  Failed: {altEx.Message}");
                        lastException = altEx;
                    }
                }

                if (value == null)
                {
                    StatusText = $"Error: Could not parse asset with any header size\n{lastException?.Message}";
                    return;
                }
            }

            var editorRoot = EditorTreeBuilder.Build(value);
            AssetOpened?.Invoke(editorRoot, SelectedEntry.FullName);
            StatusText = $"Opened: {SelectedEntry.FullName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Console.WriteLine($"Full error:\n{ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();

        var query = AllEntries.AsEnumerable();

        if (SelectedTypeFilter != "All")
            query = query.Where(e => e.Type.Equals(SelectedTypeFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Tags.Any(tag => tag.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var entry in query.OrderBy(e => e.Name))
            FilteredEntries.Add(entry);

        StatusText = $"Showing {FilteredEntries.Count:N0} of {TotalEntries:N0} assets";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    public void Dispose()
    {
        _dbpf?.Dispose();
    }
}
