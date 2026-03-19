using System.Numerics;

namespace AssetData.Parser;

public static class AssetNodeExtensions
{
    public static uint AsUInt32(this AssetNode? node) => node is NumberNode n ? (uint)n.Value : 0u;

    public static int AsInt32(this AssetNode? node) => node is NumberNode n ? (int)n.Value : 0;

    public static ulong AsUInt64(this AssetNode? node) => node is NumberNode n ? (ulong)n.Value : 0ul;

    public static long AsInt64(this AssetNode? node) => node is NumberNode n ? (long)n.Value : 0L;

    public static float AsFloat(this AssetNode? node) => node is NumberNode n ? (float)n.Value : 0f;

    public static bool AsBool(this AssetNode? node) => node is BooleanNode b && b.Value;

    public static string AsString(this AssetNode? node) => node switch
    {
        StringNode s => s.Value,
        LocalizedStringNode l => l.PrimaryValue,
        _ => string.Empty
    };

    public static uint AsEnum(this AssetNode? node) => node is EnumNode e ? e.RawValue : 0u;

    public static string AsEnumName(this AssetNode? node) => node is EnumNode e ? e.ResolvedName : string.Empty;

    public static Vector3 AsVector3(this AssetNode? node) => node is VectorNode v
        ? new Vector3(v.X, v.Y, v.Z)
        : Vector3.Zero;

    public static Vector4 AsVector4(this AssetNode? node) => node is VectorNode v
        ? new Vector4(v.X, v.Y, v.Z, v.W)
        : Vector4.Zero;

    public static Quaternion AsQuaternion(this AssetNode? node) => node is VectorNode v
        ? new Quaternion(v.X, v.Y, v.Z, v.W)
        : Quaternion.Identity;
}
