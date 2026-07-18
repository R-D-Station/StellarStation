using UnityEngine;
using TMPro;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Render-компонент лейбла: 3D-TMP на префабе (не overlay — держит depth-окклюзию за стеной), по слотам резолвит текст из FloorSeed один раз при спавне.</summary>
    public sealed class BlockLabel : MonoBehaviour, IBlockComponent
    {
        /// <summary>Источник текста слота.</summary>
        public enum SlotField { Fixed, SeedName, SeedRank, SeedFloor }

        /// <summary>Один TMP-таргет на префабе + чем его заполнять.</summary>
        [System.Serializable]
        public struct LabelSlot
        {
            public TMP_Text Target;
            public SlotField Field;
            public string FixedText;
        }

        [SerializeField] private LabelSlot[] _slots = System.Array.Empty<LabelSlot>();

        private const float RenderEps = 0.001f;

        public void OnSpawn(in BlockContext ctx)
        {
            bool hasSeed = ctx.Grid.TryGetSeed(ctx.X, ctx.Y, ctx.Z, out var seed);
            for (int i = 0; i < _slots.Length; i++)
            {
                var t = _slots[i].Target;
                if (t != null)
                    t.text = Resolve(in _slots[i], hasSeed, in seed);
            }
        }

        public void OnVisibility(float alpha)
        {
            bool on = alpha > RenderEps;
            for (int i = 0; i < _slots.Length; i++)
            {
                var t = _slots[i].Target;
                if (t == null)
                    continue;
                t.alpha = alpha;
                if (t.enabled != on)
                    t.enabled = on;
            }
        }

        public void OnDespawn()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                var t = _slots[i].Target;
                if (t != null)
                    t.text = string.Empty;
            }
        }

        private static string Resolve(in LabelSlot slot, bool hasSeed, in FloorSeed seed)
        {
            switch (slot.Field)
            {
                case SlotField.SeedName: return hasSeed ? seed.Name : string.Empty;
                case SlotField.SeedRank: return hasSeed ? seed.Rank.ToString() : string.Empty;
                case SlotField.SeedFloor: return hasSeed ? seed.Floor.ToString() : string.Empty;
                default: return slot.FixedText ?? string.Empty;
            }
        }
    }
}
