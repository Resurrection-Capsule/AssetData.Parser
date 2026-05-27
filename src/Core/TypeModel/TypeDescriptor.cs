namespace AssetData.Parser;

/// <summary>
/// A single field within a <see cref="TypeDescriptor"/>, modelled on the client's 0x60-byte
/// field descriptor. The type identity is a hash (<see cref="TypeHash"/>), not a baked enum —
/// dispatch is <c>FindTypeByHash(TypeHash)</c>, exactly like the game.
/// </summary>
/// <param name="Name">Field name (also hashed into <see cref="NameHash"/>).</param>
/// <param name="TypeHash">FNV hash of the field's type. Resolves to a registered struct (recurse)
/// or matches a <see cref="WireHash"/> sentinel / value-type.</param>
/// <param name="Offset">Byte offset of the field within its owning struct's header region.</param>
/// <param name="ElementHash">For <c>Array</c>/<c>Nullable</c>: the hash of the element/inner type
/// (mirrors <c>field+0x28</c>). 0 otherwise.</param>
/// <param name="CountOffset">For arrays: offset of the count field relative to <see cref="Offset"/>.</param>
/// <param name="BufferSize">For <c>Char</c>: inline buffer size; 0 means dynamic blob string.</param>
/// <param name="EnumType">Resolved enum table name for <c>Enum</c> fields, if any.</param>
public sealed record FieldDescriptor(
    string Name,
    uint TypeHash,
    int Offset,
    uint ElementHash = 0,
    int CountOffset = 4,
    int BufferSize = 0,
    string? EnumType = null)
{
    /// <summary>FNV hash of <see cref="Name"/> (the client keys fields by name hash).</summary>
    public uint NameHash => WireHash.Fnv1a(Name);
}

/// <summary>
/// A registered type, modelled on the client's 0xA78 type record. Keyed in the
/// <see cref="TypeRegistry"/> by <see cref="TypeHash"/> = FNV of <see cref="Name"/>.
/// </summary>
public sealed class TypeDescriptor
{
    public string Name { get; }
    public uint TypeHash { get; }
    public int Size { get; }
    public IReadOnlyList<FieldDescriptor> Fields { get; }

    public TypeDescriptor(string name, int size, IReadOnlyList<FieldDescriptor> fields)
    {
        Name = name;
        Size = size;
        Fields = fields;
        TypeHash = WireHash.Fnv1a(name);
    }
}
