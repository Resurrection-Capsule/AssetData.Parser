namespace AssetData.Parser.Catalog;

public sealed class labsPlayer : AssetCatalog
{
    protected override void Build()
    {
        Struct("labsPlayer", 0x14b8,
            Field("mbDataSetup", DataType.Bool, 0x0),
            Field("mCurrentDeckIndex", DataType.Int, 0xc),
            Field("mQueuedDeckIndex", DataType.Int, 0x10),
            IStruct("mCharacters", "labsCharacter", 0x18), // cLabsCharacter
            Field("mPlayerIndex", DataType.UInt8, 0x1278),
            Field("mTeam", DataType.UInt8, 0x12dc),
            Field("mPlayerOnlineId", DataType.UInt64, 0x1280),
            Field("mStatus", DataType.UInt32, 0x4),
            Field("mStatusProgress", DataType.Float, 0x8),
            Field("mCurrentCreatureId", DataType.ObjId, 0x12e0),
            Field("mEnergyPoints", DataType.Float, 0x12e4),
            Field("mbIsCharged", DataType.Bool, 0x12ec),
            Field("mDNA", DataType.Int, 0x12f0),
            IStruct("mCrystals", "labsCrystal", 0x133c),
            Field("mCrystalBonuses", DataType.Bool, 0x13cc),
            Field("mAvatarLevel", DataType.UInt32, 0x12fc),
            Field("mAvatarXP", DataType.Float, 0x12f8),
            Field("mChainProgression", DataType.UInt32, 0x1304),
            Field("mLockCamera", DataType.Bool, 0x1408),
            Field("mbLockedOverdrive", DataType.Bool, 0x1414),
            Field("mbLockedCrystals", DataType.Bool, 0x1415),
            Field("mLockedAbilityMin", DataType.UInt32, 0x1418),
            Field("mLockedDeckIndexMin", DataType.UInt32, 0x1420),
            Field("mDeckScore", DataType.UInt32, 0x140c)
        );
    }   
}
