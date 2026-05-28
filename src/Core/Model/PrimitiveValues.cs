namespace AssetData.Parser.Model;

/// <summary>String value (char*, key, asset reference, inline char buffer).</summary>
public sealed class StringValue : AssetValue
{
    public string Value { get; init; } = string.Empty;

    /// <summary>Whether this represents a plain string or a resolved asset reference path.</summary>
    public AssetValueKind NodeKind { get; init; } = AssetValueKind.String;

    public override AssetValueKind Kind => NodeKind;
}

/// <summary>Two-string localized reference (cLocalizedAssetString).</summary>
public sealed class LocalizedStringValue : AssetValue
{
    public string PrimaryValue { get; init; } = string.Empty;
    public string SecondaryValue { get; init; } = string.Empty;

    public override AssetValueKind Kind => AssetValueKind.String;
}

/// <summary>Numeric value — width and format intent come from <see cref="OriginalType"/>/<see cref="Format"/>.</summary>
public sealed class NumberValue : AssetValue
{
    public double Value { get; init; }
    public NumericType OriginalType { get; init; } = NumericType.Int32;
    public NumberFormat Format { get; init; } = NumberFormat.Decimal;

    public override AssetValueKind Kind => AssetValueKind.Number;
}

/// <summary>Boolean (stored as 4-byte int on disk).</summary>
public sealed class BoolValue : AssetValue
{
    public bool Value { get; init; }
    public override AssetValueKind Kind => AssetValueKind.Bool;
}

/// <summary>Enum value with the raw u32 and optionally a resolved symbolic name.</summary>
public sealed class EnumValue : AssetValue
{
    public string EnumType { get; init; } = string.Empty;
    public uint RawValue { get; init; }
    public string ResolvedName { get; init; } = string.Empty;

    public override AssetValueKind Kind => AssetValueKind.Enum;
}

/// <summary>Vector value (Vector2/3/4 or quaternion-as-Orientation).</summary>
public sealed class VectorValue : AssetValue
{
    public VectorType VectorType { get; init; } = VectorType.Vector3;
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float W { get; init; }

    public int ComponentCount => VectorType switch
    {
        VectorType.Vector2 => 2,
        VectorType.Vector3 => 3,
        _ => 4
    };

    public override AssetValueKind Kind => AssetValueKind.Vector;
}

/// <summary>Sentinel for a Nullable struct that was absent (hasValue == 0).</summary>
public sealed class NullValue : AssetValue
{
    public override AssetValueKind Kind => AssetValueKind.Nullable;
}
