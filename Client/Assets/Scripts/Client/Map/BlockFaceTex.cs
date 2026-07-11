using UnityEngine;

namespace Client.Map
{
    /// <summary>Подкачка граней мешей на шейдере Station/TileFaceSprites — контракт шейдера: текстуры и
    /// габариты подаются per-инстанс через MPB (сам .mat пуст, дефолтные габариты плющат UV в точку).
    /// Габариты — из mesh.bounds; текстуры — из BlockDefinition. Общее для рендера и авторинга.</summary>
    public static class BlockFaceTex
    {
        private static readonly int SideTexId = Shader.PropertyToID("_SideTex");
        private static readonly int TopTexId = Shader.PropertyToID("_TopTex");
        private static readonly int BoundsMinId = Shader.PropertyToID("_BoundsMin");
        private static readonly int BoundsSizeId = Shader.PropertyToID("_BoundsSize");

        private static Shader _shader;
        private static MaterialPropertyBlock _mpb;

        /// <summary>Заполнить MPB всех TileFaceSprites-рендереров инстанса (звать после Instantiate/аренды из пула).</summary>
        public static void Feed(GameObject instance, BlockDefinition def)
        {
            if (_shader == null)
                _shader = Shader.Find("Station/TileFaceSprites");
            if (_shader == null)
                return;

            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                var mat = r.sharedMaterial;
                if (mat == null || mat.shader != _shader)
                    continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;

                _mpb ??= new MaterialPropertyBlock();
                _mpb.Clear();
                var b = mf.sharedMesh.bounds;
                _mpb.SetVector(BoundsMinId, b.min);
                _mpb.SetVector(BoundsSizeId, b.size);
                if (def != null && def.FaceSideTex != null)
                    _mpb.SetTexture(SideTexId, def.FaceSideTex);
                if (def != null && def.FaceTopTex != null)
                    _mpb.SetTexture(TopTexId, def.FaceTopTex);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
