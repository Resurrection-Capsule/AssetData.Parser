namespace AssetData.Parser.Catalog;

public sealed class cSpotLightData : AssetCatalog
{
    protected override void Build()
    {
        Struct("cSpotLightData", 0x60,
            Field("diffuse_color", DataType.Vector3, 0x0),
            Field("diffuse_lamp_power", DataType.Float, 0xc),
            Field("specular_lamp_power", DataType.Float, 0x10),
            Field("inner_radius", DataType.Float, 0x14),
            Field("radius", DataType.Float, 0x18),
            Field("falloff", DataType.Float, 0x1c),
            Field("length", DataType.Float, 0x20),
            Field("shadow_bias", DataType.Float, 0x24),
            Field("gobo", DataType.Key, 0x34),
            Field("frames", DataType.Int, 0x38),
            Field("has_spec", DataType.Bool, 0x3c),
            Field("enable", DataType.Bool, 0x3d),
            Field("shadow_caster", DataType.Bool, 0x3e),
            Field("show_frustum", DataType.Bool, 0x3f),
            Field("show_volume", DataType.Bool, 0x40),
            Field("wind_blown", DataType.Bool, 0x42),
            Field("wind_pivot_pos", DataType.Vector3, 0x44),
            Field("wind_pivot_rot", DataType.Vector3, 0x50),
            Field("wind_flex", DataType.Float, 0x5c)
        );
    }
}
