#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>Инспектор <see cref="BlockView"/>: поля блока + живой перечень свойств шейдера каждого MeshRenderer.</summary>
    [CustomEditor(typeof(BlockView))]
    public sealed class BlockViewEditor : UnityEditor.Editor
    {
        private bool _showAll;
        private static MaterialPropertyBlock _mpb;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); // X/Y/Z/BaseY/Hidden/DoorOpen + верх-грид дебаг-поля

            var view = (BlockView)target;
            EditorGUILayout.Space(6);
            _showAll = EditorGUILayout.ToggleLeft("Показать ВСЕ свойства шейдера (не только текстуры/оверрайды)", _showAll);

            var renderers = view.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                EditorGUILayout.HelpBox("Нет MeshRenderer (маркер/пустой?).", MessageType.Info);
                return;
            }
            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
                DrawRenderer(r);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Скопировать отчёт в консоль"))
                Debug.Log(view.Report(), view.gameObject);
        }

        private void DrawRenderer(MeshRenderer r)
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(r.gameObject.name, EditorStyles.boldLabel);
                var mat = r.sharedMaterial;
                if (mat == null)
                {
                    EditorGUILayout.HelpBox("sharedMaterial = null → дефолт/магента.", MessageType.Error);
                    return;
                }
                var sh = mat.shader;
                EditorGUILayout.LabelField("Шейдер", sh != null ? sh.name : "— (null)");
                EditorGUILayout.LabelField("Материал", mat.name);

                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mb = mf.sharedMesh.bounds;
                    EditorGUILayout.LabelField("Mesh bounds", $"min {V(mb.min)}  size {V(mb.size)}");
                }
                EditorGUILayout.LabelField("MPB подан", r.HasPropertyBlock() ? "да" : "нет");
                r.GetPropertyBlock(_mpb);
                if (sh != null)
                    DrawShaderProps(sh, mat);
            }
        }

        private void DrawShaderProps(Shader sh, Material mat)
        {
            int n = ShaderUtil.GetPropertyCount(sh);
            for (int i = 0; i < n; i++)
            {
                string name = ShaderUtil.GetPropertyName(sh, i);
                var pt = ShaderUtil.GetPropertyType(sh, i);
                bool isTex = pt == ShaderUtil.ShaderPropertyType.TexEnv;
                bool overridden;
                string matStr, ovStr;

                switch (pt)
                {
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        overridden = _mpb.HasTexture(name);
                        matStr = TexName(mat.GetTexture(name));
                        ovStr = overridden ? TexName(_mpb.GetTexture(name)) : null;
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        overridden = _mpb.HasColor(name) || _mpb.HasVector(name);
                        matStr = mat.GetColor(name).ToString();
                        ovStr = overridden ? _mpb.GetColor(name).ToString() : null;
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        overridden = _mpb.HasVector(name);
                        matStr = V(mat.GetVector(name));
                        ovStr = overridden ? V(_mpb.GetVector(name)) : null;
                        break;
                    default: // Float, Range, Int — шейдеры проекта объявляют такие как Float
                        overridden = _mpb.HasFloat(name) || _mpb.HasInt(name);
                        matStr = mat.GetFloat(name).ToString("0.###");
                        ovStr = overridden ? _mpb.GetFloat(name).ToString("0.###") : null;
                        break;
                }

                if (!_showAll && !isTex && !overridden)
                    continue;
                string val = overridden ? $"MPB={ovStr}   (материал={matStr})" : matStr;
                EditorGUILayout.LabelField(name, val, overridden ? EditorStyles.boldLabel : EditorStyles.label);
            }
        }

        private static string TexName(Texture t) => t != null ? t.name : "None";
        private static string V(Vector4 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
    }
}
#endif
