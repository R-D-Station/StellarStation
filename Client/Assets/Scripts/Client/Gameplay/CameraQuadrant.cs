using Shared.Messages.Core;

namespace Client.Gameplay.Camera
{
    public sealed class CameraQuadrant
    {
        public int Value { get; private set; }

        public float Yaw => CameraMath.YawDegrees(Value);

        public void Next() => Value = CameraMath.NextQuadrant(Value);

        public void Reset() => Value = 0;

        public IntentDirection Rotate(IntentDirection dir) => CameraMath.RotateIntent(dir, Value);

        public void RotateOffset(float x, float y, float z, out float rx, out float ry, out float rz)
            => CameraMath.RotateOffset(x, y, z, Value, out rx, out ry, out rz);
    }
}
