namespace AssetData.Parser.Model;

/// <summary>
/// L1 (lean, immutable) asset tree node — the parser's true output. Has no INPC, no observable
/// collections, no display formatting, no edit flags. The Editor wraps this in an INPC-bearing
/// adapter (<c>AssetData.Parser.Editor.Models.*Node</c>); the CLI/Wiki consume it directly.
/// Mirrors the client's parsed-tree shape: a struct/array carries children, leaves carry values.
/// </summary>
public abstract class AssetValue
{
    /// <summary>Field/element name (positional from the descriptor; "[i]" for array entries).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Source offset of this value inside the binary blob (debug only).</summary>
    public int BinaryOffset { get; init; }

    /// <summary>Coarse-grained kind for callers that don't want to pattern-match.</summary>
    public abstract AssetValueKind Kind { get; }

    /// <summary>Children for container kinds (struct/array). Leaves return an empty list.</summary>
    public virtual IReadOnlyList<AssetValue> Children => System.Array.Empty<AssetValue>();
}

/// <summary>
/// Kind discriminator for callers that don't want to pattern-match. Editor adapters mirror this
/// enum 1:1 so round-trip into L2 is symbol-for-symbol.
/// </summary>
public enum AssetValueKind
{
    Struct,
    Array,
    String,
    Number,
    Bool,
    Enum,
    Asset,
    Nullable,
    Vector
}

/// <summary>
/// Original numeric width/identity of a <see cref="NumberValue"/>, preserved for round-trip
/// serialization (XML/text) and display formatting.
/// </summary>
public enum NumericType
{
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    UInt16,
    UInt8,
    HashId,
    ObjId
}

/// <summary>Display-time formatting preference; serializers/editors honor this for text output.</summary>
public enum NumberFormat
{
    Decimal,
    Hex,
    Float
}

/// <summary>Vector arity / role.</summary>
public enum VectorType
{
    Vector2,
    Vector3,
    Vector4,
    Orientation
}
