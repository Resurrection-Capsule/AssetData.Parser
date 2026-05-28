using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AssetData.Parser.Model;

namespace AssetData.Parser.Editor.Models;

/// <summary>
/// L2 (editor-facing, observable) wrapper around an immutable L1 <see cref="AssetValue"/>.
/// Adds INotifyPropertyChanged, mutable Value/Name/etc., parent links, an observable Children
/// collection, plus the formatting (<see cref="DisplayValue"/>) and edit-state
/// (<see cref="IsEditable"/>) the WPF/Avalonia bindings need. The parser never sees this type.
/// </summary>
public abstract class EditorNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private EditorNode? _parent;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public EditorNode? Parent
    {
        get => _parent;
        internal set => _parent = value;
    }

    public ObservableCollection<EditorNode> Children { get; } = [];

    public abstract EditorNodeKind Kind { get; }

    public int BinaryOffset { get; init; }

    public abstract string DisplayValue { get; }

    public virtual bool IsEditable => true;

    public void AddChild(EditorNode child)
    {
        child._parent = this;
        Children.Add(child);
    }

    public EditorNode? this[string name] =>
        Children.FirstOrDefault(c => c.Name == name);

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

/// <summary>Editor-facing kind discriminator (mirrors the L1 <see cref="AssetValueKind"/>).</summary>
public enum EditorNodeKind
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

public sealed class StructNode : EditorNode
{
    public override EditorNodeKind Kind => EditorNodeKind.Struct;
    public string TypeName { get; init; } = string.Empty;
    public override string DisplayValue => $"[{TypeName}]";
    public override bool IsEditable => false;
}

public sealed class ArrayNode : EditorNode
{
    public override EditorNodeKind Kind => EditorNodeKind.Array;
    public string ElementType { get; init; } = string.Empty;
    public override string DisplayValue => $"[{Children.Count} items]";
    public override bool IsEditable => false;
}

public sealed class StringNode : EditorNode
{
    private string _value = string.Empty;
    private EditorNodeKind _kind = EditorNodeKind.String;

    public override EditorNodeKind Kind => _kind;

    public EditorNodeKind NodeKind
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

public sealed class LocalizedStringNode : EditorNode
{
    private string _primaryValue = string.Empty;
    private string _secondaryValue = string.Empty;

    public override EditorNodeKind Kind => EditorNodeKind.String;

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

public sealed class NumberNode : EditorNode
{
    private double _value;
    private NumberFormat _format = NumberFormat.Decimal;

    public override EditorNodeKind Kind => EditorNodeKind.Number;

    public double Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public NumericType OriginalType { get; init; } = NumericType.Int32;

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

public sealed class BooleanNode : EditorNode
{
    private bool _value;

    public override EditorNodeKind Kind => EditorNodeKind.Bool;

    public bool Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public override string DisplayValue => Value ? "true" : "false";
}

public sealed class EnumNode : EditorNode
{
    private uint _rawValue;
    private string _resolvedName = string.Empty;

    public override EditorNodeKind Kind => EditorNodeKind.Enum;

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

public sealed class VectorNode : EditorNode
{
    private float _x, _y, _z, _w;

    public override EditorNodeKind Kind => EditorNodeKind.Vector;

    public VectorType VectorType { get; init; } = VectorType.Vector3;

    public float X { get => _x; set => SetField(ref _x, value); }
    public float Y { get => _y; set => SetField(ref _y, value); }
    public float Z { get => _z; set => SetField(ref _z, value); }
    public float W { get => _w; set => SetField(ref _w, value); }

    public int ComponentCount => VectorType switch
    {
        VectorType.Vector2 => 2,
        VectorType.Vector3 => 3,
        _ => 4
    };

    public override string DisplayValue => VectorType switch
    {
        VectorType.Vector2 => $"x: {Fmt(X)}, y: {Fmt(Y)}",
        VectorType.Vector3 => $"x: {Fmt(X)}, y: {Fmt(Y)}, z: {Fmt(Z)}",
        VectorType.Orientation => $"(quat) x: {Fmt(X)}, y: {Fmt(Y)}, z: {Fmt(Z)}, w: {Fmt(W)}",
        _ => $"x: {Fmt(X)}, y: {Fmt(Y)}, z: {Fmt(Z)}, w: {Fmt(W)}"
    };

    private static string Fmt(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

public sealed class NullNode : EditorNode
{
    public override EditorNodeKind Kind => EditorNodeKind.Nullable;
    public override string DisplayValue => "(null)";
    public override bool IsEditable => false;
}
