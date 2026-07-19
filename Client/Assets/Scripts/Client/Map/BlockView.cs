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
        [Tooltip("Нижняя грань открыта (под блоком не-solid) — кандидат-потолок для cut-away.")]
        public bool BottomOpen;
        public bool Hidden;
        [Tooltip("Текущая cut-альфа [0..1]: 0 = enabled off, дробь = MPB _alpha.")]
        public float Alpha = 1f;
        [Tooltip("Раскрытие перекрытого верха [0..1]: 1 − альфа покрывающего блока.")]
        public float TopUncover;
        [Tooltip("Верх перекрыт блоком сверху (бейк спавна) — кандидат на раскрытие.")]
        public bool TopCovered;
        public int TopCellY;
        public bool DoorOpen;

        [Header("Верх-грид (дебаг)")]
        public WallShape TopShape;
        public int TopSteps, TopCur, TopCurCorner, TopRotate;
        public byte TopCornerMask;

        [System.NonSerialized] public GameObject PrefabKey; // ключ пула (null = куб из общего пула)
        [System.NonSerialized] public Renderer[] Renderers; // кэш для cut-away гейта
        [System.NonSerialized] public Animator DoorAnim;    // якорь двери (иначе null)
        [System.NonSerialized] private IBlockComponent[] _components; // render-компоненты префаба (кэш по GO, стабилен по пулу)

        private static readonly int DoorOpenTrigger = Animator.StringToHash("Open");
        private static readonly int DoorCloseTrigger = Animator.StringToHash("Close");

        private const float HideEps = 0.001f;
        private const float ShowEps = 0.999f;

        private static readonly int AlphaId = Shader.PropertyToID("_alpha");
        private static MaterialPropertyBlock _alphaMpb;

        [System.NonSerialized] private float[] _bakedAlpha;
        [System.NonSerialized] private bool _bakedCaptured;
        [System.NonSerialized] private bool _alphaOverridden;

        /// <summary>Привязать данные блока + кэшировать рендереры (звать при спавне/аренде из пула).</summary>
        public void Bind(int x, int y, int z, int baseY, GameObject prefabKey)
        {
            X = x; Y = y; Z = z; BaseY = baseY;
            BottomOpen = false;
            TopCovered = false;
            TopCellY = y + 1;
            PrefabKey = prefabKey;
            Renderers = GetComponentsInChildren<Renderer>(true);
            _components ??= GetComponentsInChildren<IBlockComponent>(true);
            Hidden = false;
            Alpha = 1f;
            TopUncover = 0f;
            _bakedCaptured = false;
            _alphaOverridden = false;
            DoorAnim = null;
            DoorOpen = false;
        }

        /// <summary>Разбудить render-компоненты префаба (после Bind + BottomOpen/TopCovered).</summary>
        public void SpawnComponents(in BlockContext ctx)
        {
            if (_components == null)
                return;
            for (int i = 0; i < _components.Length; i++)
                _components[i].OnSpawn(in ctx);
        }

        /// <summary>Симметрично SpawnComponents: при возврате в пул (dismiss лейблов и т.п.).</summary>
        public void DespawnComponents()
        {
            if (_components == null)
                return;
            for (int i = 0; i < _components.Length; i++)
                _components[i].OnDespawn();
        }

        /// <summary>Совместимость: бинарный гейт через альфа-конвейер (0 или 1).</summary>
        public void SetHidden(bool hidden) => SetAlpha(hidden ? 0f : 1f);

        /// <summary>Альфа cut-away без раскрытия перекрытого верха.</summary>
        public void SetAlpha(float a) => SetAlpha(a, 0f);

        /// <summary>Cut-альфа блока: 0 = enabled off, 1 = откат к запечённой базе, дробь = MPB _alpha = база×a (topUncover — доп. раскрытие перекрытого верха).</summary>
        public void SetAlpha(float a, float topUncover)
        {
            a = Mathf.Clamp01(a);
            ApplyAlpha(a, topUncover);
            if (_components != null)
                for (int i = 0; i < _components.Length; i++)
                    _components[i].OnVisibility(a);
        }

        private void ApplyAlpha(float a, float topUncover)
        {
            topUncover = Mathf.Clamp01(topUncover);
            if (Renderers == null || (Mathf.Abs(a - Alpha) < 0.0005f && Mathf.Abs(topUncover - TopUncover) < 0.0005f))
            {
                Alpha = a;
                TopUncover = topUncover;
                Hidden = a <= HideEps;
                return;
            }
            bool wasHidden = Alpha <= HideEps;
            Alpha = a;
            TopUncover = topUncover;

            if (a <= HideEps)
            {
                Hidden = true;
                for (int i = 0; i < Renderers.Length; i++)
                    if (Renderers[i] != null)
                        Renderers[i].enabled = false;
                return;
            }

            Hidden = false;
            if (wasHidden)
                for (int i = 0; i < Renderers.Length; i++)
                    if (Renderers[i] != null)
                        Renderers[i].enabled = true;

            if (a >= ShowEps && topUncover <= HideEps)
                RestoreBakedAlpha();
            else
                ApplyFractionalAlpha(a, topUncover);
        }

        private void ApplyFractionalAlpha(float a, float topUncover)
        {
            _alphaMpb ??= new MaterialPropertyBlock();
            if (_bakedAlpha == null || _bakedAlpha.Length < Renderers.Length)
                _bakedAlpha = new float[Renderers.Length];
            for (int i = 0; i < Renderers.Length; i++)
            {
                var r = Renderers[i];
                var mat = r != null ? r.sharedMaterial : null;
                if (mat == null || !mat.HasProperty(AlphaId))
                    continue;
                _alphaMpb.Clear(); // без Clear MPB тянет чужие свойства (_TopMap/_cur от FeedTopMesh) — «белая стена»
                r.GetPropertyBlock(_alphaMpb);
                if (!_bakedCaptured)
                    _bakedAlpha[i] = _alphaMpb.HasFloat(AlphaId) ? _alphaMpb.GetFloat(AlphaId) : mat.GetFloat(AlphaId);
                float baseA = _bakedAlpha[i];
                if (topUncover > baseA) // раскрытие перекрытого верха проявляет ДАЖЕ базу 0 (полностью скрытая грань)
                    baseA = topUncover;
                _alphaMpb.SetFloat(AlphaId, baseA * a);
                r.SetPropertyBlock(_alphaMpb);
            }
            _bakedCaptured = true;
            _alphaOverridden = true;
        }

        private void RestoreBakedAlpha()
        {
            if (!_alphaOverridden || Renderers == null)
                return;
            _alphaMpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < Renderers.Length; i++)
            {
                var r = Renderers[i];
                var mat = r != null ? r.sharedMaterial : null;
                if (mat == null || !mat.HasProperty(AlphaId))
                    continue;
                _alphaMpb.Clear();
                r.GetPropertyBlock(_alphaMpb);
                _alphaMpb.SetFloat(AlphaId, _bakedAlpha != null && i < _bakedAlpha.Length ? _bakedAlpha[i] : 1f);
                r.SetPropertyBlock(_alphaMpb);
            }
            _alphaOverridden = false;
            _bakedCaptured = false;
        }

        [ContextMenu("Отладка: alpha 0.5")] private void DebugAlphaHalf() => SetAlpha(0.5f);
        [ContextMenu("Отладка: alpha 1")] private void DebugAlphaFull() => SetAlpha(1f);

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

        /// <summary>Сброс к «полностью видим» перед возвратом в пул — анти-leak enabled/MPB-оверрайда (звать до SetActive(false)).</summary>
        public void ResetForPool()
        {
            if (Hidden && Renderers != null)
                for (int i = 0; i < Renderers.Length; i++)
                    if (Renderers[i] != null)
                        Renderers[i].enabled = true;
            RestoreBakedAlpha();
            Hidden = false;
            Alpha = 1f;
            TopUncover = 0f;
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
