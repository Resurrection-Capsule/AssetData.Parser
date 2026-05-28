using System.Globalization;
using System.Xml.Linq;
using AssetData.Parser.Model;

namespace AssetData.Parser;

/// <summary>
/// Serializes L1 <see cref="AssetValue"/> trees to XML for export/round-trip. Pure transformation
/// on the lean tree; no editor concerns. (The editor-side XAML round-trip lives in the Editor.)
/// </summary>
public static class AssetSerializer
{
    public static XDocument ToXml(AssetValue root)
    {
        var doc = new XDocument();
        doc.Add(SerializeNode(root));
        return doc;
    }

    public static string ToXmlString(AssetValue root, bool indent = true)
    {
        var doc = ToXml(root);
        return doc.ToString(indent ? SaveOptions.None : SaveOptions.DisableFormatting);
    }

    private static XElement SerializeNode(AssetValue node)
    {
        var element = new XElement(SanitizeName(node.Name));

        switch (node)
        {
            case StructValue s:
                foreach (var field in s.Children)
                    element.Add(SerializeNode(field));
                break;

            case ArrayValue a:
                foreach (var child in a.Children)
                {
                    var entry = new XElement("entry");
                    if (child is StructValue childStruct)
                    {
                        foreach (var grandChild in childStruct.Children)
                            entry.Add(SerializeNode(grandChild));
                    }
                    else
                    {
                        entry.Value = FormatLeaf(child);
                    }
                    element.Add(entry);
                }
                break;

            case StringValue s:
                element.Value = s.Value;
                break;

            case LocalizedStringValue l:
                element.Value = string.IsNullOrEmpty(l.SecondaryValue)
                    ? l.PrimaryValue
                    : $"{l.PrimaryValue} [{l.SecondaryValue}]";
                break;

            case NumberValue n:
                element.Value = n.OriginalType switch
                {
                    NumericType.Float => n.Value.ToString("G9", CultureInfo.InvariantCulture),
                    NumericType.Int64 or NumericType.UInt64
                        => ((long)n.Value).ToString(CultureInfo.InvariantCulture),
                    _ => ((int)n.Value).ToString(CultureInfo.InvariantCulture)
                };
                break;

            case BoolValue b:
                element.Value = b.Value ? "true" : "false";
                break;

            case EnumValue e:
                element.Value = string.IsNullOrEmpty(e.ResolvedName)
                    ? $"0x{e.RawValue:X8}"
                    : $"{e.ResolvedName}, 0x{e.RawValue:X8}";
                break;

            case VectorValue v:
                element.Value = FormatVector(v);
                break;

            case NullValue:
                // empty
                break;
        }

        return element;
    }

    private static string FormatLeaf(AssetValue node) => node switch
    {
        StringValue s => s.Value,
        LocalizedStringValue l => string.IsNullOrEmpty(l.SecondaryValue)
            ? l.PrimaryValue
            : $"{l.PrimaryValue} [{l.SecondaryValue}]",
        NumberValue n => n.OriginalType == NumericType.Float
            ? n.Value.ToString("G9", CultureInfo.InvariantCulture)
            : ((long)n.Value).ToString(CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "true" : "false",
        EnumValue e => string.IsNullOrEmpty(e.ResolvedName)
            ? $"0x{e.RawValue:X8}"
            : $"{e.ResolvedName}, 0x{e.RawValue:X8}",
        VectorValue v => FormatVector(v),
        _ => string.Empty
    };

    private static string FormatVector(VectorValue v)
    {
        var ci = CultureInfo.InvariantCulture;
        return v.VectorType switch
        {
            VectorType.Vector2 => $"{v.X.ToString("G9", ci)} {v.Y.ToString("G9", ci)}",
            VectorType.Vector3 => $"{v.X.ToString("G9", ci)} {v.Y.ToString("G9", ci)} {v.Z.ToString("G9", ci)}",
            _                  => $"{v.X.ToString("G9", ci)} {v.Y.ToString("G9", ci)} {v.Z.ToString("G9", ci)} {v.W.ToString("G9", ci)}"
        };
    }

    private static string SanitizeName(string name)
    {
        if (name.StartsWith('[') && name.EndsWith(']'))
            return "entry";
        return name;
    }
}

/// <summary>
/// High-level API for consumers (CLI, Editor entry points). Lazy parser; thread-safe enough for
/// the editor's single-parser pattern.
/// </summary>
public sealed class AssetService
{
    private readonly Lazy<AssetParser> _parser = new(() => new AssetParser());

    public AssetParser Parser => _parser.Value;

    public AssetValue LoadFile(string filePath) => Parser.ParseFile(filePath);

    public AssetValue Load(Stream stream, string rootStructName, int headerSize)
        => Parser.Parse(stream, rootStructName, headerSize);

    public AssetValue Load(byte[] data, string rootStructName, int headerSize)
        => Parser.Parse(data, rootStructName, headerSize);

    public FileTypeInfo? GetFileType(string extension) => Parser.GetFileType(extension);

    public IEnumerable<string> SupportedExtensions => Parser.SupportedTypes;

    public void ExportXml(AssetValue root, string outputPath)
    {
        var xml = AssetSerializer.ToXmlString(root);
        File.WriteAllText(outputPath, xml);
    }

    public StructDefinition? GetStructSchema(string typeName) => Parser.Structs.GetValueOrDefault(typeName);
    public EnumDefinition? GetEnumSchema(string enumName) => Parser.Enums.GetValueOrDefault(enumName);

    /// <summary>Depth-first flatten of the tree, useful for search/iteration.</summary>
    public List<AssetValue> Flatten(AssetValue root)
    {
        var result = new List<AssetValue>();
        FlattenRecursive(root, result);
        return result;
    }

    private static void FlattenRecursive(AssetValue node, List<AssetValue> result)
    {
        result.Add(node);
        foreach (var child in node.Children)
            FlattenRecursive(child, result);
    }
}
