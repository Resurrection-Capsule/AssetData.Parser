namespace AssetData.Parser.Catalog;

public sealed class cAICondition : AssetCatalog
{
    protected override void Build()
    {
        Struct("cAICondition", 0x78,
            Field("conditionType", DataType.Int, 0x0),
            Field("namespace", DataType.Char, 0x4, 0x50),
            Field("name", DataType.Char, 0x54, 0x10),
            // Was the fabricated AssetPropertyVector; on disk this is a standard
            // array<cAssetProperty> ([hasValue@0x68][count@0x6C], elements 0xBC each).
            ArrayStruct("properties", "cAssetProperty", 0x68)
        );
    }
}
