using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace AssetData.Parser;

/// <summary>
/// Base class for all parsed asset nodes. Observable for MVVM binding.
/// </summary>
public abstract class AssetNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private AssetNode? _parent;
    
    /// <summary>Field/property name from the asset definition.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }
    
    /// <summary>Parent node in the hierarchy.</summary>
    public AssetNode? Parent
    {
        get => _parent;
        internal set => _parent = value;
    }
    
    /// <summary>Child nodes (for struct/array types).</summary>
    public ObservableCollection<AssetNode> Children { get; } = [];
    
    /// <summary>The data type of this node.</summary>
    public abstract AssetNodeKind Kind { get; }
    
    /// <summary>Original binary offset (for debugging/editing).</summary>
    public int BinaryOffset { get; init; }
    
    /// <summary>Display string for the value.</summary>
    public abstract string DisplayValue { get; }
    
    /// <summary>Whether this node can be edited.</summary>
    public virtual bool IsEditable => true;
    
    /// <summary>Add a child node, setting parent reference.</summary>
    public void AddChild(AssetNode child)
    {
        child._parent = this;
        Children.Add(child);
    }
    
    public AssetNode? this[string name] =>
        Children.FirstOrDefault(c => c.Name == name);

    public IEnumerable<AssetNode> Elements =>
        this is ArrayNode ? Children : Enumerable.Empty<AssetNode>();

    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name != nameof(DisplayValue)) OnPropertyChanged(nameof(DisplayValue));
        return true;
    }
}

/// <summary>
/// Node type classification for UI rendering.
/// </summary>
public enum AssetNodeKind
{
    Struct,
    Array,
    String,
    Number,
    Bool,
    Enum,
    Asset,      // Asset reference (key path)
    Nullable,       // Nullable with no value
    Vector      // Vector2/3/4 or Orientation
}

/// <summary>
/// Struct container node.
/// </summary>
public sealed class StructNode : AssetNode
{
    public override AssetNodeKind Kind => AssetNodeKind.Struct;
    
    /// <summary>Struct type name from the catalog.</summary>
    public string TypeName { get; init; } = string.Empty;
    
    public override string DisplayValue => $"[{TypeName}]";
    public override bool IsEditable => false;
}

/// <summary>
/// Array container node.
/// </summary>
public sealed class ArrayNode : AssetNode
{
    public override AssetNodeKind Kind => AssetNodeKind.Array;
    
    /// <summary>Element type name.</summary>
    public string ElementType { get; init; } = string.Empty;
    
    public override string DisplayValue => $"[{Children.Count} items]";
    public override bool IsEditable => false;
}

/// <summary>
/// String value node (char*, key, asset reference).
/// </summary>
public sealed class StringNode : AssetNode
{
    private string _value = string.Empty;
    private AssetNodeKind _kind = AssetNodeKind.String;
    
    public override AssetNodeKind Kind => _kind;
    
    public AssetNodeKind NodeKind
    {
        get => _kind;
        init => _kind = value;
    }
    
    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
    
    public override string DisplayValue => Value;
}

/// <summary>
/// Localized string node with primary and secondary values (cLocalizedAssetString).
/// </summary>
public sealed class LocalizedStringNode : AssetNode
{
    private string _primaryValue = string.Empty;
    private string _secondaryValue = string.Empty;

    public override AssetNodeKind Kind => AssetNodeKind.String;

    public string PrimaryValue
    {
        get => _primaryValue;
        set => SetField(ref _primaryValue, value);
    }

    public string SecondaryValue
    {
        get => _secondaryValue;
        set => SetField(ref _secondaryValue, value);
    }

    public override string DisplayValue =>
        string.IsNullOrEmpty(SecondaryValue)
            ? PrimaryValue
            : $"{PrimaryValue} [{SecondaryValue}]";
}

/// <summary>
/// Numeric value node (int, uint, float, int64, uint64, uint16, uint8).
/// </summary>
public sealed class NumberNode : AssetNode
{
    private double _value;
    private NumberFormat _format = NumberFormat.Decimal;
    
    public override AssetNodeKind Kind => AssetNodeKind.Number;
    
    public double Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
    
    /// <summary>Original numeric type for proper serialization.</summary>
    public NumericType OriginalType { get; init; } = NumericType.Int32;
    
    /// <summary>Display format preference.</summary>
    public NumberFormat Format
    {
        get => _format;
        set => SetField(ref _format, value);
    }
    
    public override string DisplayValue => Format switch
    {
        NumberFormat.Hex => OriginalType switch
        {
            NumericType.UInt8 => $"0x{(byte)Value:X2}",
            NumericType.UInt16 => $"0x{(ushort)Value:X4}",
            NumericType.UInt32 or NumericType.HashId or NumericType.ObjId => $"0x{(uint)Value:X8}",
            NumericType.UInt64 => $"0x{(ulong)Value:X16}",
            _ => $"0x{(long)Value:X}"
        },
        NumberFormat.Float => Value.ToString("G9", CultureInfo.InvariantCulture),
        _ => OriginalType switch
        {
            NumericType.Float => Value.ToString("G9", CultureInfo.InvariantCulture),
            NumericType.Int64 => ((long)Value).ToString(),
            NumericType.UInt64 => ((ulong)Value).ToString(),
            NumericType.UInt8 => ((byte)Value).ToString(),
            NumericType.UInt16 => ((ushort)Value).ToString(),
            _ => ((int)Value).ToString()
        }
    };
}

/// <summary>
/// Boolean value node.
/// </summary>
public sealed class BooleanNode : AssetNode
{
    private bool _value;
    
    public override AssetNodeKind Kind => AssetNodeKind.Bool;
    
    public bool Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
    
    public override string DisplayValue => Value ? "true" : "false";
}

/// <summary>
/// Enum value node with resolved name.
/// </summary>
public sealed class EnumNode : AssetNode
{
    private uint _rawValue;
    private string _resolvedName = string.Empty;
    
    public override AssetNodeKind Kind => AssetNodeKind.Enum;
    
    /// <summary>Enum type name from catalog.</summary>
    public string EnumType { get; init; } = string.Empty;
    
    public uint RawValue
    {
        get => _rawValue;
        set
        {
            if (SetField(ref _rawValue, value))
                OnPropertyChanged(nameof(DisplayValue));
        }
    }
    
    public string ResolvedName
    {
        get => _resolvedName;
        set => SetField(ref _resolvedName, value);
    }
    
    public override string DisplayValue => string.IsNullOrEmpty(ResolvedName) 
        ? $"0x{RawValue:X8}" 
        : $"{ResolvedName} (0x{RawValue:X8})";
}

/// <summary>
/// Vector value node for Vector2, Vector3, Vector4, and Orientation.
/// </summary>
public sealed class VectorNode : AssetNode
{
    private float _x, _y, _z, _w;
    
    public override AssetNodeKind Kind => AssetNodeKind.Vector;
    
    /// <summary>Vector type: Vector2, Vector3, Vector4, or Orientation.</summary>
    public VectorType VectorType { get; init; } = VectorType.Vector3;
    
    public float X
    {
        get => _x;
        set => SetField(ref _x, value);
    }
    
    public float Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }
    
    public float Z
    {
        get => _z;
        set => SetField(ref _z, value);
    }
    
    public float W
    {
        get => _w;
        set => SetField(ref _w, value);
    }
    
    /// <summary>Number of components (2, 3, or 4).</summary>
    public int ComponentCount => VectorType switch
    {
        VectorType.Vector2 => 2,
        VectorType.Vector3 => 3,
        _ => 4
    };
    
    public override string DisplayValue => VectorType switch
    {
        VectorType.Vector2 => $"x: {FormatFloat(X)}, y: {FormatFloat(Y)}",
        VectorType.Vector3 => $"x: {FormatFloat(X)}, y: {FormatFloat(Y)}, z: {FormatFloat(Z)}",
        VectorType.Orientation => $"(quat) x: {FormatFloat(X)}, y: {FormatFloat(Y)}, z: {FormatFloat(Z)}, w: {FormatFloat(W)}",
        _ => $"x: {FormatFloat(X)}, y: {FormatFloat(Y)}, z: {FormatFloat(Z)}, w: {FormatFloat(W)}"
    };
    
    private static string FormatComponents(params float[] values)
        => string.Join(" ", values.Select(FormatFloat));
    
    private static string FormatFloat(float v)
        => v.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>
/// Null/empty node for nullable structs without value.
/// </summary>
public sealed class NullNode : AssetNode
{
    public override AssetNodeKind Kind => AssetNodeKind.Nullable;
    public override string DisplayValue => "(null)";
    public override bool IsEditable => false;
}

/// <summary>
/// Original numeric type for proper serialization.
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
    HashId,     // uint32 always displayed as hex
    ObjId  // uint32 always displayed as hex
}

/// <summary>
/// Display format for numbers.
/// </summary>
public enum NumberFormat
{
    Decimal,
    Hex,
    Float
}

/// <summary>
/// Vector type classification.
/// </summary>
public enum VectorType
{
    Vector2,
    Vector3,
    Vector4,
    Orientation
}