namespace AssetData.Parser;

/// <summary>
/// Wire-level type identities: the FNV hashes of the client's canonical type names.
/// In <c>AssetParser::DeserializeObject</c> (0x009cd2c0) every field dispatches on a single
/// decision — its type hash either resolves to a registered struct (recurse) or is one of the
/// sentinel / value-type hashes below (handled inline).
///
/// These values mirror the legacy <see cref="DataType"/> enum so the two stay byte-compatible
/// during migration. Verified: <see cref="Fnv1a"/> of each canonical name reproduces the legacy
/// value for every type EXCEPT <see cref="Orientation"/> — the field named "orientation" is
/// actually the wire type <c>cSPQuaternion</c> (Fnv1a("cSPQuaternion") == 0x75EC94F5).
/// </summary>
public static class WireHash
{
    /// <summary>
    /// The single FNV hash used across the parser (replaces the 3 copies in DbpfReader,
    /// EnumBuilder, and the baked DataType values). Case-insensitive; matches the client and
    /// the legacy DataType values bit-for-bit. NOTE: operation order is multiply-then-xor
    /// (the client's variant) — do not "correct" it to canonical FNV-1a or every hash shifts.
    /// </summary>
    public static uint Fnv1a(string s)
    {
        uint hash = 0x811C9DC5;
        foreach (char c in s.ToLowerInvariant())
        {
            hash *= 0x1000193;
            hash ^= (byte)c;
        }
        return hash;
    }

    // ── Value types (raw bytes copied inline) ────────────────────────────────
    public const uint Bool        = 0x68FE5F59;
    public const uint Int         = 0x1F886EB0;
    public const uint Int32       = 0x45D8F3DE;
    public const uint UInt32      = 0xE48967A3;
    public const uint HashId      = 0x2E10AAAE;
    public const uint ObjId       = 0x1FB04A19;
    public const uint UInt16      = 0x7EF65E35;
    public const uint UInt8       = 0xCDBE69CA;
    public const uint Float       = 0x4EDCD7A9;
    public const uint Int64       = 0x4C91FF43;
    public const uint UInt64      = 0x5DDA2052;
    public const uint Vector2     = 0x9EB342FE;  // cSPVector2
    public const uint Vector3     = 0x9EB342FF;  // cSPVector3
    public const uint Vector4     = 0x9EB342F8;  // cSPVector4
    public const uint Orientation = 0x75EC94F5;  // cSPQuaternion (NOT Fnv1a("orientation"))

    // ── Sentinels (indicator in header, data/handling in blob) ───────────────
    public const uint Enum                 = 0x096339A2;
    public const uint Key                  = 0x46842E82;
    public const uint Char                 = 0xF6C8069D;
    public const uint CharPtr              = 0x19E2690D;  // char*
    public const uint Asset                = 0x9C617503;
    public const uint LocalizedAssetString = 0x1D1FF116;  // cLocalizedAssetString
    public const uint Array                = 0x555CCDF4;
    public const uint Nullable             = 0x71AB5182;
}
