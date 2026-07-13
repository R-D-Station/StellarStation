using UnityEngine;

namespace Client.Map
{
    /// <summary>Холдер на префабе блока: явный список мешей верха/боков для BlockAutoTex.FeedGrids (авторинг руками, без угадывания по шейдеру).</summary>
    [DisallowMultipleComponent]
    public sealed class BlockMaterials : MonoBehaviour
    {
        public MeshRenderer[] TopRenderers = System.Array.Empty<MeshRenderer>();
        public MeshRenderer[] WallRenderers = System.Array.Empty<MeshRenderer>();
    }
}
