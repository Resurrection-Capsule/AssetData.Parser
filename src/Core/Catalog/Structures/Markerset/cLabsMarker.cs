namespace AssetData.Parser.Catalog;

public sealed class cLabsMarker : AssetCatalog
{
    protected override void Build()
    {
        Struct("cLabsMarker", 0xc8,
            Field("markerName", DataType.CharPtr, 0x0),
            Field("markerId", DataType.UInt32, 0x4),
            Field("nounDef", DataType.Asset, 0x8),
            Field("pos", DataType.Vector3, 0x1c),
            Field("rotDegrees", DataType.Vector3, 0x28),
            Field("scale", DataType.Float, 0x34),
            Field("dimensions", DataType.Vector3, 0x38),
            Field("visible", DataType.Bool, 0x44),
            Field("castShadows", DataType.Bool, 0x45),
            Field("backFaceShadows", DataType.Bool, 0x46),
            Field("onlyShadows", DataType.Bool, 0x47),
            Field("createWithCollision", DataType.Bool, 0x48),
            Field("debugDisplayKDTree", DataType.Bool, 0x49),
            Field("debugDisplayNormals", DataType.Bool, 0x4a),
            EnumField("navMeshSetting", "cLabsMarker.navMeshSetting", 0x4c),
            Array("assetOverrideId", DataType.UInt64, 0x10),
            IStruct("componentData", "SharedComponentData", 0x9c),
            NStruct("pointLightData", "cPointLightData", 0x5c),
            NStruct("spotLightData", "cSpotLightData", 0x60),
            NStruct("lineLightData", "cLineLightData", 0x64),
            NStruct("parallelLightData", "cParallelLightData", 0x68),
            NStruct("graphicsData", "cGraphicsData", 0x70),
            NStruct("animatedData", "cAnimatedData", 0x7c),
            NStruct("animatorData", "cAnimatorData", 0x78),
            NStruct("cameraComponentData", "cCameraComponentData", 0x74),
            NStruct("decalData", "cDecalData", 0x80),
            NStruct("waterData", "cWaterData", 0x84),
            NStruct("grassData", "cGrassData", 0x88),
            NStruct("mapCameraData", "cMapCameraData", 0x8c),
            NStruct("occluderData", "cOccluderData", 0x90),
            NStruct("splineCameraData", "cSplineCameraData", 0x94),
            NStruct("splineCameraNodeData", "cSplineCameraNodeBaseData", 0x98),
            NStruct("volumeDef", "cVolumeDef", 0x58),
            Field("ignoreOnXBox", DataType.Bool, 0x50),
            Field("ignoreOnMinSpec", DataType.Bool, 0x51),
            Field("ignoreOnPC", DataType.Bool, 0x52),
            Field("highSpecOnly", DataType.Bool, 0x53),
            Field("targetMarkerId", DataType.UInt32, 0x54)
        );
    }
}
