namespace AssetData.Parser.Catalog;

public sealed class DirectorBucket : AssetCatalog
{
    protected override void Build()
    {
        Struct("DirectorBucket", 0x10,
            Field("numMinions", DataType.Int, 0x0),
            Field("numSpecials", DataType.Int, 0x4),
            Field("difficulty", DataType.Int, 0x8),
            Field("chance", DataType.Float, 0xc)
        );
    }
}