namespace AssetData.Parser.Catalog;

public sealed class cAnimatedData : AssetCatalog
{
    protected override void Build()
    {
        Struct("cAnimatedData", 0x4,
            Field("animator", DataType.CharPtr, 0x0)
        );
    }
}
