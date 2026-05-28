namespace AssetData.Parser.Catalog;

public sealed class EventListenerData : AssetCatalog
{
    protected override void Build()
    {
        Struct("EventListenerData", 40,
            Field("event", DataType.Key, 0xc),
            Field("callback", DataType.Key, 0x1c),
            Field("luaCallback", DataType.CharPtr, 0x24)
        );
    }
}