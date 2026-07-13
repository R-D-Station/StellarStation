using System.Text;
using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Контроллер одного блок-визуала: данные блока, cut-away/дверь, встроенный дебаг-дамп (без Update).</summary>
    public sealed class BlockView : MonoBehaviour
    {
        [Header("Блок")]
        public int X, Y, Z;
        [Tooltip("Основание solid-стека: уровень, «на котором стоит» (фильтр этажей cut-away).")]
        public int BaseY;
        public bool Hidden;
        public bool DoorOpen;

        [Header("Верх-грид (дебаг)")]
        public WallShape TopShape;
        public int TopSteps, TopCur, TopCurCorner, TopRotate;
        public byte TopCornerMask;

        [System.NonSerialized] public GameObject PrefabKey; // ключ пула (null = куб из общего пула)
        [System.NonSerialized] public Renderer[] Renderers; // кэш для cut-away гейта
        [System.NonSerialized] public Animator DoorAnim;    // якорь двери (иначе null)

        private static readonly int DoorOpenTrigger = Animator.StringToHash("Open");
        private static readonly int DoorCloseTrigger = Animator.StringToHash("Close");

        /// <summary>Привязать данные блока + кэшировать рендереры (звать при спавне/аренде из пула).</summary>
        public void Bind(int x, int y, int z, int baseY, GameObject prefabKey)
        {
            X = x; Y = y; Z = z; BaseY = baseY;
            PrefabKey = prefabKey;
            Renderers = GetComponentsInChildren<Renderer>(true);
            Hidden = false;
            DoorAnim = null;
            DoorOpen = false;
        }

        /// <summary>Гейт cut-away: тоггл всех рендереров блока. Возврат в пул восстанавливает enabled (ResetForPool).</summary>
        public void SetHidden(bool hidden)
        {
            if (hidden == Hidden || Renderers == null)
            {
                Hidden = hidden;
                return;
            }
            Hidden = hidden;
            for (int i = 0; i < Renderers.Length; i++)
                if (Renderers[i] != null)
                    Renderers[i].enabled = !hidden;
        }

        /// <summary>Пометить как якорь двери: аниматор для тоггла Open/Close без пересборки секции.</summary>
        public void SetDoor(Animator anim, bool open)
        {
            DoorAnim = anim;
            DoorOpen = open;
            if (open)
                PlayDoor(true); // спавн уже-открытой — показать открытое состояние
        }

        /// <summary>Проиграть открытие/закрытие двери (контракт: триггеры Open/Close на дочернем Animator).</summary>
        public void PlayDoor(bool open)
        {
            if (DoorAnim == null)
                return;
            DoorAnim.ResetTrigger(open ? DoorCloseTrigger : DoorOpenTrigger);
            DoorAnim.SetTrigger(open ? DoorOpenTrigger : DoorCloseTrigger);
            DoorOpen = open;
        }

        /// <summary>Записать параметры верх-грида (диагностика; из BlockAutoTex.TopParams).</summary>
        public void SetTopDebug(in BlockAutoTex.TopParams p, byte cornerMask)
        {
            TopShape = p.Shape; TopSteps = p.Steps; TopCur = p.Cur;
            TopCurCorner = p.CurCorner; TopRotate = p.Rotate; TopCornerMask = cornerMask;
        }

        /// <summary>Перед возвратом в пул: восстановить enabled (иначе блок «навсегда невидим» — урок RendererPool).</summary>
        public void ResetForPool()
        {
            if (Hidden && Renderers != null)
                for (int i = 0; i < Renderers.Length; i++)
                    if (Renderers[i] != null)
                        Renderers[i].enabled = true;
            Hidden = false;
        }

        // ── Дебаг-дамп рендер-состояния (было BlockRenderProbe.Report): рантайм-безопасно, без ShaderUtil ──
        private static readonly string[] TexProps = { "_TopMap", "_DownTex", "_SideTex", "_TopTex" };
        private static readonly string[] ScalarProps = { "_cur", "_cur_corner", "_rotate", "_count_x", "_count_y", "_alpha" };
        private static readonly string[] VecProps = { "_BoundsMin", "_BoundsSize" };

        /// <summary>Текстовый дамп шейдера/материала/bounds/MPB всех MeshRenderer этого блока.</summary>
        public string Report() => Dump(gameObject);

        /// <summary>То же для произвольного root (дамп из авторинга без экземпляра BlockView).</summary>
        public static string Dump(GameObject root)
        {
            var sb = new StringBuilder();
            sb.Append("[BlockView] ").Append(root != null ? root.name : "null").Append('\n');
            if (root == null)
                return sb.ToString();
            var mpb = new MaterialPropertyBlock();
            var rends = root.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length == 0)
                sb.Append("  нет MeshRenderer\n");
            foreach (var r in rends)
            {
                sb.Append("  • ").Append(r.gameObject.name).Append('\n');
                var mat = r.sharedMaterial;
                if (mat == null) { sb.Append("    material = NULL\n"); continue; }
                sb.Append("    shader   : ").Append(mat.shader != null ? mat.shader.name : "null").Append('\n');
                sb.Append("    material : ").Append(mat.name).Append('\n');
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var b = mf.sharedMesh.bounds;
                    sb.Append("    bounds   : min ").Append(b.min).Append(" size ").Append(b.size).Append('\n');
                }
                sb.Append("    MPB      : ").Append(r.HasPropertyBlock() ? "подан" : "нет").Append('\n');
                r.GetPropertyBlock(mpb);
                foreach (var p in TexProps)
                    if (mat.HasProperty(p))
                    {
                        bool ov = mpb.HasTexture(p);
                        Texture eff = ov ? mpb.GetTexture(p) : mat.GetTexture(p);
                        Line(sb, p, eff != null ? eff.name : "None", ov);
                    }
                foreach (var p in ScalarProps)
                    if (mat.HasProperty(p))
                    {
                        if (mpb.HasInt(p)) Line(sb, p, mpb.GetInt(p).ToString(), true);
                        else if (mpb.HasFloat(p)) Line(sb, p, mpb.GetFloat(p).ToString("0.###"), true);
                        else Line(sb, p, mat.GetFloat(p).ToString("0.###"), false);
                    }
                foreach (var p in VecProps)
                    if (mat.HasProperty(p))
                    {
                        bool ov = mpb.HasVector(p);
                        Vector4 eff = ov ? mpb.GetVector(p) : mat.GetVector(p);
                        Line(sb, p, eff.ToString("0.##"), ov);
                    }
            }
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string name, string value, bool fromMpb)
            => sb.Append("    ").Append(name).Append(" : ").Append(value)
                 .Append(fromMpb ? " (MPB)" : " (материал)").Append('\n');
    }
}
