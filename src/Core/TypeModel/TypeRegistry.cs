namespace AssetData.Parser;

/// <summary>
/// Hash-keyed type table — the C# analogue of the client's <c>AssetTypeRegistry</c>
/// (<c>Register</c> 0x009f4b40, <c>FindTypeByHash</c> 0x009f4370). Replaces the legacy
/// string-keyed <c>Dictionary&lt;string, StructDefinition&gt;</c>: every lookup is by FNV hash,
/// so type identity matches the game.
/// </summary>
public sealed class TypeRegistry
{
    private readonly Dictionary<uint, TypeDescriptor> _byHash = new();
    private readonly Dictionary<string, uint> _nameToHash = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a type. First-wins on hash collision (mirrors the legacy merge).</summary>
    public void Register(TypeDescriptor descriptor)
    {
        if (_byHash.TryAdd(descriptor.TypeHash, descriptor))
            _nameToHash[descriptor.Name] = descriptor.TypeHash;
    }

    /// <summary>The core dispatch primitive: null means the hash is a sentinel or value-type.</summary>
    public TypeDescriptor? FindTypeByHash(uint typeHash) => _byHash.GetValueOrDefault(typeHash);

    /// <summary>Name lookup, retained only for the <c>GetFileType(extension)</c> entry point.</summary>
    public TypeDescriptor? FindTypeByName(string name)
        => _nameToHash.TryGetValue(name, out var hash) ? _byHash.GetValueOrDefault(hash) : null;

    public bool Contains(uint typeHash) => _byHash.ContainsKey(typeHash);
    public int Count => _byHash.Count;
    public IEnumerable<TypeDescriptor> Types => _byHash.Values;
}
