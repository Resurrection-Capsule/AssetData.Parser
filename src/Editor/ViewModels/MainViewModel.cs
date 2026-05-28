using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetData.Parser;
using AssetData.Parser.Editor.Models;

namespace AssetData.Parser.Editor.ViewModels;

/// <summary>
/// Main ViewModel for the asset editor. Owns the L2 <see cref="EditorNode"/> tree shown in the UI;
/// builds it from the parser's L1 <see cref="Model.AssetValue"/> output via <see cref="EditorTreeBuilder"/>.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AssetService _assetService;

    public ObservableCollection<EditorNode> Roots { get; } = [];

    public ObservableCollection<string> KeyAssetSuggestions { get; } = [];

    private List<EditorNode>? _flatCache;
    private int _searchIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedEditable))]
    [NotifyPropertyChangedFor(nameof(SelectedDisplayValue))]
    private EditorNode? _selected;

    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFind))]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _searchInKeys = true;

    [ObservableProperty]
    private bool _searchInValues = true;

    [ObservableProperty]
    private bool _caseSensitive;

    public bool IsSelectedEditable => Selected?.IsEditable ?? false;
    public string SelectedDisplayValue => Selected?.DisplayValue ?? string.Empty;
    public bool CanFind => !string.IsNullOrEmpty(FindText);

    public MainViewModel() : this(new AssetService()) { }

    public MainViewModel(AssetService assetService)
    {
        _assetService = assetService;
    }

    /// <summary>Load an asset file via Core, then adapt L1 → L2.</summary>
    [RelayCommand]
    public async Task LoadFileAsync(string filePath)
    {
        try
        {
            var value = await Task.Run(() => _assetService.LoadFile(filePath));
            var root = EditorTreeBuilder.Build(value);

            SetRoot(root);
            CurrentFilePath = filePath;
            IsDirty = false;

            BuildSuggestions();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load asset: {ex.Message}", ex);
        }
    }

    public void SetRoot(EditorNode root)
    {
        Roots.Clear();
        Roots.Add(root);
        _flatCache = null;
        Selected = null;
    }

    /// <summary>Export current asset to XML by serializing the L2 tree back through the L1 model.</summary>
    [RelayCommand]
    public async Task ExportXmlAsync(string outputPath)
    {
        if (Roots.Count == 0) return;
        var root = Roots[0];
        await Task.Run(() =>
        {
            var l1 = EditorToValue.Convert(root);
            _assetService.ExportXml(l1, outputPath);
        });
    }

    [RelayCommand(CanExecute = nameof(CanFind))]
    public void FindNext()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        EnsureFlatCache();
        if (_flatCache == null || _flatCache.Count == 0) return;

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int start = (_searchIndex + 1) % _flatCache.Count;
        int i = start;

        do
        {
            if (MatchNode(_flatCache[i], comparison))
            {
                _searchIndex = i;
                Selected = _flatCache[i];
                return;
            }
            i = (i + 1) % _flatCache.Count;
        } while (i != start);
    }

    [RelayCommand(CanExecute = nameof(CanFind))]
    public void FindPrev()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        EnsureFlatCache();
        if (_flatCache == null || _flatCache.Count == 0) return;

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int start = (_searchIndex - 1 + _flatCache.Count) % _flatCache.Count;
        int i = start;

        do
        {
            if (MatchNode(_flatCache[i], comparison))
            {
                _searchIndex = i;
                Selected = _flatCache[i];
                return;
            }
            i = (i - 1 + _flatCache.Count) % _flatCache.Count;
        } while (i != start);
    }

    [RelayCommand]
    public void ReplaceOne()
    {
        if (Selected == null || string.IsNullOrEmpty(FindText)) return;

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (SearchInKeys && Selected.Name.Contains(FindText, comparison))
        {
            Selected.Name = ReplaceFirst(Selected.Name, FindText, ReplaceText, comparison);
            IsDirty = true;
        }

        if (SearchInValues && Selected is StringNode sn && sn.Value.Contains(FindText, comparison))
        {
            sn.Value = ReplaceFirst(sn.Value, FindText, ReplaceText, comparison);
            IsDirty = true;
        }
    }

    [RelayCommand]
    public void ReplaceAll()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        EnsureFlatCache();
        if (_flatCache == null) return;

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var node in _flatCache)
        {
            if (SearchInKeys && node.Name.Contains(FindText, comparison))
            {
                node.Name = ReplaceAllOccurrences(node.Name, FindText, ReplaceText, comparison);
                IsDirty = true;
            }

            if (SearchInValues && node is StringNode sn && sn.Value.Contains(FindText, comparison))
            {
                sn.Value = ReplaceAllOccurrences(sn.Value, FindText, ReplaceText, comparison);
                IsDirty = true;
            }
        }
    }

    public StructDefinition? GetSelectedSchema()
    {
        if (Selected is StructNode sn)
            return _assetService.GetStructSchema(sn.TypeName);
        return null;
    }

    public EnumDefinition? GetSelectedEnumSchema()
    {
        if (Selected is EnumNode en)
            return _assetService.GetEnumSchema(en.EnumType);
        return null;
    }

    private void EnsureFlatCache()
    {
        if (_flatCache != null) return;
        _flatCache = [];
        foreach (var root in Roots)
            FlattenInto(root, _flatCache);
        _searchIndex = -1;
    }

    private static void FlattenInto(EditorNode node, List<EditorNode> result)
    {
        result.Add(node);
        foreach (var child in node.Children)
            FlattenInto(child, result);
    }

    private bool MatchNode(EditorNode node, StringComparison comparison)
    {
        if (SearchInKeys && node.Name.Contains(FindText, comparison))
            return true;

        if (SearchInValues)
        {
            return node switch
            {
                StringNode sn => sn.Value.Contains(FindText, comparison),
                NumberNode nn => nn.DisplayValue.Contains(FindText, comparison),
                BooleanNode bn => bn.DisplayValue.Contains(FindText, comparison),
                EnumNode en => en.DisplayValue.Contains(FindText, comparison),
                _ => false
            };
        }

        return false;
    }

    private void BuildSuggestions()
    {
        KeyAssetSuggestions.Clear();
        EnsureFlatCache();

        if (_flatCache == null) return;

        var paths = _flatCache
            .OfType<StringNode>()
            .Where(n => n.NodeKind == EditorNodeKind.Asset && !string.IsNullOrWhiteSpace(n.Value))
            .Select(n => n.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x);

        foreach (var path in paths)
            KeyAssetSuggestions.Add(path);
    }

    private static string ReplaceFirst(string source, string find, string replace, StringComparison comparison)
    {
        int index = source.IndexOf(find, comparison);
        if (index < 0) return source;
        return string.Concat(source.AsSpan(0, index), replace, source.AsSpan(index + find.Length));
    }

    private static string ReplaceAllOccurrences(string source, string find, string replace, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(find)) return source;

        int start = 0;
        while (true)
        {
            int index = source.IndexOf(find, start, comparison);
            if (index < 0) break;
            source = string.Concat(source.AsSpan(0, index), replace, source.AsSpan(index + find.Length));
            start = index + replace.Length;
        }
        return source;
    }

    partial void OnFindTextChanged(string value)
    {
        _flatCache = null;
        FindNextCommand.NotifyCanExecuteChanged();
        FindPrevCommand.NotifyCanExecuteChanged();
    }
}
