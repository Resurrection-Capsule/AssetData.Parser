namespace AssetData.Parser.Catalog;

public sealed class cThumbnailCaptureParameters : AssetCatalog
{
    protected override void Build()
    {
        // Offsets verified against real on-disk data (PC_EL_Rogue.noun): the three
        // cameraRotation_* rows decode to a valid orthonormal rotation matrix, and
        // poseAnimID lands exactly at the end of the 108-byte (0x6C) struct.
        Struct("cThumbnailCaptureParameters", 108,
            Field("fovY", DataType.Float, 0x00),
            Field("nearPlane", DataType.Float, 0x04),
            Field("farPlane", DataType.Float, 0x08),
            Vector3("cameraPosition", 0x0C),
            Field("cameraScale", DataType.Float, 0x18),
            Vector3("cameraRotation_0", 0x1C),
            Vector3("cameraRotation_1", 0x28),
            Vector3("cameraRotation_2", 0x34),
            Field("mouseCameraDataValid", DataType.Bool, 0x40),
            Field("mouseCameraOffset", DataType.Float, 0x44),
            Vector3("mouseCameraSubjectPosition", 0x48),
            Field("mouseCameraTheta", DataType.Float, 0x54),
            Field("mouseCameraPhi", DataType.Float, 0x58),
            Field("mouseCameraRoll", DataType.Float, 0x5C),
            // 0x60 (float ~0.098) and 0x64 (0) unidentified — confirm names vs Ghidra ground truth
            Field("poseAnimID", DataType.UInt32, 0x68)
        );
    }
}
