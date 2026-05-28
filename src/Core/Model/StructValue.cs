namespace AssetData.Parser.Model;

/// <summary>
/// A parsed struct instance: an ordered list of named field values.
/// Built mutably by the parser via <see cref="Add"/>; immutable to outside callers.
/// </summary>
public sealed class StructValue : AssetValue
{
    private readonly List<AssetValue> _fields = new();

    /// <summary>Registered type name (e.g. "Noun", "cSPBoundingBox").</summary>
    public string TypeName { get; init; } = string.Empty;

    public override AssetValueKind Kind => AssetValueKind.Struct;
    public override IReadOnlyList<AssetValue> Children => _fields;

    /// <summary>Append a parsed field value in declaration order. Used by the parser and by L2 → L1
    /// converters; consumers should treat the tree as immutable once construction is complete.</summary>
    public void Add(AssetValue field) => _fields.Add(field);
}

/// <summary>A parsed array instance: a sequence of element values (struct or primitive).</summary>
public sealed class ArrayValue : AssetValue
{
    private readonly List<AssetValue> _items = new();

    /// <summary>Element type name as authored in the catalog (for display/serialization).</summary>
    public string ElementType { get; init; } = string.Empty;

    public override AssetValueKind Kind => AssetValueKind.Array;
    public override IReadOnlyList<AssetValue> Children => _items;
    public IReadOnlyList<AssetValue> Items => _items;

    /// <summary>Append an element. Used by the parser and by L2 → L1 converters; consumers should
    /// treat the tree as immutable once construction is complete.</summary>
    public void Add(AssetValue item) => _items.Add(item);
}
