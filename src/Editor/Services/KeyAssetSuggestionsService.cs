using AssetData.Parser.Editor.Models;

namespace AssetData.Parser.Editor.Services;

/// <summary>
/// Extracts asset key suggestions from an EditorNode tree.
/// Used for autocomplete in asset path fields.
/// </summary>
public static class KeyAssetSuggestionsService
{
    public static IEnumerable<string> Extract(IEnumerable<EditorNode> roots)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            Walk(root, set);
        return set.OrderBy(x => x);
    }

    public static IEnumerable<string> Extract(EditorNode root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, set);
        return set.OrderBy(x => x);
    }

    private static void Walk(EditorNode node, HashSet<string> set)
    {
        if (node is StringNode sn && sn.NodeKind == EditorNodeKind.Asset)
        {
            if (!string.IsNullOrWhiteSpace(sn.Value))
                set.Add(sn.Value);
        }

        foreach (var child in node.Children)
            Walk(child, set);
    }
}
