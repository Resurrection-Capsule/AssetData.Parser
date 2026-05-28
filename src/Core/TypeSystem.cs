using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetData.Parser;

/// <summary>
/// Darkspore asset data types. Hash values are FNV-1a (case-insensitive) from the actual game binary.
/// Verified by reverse engineering Darkspore.exe parser functions.
/// </summary>
public enum DataType : uint
{
    // ═══════════════════════════════════════════════════════════════════════
    // PRIMITIVES - Direct value in header (fixed size)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>bool - 1 byte stored as 4 bytes, compares "true" string</summary>
    Bool        = 0x68FE5F59,
    
    /// <summary>int - 4 bytes, parsed with atoi()</summary>
    Int         = 0x1F886EB0,
    
    /// <summary>int32_t - 4 bytes, same as Int but explicit 32-bit</summary>
    Int32       = 0x45D8F3DE,
    
    /// <summary>uint32_t - 4 bytes, supports "0x" hex prefix</summary>
    UInt32      = 0xE48967A3,
    
    /// <summary>Unknown type (0x2E10AAAE) - 4 bytes, always formatted as hex, same parser as UInt32</summary>
    HashId      = 0x2E10AAAE,
    
    /// <summary>tObjID - 4 bytes. Object Reference/Identifier.</summary>
    ObjId       = 0x1FB04A19,
    
    /// <summary>uint16_t - 2 bytes</summary>
    UInt16      = 0x7EF65E35,
    
    /// <summary>uint8_t - 1 byte</summary>
    UInt8       = 0xCDBE69CA,
    
    /// <summary>float - 4 bytes, parsed with atof()</summary>
    Float       = 0x4EDCD7A9,
    
    /// <summary>int64 - 8 bytes, parsed with _strtoi64()</summary>
    Int64       = 0x4C91FF43,
    
    /// <summary>uint64_t - 8 bytes, supports "0x" hex prefix</summary>
    UInt64      = 0x5DDA2052,
    
    /// <summary>enum - 4 bytes, uses enum lookup table</summary>
    Enum        = 0x096339A2,
    
    // ═══════════════════════════════════════════════════════════════════════
    // VECTORS - Inline fixed-size multi-component types
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>cSPVector2 - 8 bytes (2 floats), parsed as "%f %f"</summary>
    Vector2     = 0x9EB342FE,
    
    /// <summary>cSPVector3 - 12 bytes (3 floats), parsed as "%f %f %f"</summary>
    Vector3     = 0x9EB342FF,
    
    /// <summary>cSPVector4 - 16 bytes (4 floats), parsed as "%f %f %f %f"</summary>
    Vector4     = 0x9EB342F8,
    
    /// <summary>
    /// orientation/quaternion - 16 bytes (4 floats as XYZW)
    /// Accepts 3 floats (Euler angles in degrees) or 4 floats (quaternion WXYZ -> stored XYZW)
    /// </summary>
    Orientation = 0x75EC94F5,
    
    // ═══════════════════════════════════════════════════════════════════════
    // DYNAMIC - Indicator in header, data in blob if != 0
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>key - asset reference hash, data in blob</summary>
    Key         = 0x46842E82,
    
    /// <summary>char - inline char[] buffer (size in BufferSize) OR blob string</summary>
    Char        = 0xF6C8069D,
    
    /// <summary>char* - null-terminated string pointer, data in blob</summary>
    CharPtr     = 0x19E2690D,
    
    /// <summary>asset - asset path reference (checks for "[null]"), data in blob</summary>
    Asset       = 0x9C617503,

    /// <summary>cLocalizedAssetString - dual-string localized reference (8 bytes: 2 indicators + 2 blob strings)</summary>
    cLocalizedAssetString = 0x1D1FF116,

    // ═══════════════════════════════════════════════════════════════════════
    // CONTAINERS
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>array - [hasValue:4][count:4] in header, elements in blob</summary>
    Array       = 0x555CCDF4,
    
    /// <summary>nullable - [hasValue:4] in header, struct in blob if hasValue != 0</summary>
    Nullable    = 0x71AB5182,

    // ═══════════════════════════════════════════════════════════════════════
    // SPECIAL
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inline struct marker (DSL-only). The value 0x8 is NOT a wire type — the parser ignores
    /// it and dispatches inline structs by resolving FNV1a(ElementType) through the TypeRegistry
    /// (see AssetParser.ParseField). Kept only so the IStruct() DSL helper has a tag to emit.
    /// </summary>
    Struct      = 0x00000008,
}

public static class DataTypeExtensions
{
    /// <summary>Returns true if type is a fixed-size primitive (fits in header directly).</summary>
    public static bool IsPrimitive(this DataType type) => type switch
    {
        DataType.Bool or DataType.Int or DataType.Int32 or 
        DataType.UInt32 or DataType.HashId or DataType.ObjId or
        DataType.UInt16 or DataType.UInt8 or
        DataType.Float or DataType.Enum or 
        DataType.Int64 or DataType.UInt64 => true,
        _ => false
    };
    
    /// <summary>Returns true if type is a vector (multi-component inline).</summary>
    public static bool IsVector(this DataType type) => type switch
    {
        DataType.Vector2 or DataType.Vector3 or 
        DataType.Vector4 or DataType.Orientation => true,
        _ => false
    };
    
    /// <summary>Returns true if type has indicator in header and data in blob.</summary>
    public static bool IsDynamic(this DataType type) => type switch
    {
        DataType.Key or DataType.Char or DataType.CharPtr or
        DataType.Asset or DataType.cLocalizedAssetString => true,
        _ => false
    };
    
    /// <summary>Returns the size in bytes for primitive and vector types.</summary>
    public static int GetSize(this DataType type) => type switch
    {
        DataType.Bool => 4,      // Stored as 4-byte int
        DataType.Int => 4,
        DataType.Int32 => 4,
        DataType.UInt32 => 4,
        DataType.HashId => 4,
        DataType.ObjId  => 4,
        DataType.UInt16 => 2,
        DataType.UInt8 => 1,
        DataType.Float => 4,
        DataType.Enum => 4,
        DataType.Int64 => 8,
        DataType.UInt64 => 8,
        DataType.Vector2 => 8,
        DataType.Vector3 => 12,
        DataType.Vector4 => 16,
        DataType.Orientation => 16,
        DataType.cLocalizedAssetString => 8,  // Two 4-byte indicators
        _ => 4  // Default indicator size for dynamic types
    };
    
    /// <summary>Returns true if this type should be displayed in hex format.</summary>
    public static bool PreferHexFormat(this DataType type) => type switch
    {
        DataType.HashId or DataType.ObjId  or DataType.Key => true,
        _ => false
    };
}

/// <summary>
/// Field definition within a struct.
/// </summary>
public sealed record FieldDefinition(
    string Name,
    DataType Type,
    int Offset,
    string? ElementType = null,
    int CountOffset = 4,
    int BufferSize = 0,
    string? EnumType = null
)
{
    public bool IsStructArray => Type == DataType.Array && ElementType != null && 
                                  !Enum.TryParse<DataType>(ElementType, true, out _);
}

/// <summary>
/// Struct definition with fields.
/// </summary>
public sealed class StructDefinition
{
    public string Name { get; }
    public int Size { get; }
    public IReadOnlyList<FieldDefinition> Fields { get; }
    
    public StructDefinition(string name, int size, params FieldDefinition[] fields)
    {
        Name = name;
        Size = size;
        Fields = fields.ToList();  // CRITICAL: Keep definition order! Blob data follows this order, NOT offset order.
    }
}

/// <summary>
/// File type registration info.
/// </summary>
public sealed record FileTypeInfo(string Extension, string RootStruct, int HeaderSize);

/// <summary>
/// Enum definition with value-to-name mappings.
/// </summary>
public sealed class EnumDefinition
{
    public string Name { get; }
    private readonly Dictionary<uint, string> _values = new();
    private readonly Dictionary<string, uint> _names = new(StringComparer.OrdinalIgnoreCase);
    
    public IReadOnlyDictionary<uint, string> Values => _values;
    
    public EnumDefinition(string name) => Name = name;
    
    public void Add(string name, uint value)
    {
        _values[value] = name;
        _names[name] = value;
    }
    
    public string? GetName(uint value) => _values.GetValueOrDefault(value);
    public uint? GetValue(string name) => _names.TryGetValue(name, out var v) ? v : null;
    
    public string Format(uint value)
    {
        if (_values.TryGetValue(value, out var name))
            return $"{name}, 0x{value:X8}";
        return $"0x{value:X8}";
    }
}

/// <summary>
/// Base class for asset type catalogs.
/// </summary>
public abstract class AssetCatalog
{
    private readonly Dictionary<string, StructDefinition> _structs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Structs registered by this catalog's <see cref="Build"/>. Consumed by AssetParser's
    /// explicit merge (replaces the old private-field reflection).</summary>
    internal IReadOnlyDictionary<string, StructDefinition> Structs => _structs;

    /// <summary>Enums registered by this catalog's <see cref="Build"/>.</summary>
    internal IReadOnlyDictionary<string, EnumDefinition> Enums => _enums;

    protected AssetCatalog() => Build();
    
    protected abstract void Build();
    
    // Struct registration
    protected void Struct(string name, int size, params FieldDefinition[] fields)
        => _structs[name] = new StructDefinition(name, size, fields);
    
    // Enum registration
    protected EnumBuilder Enum(string name)
    {
        var def = new EnumDefinition(name);
        _enums[name] = def;
        return new EnumBuilder(def);
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // Field definition helpers - Primitives
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generic field definition helper with optional buffer size for Char types.
    /// </summary>
    /// <param name="name">Field name</param>
    /// <param name="type">Data type</param>
    /// <param name="offset">Byte offset in struct</param>
    /// <param name="bufferSize">Optional buffer size for inline Char fields (default: 0 = dynamic blob string)</param>
    protected static FieldDefinition Field(string name, DataType type, int offset, int bufferSize = 0)
        => new(name, type, offset, BufferSize: bufferSize);
    
    protected static FieldDefinition Bool(string name, int offset)
        => new(name, DataType.Bool, offset);
    
    protected static FieldDefinition Int(string name, int offset)
        => new(name, DataType.Int, offset);
    
    protected static FieldDefinition UInt32(string name, int offset)
        => new(name, DataType.UInt32, offset);
    
    protected static FieldDefinition HashId(string name, int offset)
        => new(name, DataType.HashId, offset);
    
    protected static FieldDefinition ObjId(string name, int offset)
        => new(name, DataType.ObjId , offset);
    
    protected static FieldDefinition UInt16(string name, int offset)
        => new(name, DataType.UInt16, offset);
    
    protected static FieldDefinition UInt8(string name, int offset)
        => new(name, DataType.UInt8, offset);
    
    protected static FieldDefinition Float(string name, int offset)
        => new(name, DataType.Float, offset);
    
    protected static FieldDefinition Int64(string name, int offset)
        => new(name, DataType.Int64, offset);
    
    protected static FieldDefinition UInt64(string name, int offset)
        => new(name, DataType.UInt64, offset);
    
    // ═══════════════════════════════════════════════════════════════════════
    // Field definition helpers - Vectors
    // ═══════════════════════════════════════════════════════════════════════
    
    protected static FieldDefinition Vector2(string name, int offset)
        => new(name, DataType.Vector2, offset);
    
    protected static FieldDefinition Vector3(string name, int offset)
        => new(name, DataType.Vector3, offset);
    
    protected static FieldDefinition Vector4(string name, int offset)
        => new(name, DataType.Vector4, offset);
    
    protected static FieldDefinition Orientation(string name, int offset)
        => new(name, DataType.Orientation, offset);
    
    // ═══════════════════════════════════════════════════════════════════════
    // Field definition helpers - Dynamic & Containers
    // ═══════════════════════════════════════════════════════════════════════
    
    protected static FieldDefinition EnumField(string name, string enumType, int offset)
        => new(name, DataType.Enum, offset, EnumType: enumType);
    
    protected static FieldDefinition Key(string name, int offset)
        => new(name, DataType.Key, offset);
    
    protected static FieldDefinition CharBuffer(string name, int offset, int bufferSize)
        => new(name, DataType.Char, offset, BufferSize: bufferSize);
    
    protected static FieldDefinition CharPtr(string name, int offset)
        => new(name, DataType.CharPtr, offset);
    
    protected static FieldDefinition Asset(string name, int offset)
        => new(name, DataType.Asset, offset);

    protected static FieldDefinition LocalizedAssetString(string name, int offset)
        => new(name, DataType.cLocalizedAssetString, offset);

    protected static FieldDefinition Array(string name, DataType elementType, int offset, int countOffset = 4)
        => new(name, DataType.Array, offset, elementType.ToString(), countOffset);

    protected static FieldDefinition ArrayEnum(string name, string enumType, int offset, int countOffset = 4)
        => new(name, DataType.Array, offset, DataType.Enum.ToString(), countOffset, EnumType: enumType);

    protected static FieldDefinition ArrayStruct(string name, string structType, int offset, int countOffset = 4)
        => new(name, DataType.Array, offset, structType, countOffset);
    
    protected static FieldDefinition IStruct(string name, string structType, int offset)
        => new(name, DataType.Struct, offset, structType);
    
    protected static FieldDefinition NStruct(string name, string structType, int offset)
        => new(name, DataType.Nullable, offset, structType);
    
    // Getters
    public StructDefinition? GetStruct(string name) => _structs.GetValueOrDefault(name);
    public EnumDefinition? GetEnum(string name) => _enums.GetValueOrDefault(name);
    
    public FileTypeInfo? GetFileType(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        var structDef = _structs.GetValueOrDefault(ext);
        if (structDef == null) return null;
        return new FileTypeInfo(ext, structDef.Name, structDef.Size);
    }
    
    public IEnumerable<string> StructNames => _structs.Keys;
    public IEnumerable<string> EnumNames => _enums.Keys;
}

/// <summary>
/// Fluent builder for enum definitions.
/// </summary>
public sealed class EnumBuilder
{
    private readonly EnumDefinition _def;
    
    internal EnumBuilder(EnumDefinition def) => _def = def;
    
    public EnumBuilder Value(string name, uint value)
    {
        _def.Add(name, value);
        return this;
    }
    
    public EnumBuilder Hash(string name)
    {
        _def.Add(name, FnvHash(name));
        return this;
    }
    
    private static uint FnvHash(string s)
    {
        uint hash = 0x811C9DC5;
        foreach (char c in s.ToLowerInvariant())
        {
            hash *= 0x1000193;
            hash ^= (byte)c;
        }
        return hash;
    }
}