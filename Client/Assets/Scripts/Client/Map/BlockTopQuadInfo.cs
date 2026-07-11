using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Диагностика верх-квада (авторинг, при видимых визуалах): значения, поданные в шейдер TileReader.</summary>
    public sealed class BlockTopQuadInfo : MonoBehaviour
    {
        public WallShape Shape;
        public int Steps;
        public byte CornerMask;
        public int Cur;
        public int CurCorner;
        public int Rotate;
    }
}
