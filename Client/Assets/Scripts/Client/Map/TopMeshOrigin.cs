using UnityEngine;

namespace Client.Map
{
    /// <summary>Исходная поза верх-меша префаба — база для абсолютного доворота Main-калибровки (без накопления из пула).</summary>
    public sealed class TopMeshOrigin : MonoBehaviour
    {
        public Vector3 LocalPos;
        public Quaternion LocalRot;
        public bool Captured;
    }
}
