namespace AssetData.Parser.Catalog;

public sealed class Condition : AssetCatalog
{
    protected override void Build()
    {
        Struct("Condition", 16,
            Field("condition", DataType.Key, 0xc),
            IStruct("conditionProps", "cAssetPropertyList", 0x10),
            Field("activateOnce", DataType.Bool, 0x18),
            Field("checkOnSequenceEnd", DataType.Bool, 0x19),
            Field("activateTime", DataType.Float, 0x1c),
            Field("checkTimeInterval", DataType.Float, 0x20)
        );
    }
}
