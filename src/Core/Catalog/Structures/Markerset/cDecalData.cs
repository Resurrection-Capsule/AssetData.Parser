namespace AssetData.Parser.Catalog;

public sealed class cDecalData : AssetCatalog
{
    protected override void Build()
    {
        Struct("cDecalData", 0x10c,
            Field("size", DataType.Vector3, 0x0),
            Field("material", DataType.Char, 0xc, 0x40),
            Field("layer", DataType.Int, 0x4c),
            Field("diffuse", DataType.Char, 0x50, 0x40),
            Field("normal", DataType.Char, 0x90, 0x40),
            Field("diffuseTint", DataType.Vector3, 0xd0),
            Field("opacity", DataType.Float, 0xdc),
            Field("specularTint", DataType.Vector3, 0xe0),
            Field("opacityNormal", DataType.Float, 0xec),
            Field("tile", DataType.Vector2, 0xf0),
            Field("normalLevel", DataType.Float, 0xf8),
            Field("glowLevel", DataType.Float, 0xfc),
            Field("emissiveLevel", DataType.Float, 0x100),
            Field("specExponent", DataType.Float, 0x104),
            Field("enable", DataType.Bool, 0x108),
            Field("display_volume", DataType.Bool, 0x109)
        );
    }
}
