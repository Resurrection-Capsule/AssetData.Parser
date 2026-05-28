namespace AssetData.Parser.Catalog;

public sealed class cAssetProperty : AssetCatalog
{
    protected override void Build()
    {
        // Layout per Ghidra ground truth (redesign §4.3): key@0x00, name char[80]@0x04,
        // type discriminator@0x54, value char[80]@0x58. Size 0xBC (188).
        Struct("cAssetProperty", 0xbc,
            Field("key", DataType.UInt32, 0x0),          // TODO: add DataType.GUID
            CharBuffer("name", 0x4, 0x50),               // inline char[80]
            Field("type", DataType.UInt32, 0x54),        // variant discriminator
            CharBuffer("value", 0x58, 0x50)              // inline char[80], interpreted per `type`
        );
    }
}
