#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>
    /// Редактирование AABB блока для <see cref="BlockBoundsAuthoring"/>: числовые Центр/Размер по каждому
    /// боксу + хендлы в SceneView (à la BoxCollider; блок центрирован на объекте — сдвиг ±0.5).
    /// Оси Unity: X/Z — план, Y — высота; координаты бокса [0..1].
    /// </summary>
    [CustomEditor(typeof(BlockBoundsAuthoring))]
    public sealed class BlockBoundsAuthoringEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle _handle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var authoring = (BlockBoundsAuthoring)target;
            var def = authoring.Target;
            if (def == null)
            {
                EditorGUILayout.HelpBox("Назначь BlockDefinition — его боксы появятся в сцене.", MessageType.Info);
                return;
            }

            // Числовая правка боксов SO прямо здесь (оси Unity: Y — высота; блок [0..1]).
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Боксы (оси Unity, Y = высота)", EditorStyles.boldLabel);
            var boxes = def.CollisionBoxes ?? System.Array.Empty<BlockDefinition.CollisionBox>();

            foreach (var b in boxes)
            {
                Vector3 min = b.Center - b.Size * 0.5f;
                Vector3 max = b.Center + b.Size * 0.5f;
                var sv = def.Size;
                if (min.x < -0.001f || min.y < -0.001f || min.z < -0.001f ||
                    max.x > sv.x + 0.001f || max.y > sv.y + 0.001f || max.z > sv.z + 0.001f)
                {
                    EditorGUILayout.HelpBox(
                        $"Бокс за габаритом объекта [0..{sv.x}]×[0..{sv.y}]×[0..{sv.z}]. Коллизия авторится " +
                        "по ВСЕМУ объекту (Size) — кодоген сам нарежет её по частям.",
                        MessageType.Warning);
                    break;
                }
            }
            int removeAt = -1;
            for (int i = 0; i < boxes.Length; i++)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 center = EditorGUILayout.Vector3Field($"Центр {i}", boxes[i].Center);
                Vector3 size = EditorGUILayout.Vector3Field($"Размер {i}", boxes[i].Size);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Удалить", GUILayout.Width(70)))
                        removeAt = i;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(def, "Edit collision box");
                    boxes[i].Center = center;
                    boxes[i].Size = size;
                    EditorUtility.SetDirty(def);
                }
                EditorGUILayout.Space(4);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ бокс"))
                {
                    Undo.RecordObject(def, "Add collision box");
                    var list = new System.Collections.Generic.List<BlockDefinition.CollisionBox>(boxes)
                        { BlockDefinition.CollisionBox.Full };
                    def.CollisionBoxes = list.ToArray();
                    EditorUtility.SetDirty(def);
                }
                if (GUILayout.Button("Квантовать 1/16"))
                {
                    Undo.RecordObject(def, "Quantize collision boxes");
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        Vector3 min = Quantize(boxes[i].Center - boxes[i].Size * 0.5f, def.Size);
                        Vector3 max = Quantize(boxes[i].Center + boxes[i].Size * 0.5f, def.Size);
                        boxes[i].Center = (min + max) * 0.5f;
                        boxes[i].Size = max - min;
                    }
                    EditorUtility.SetDirty(def);
                }
            }

            if (removeAt >= 0)
            {
                Undo.RecordObject(def, "Remove collision box");
                var list = new System.Collections.Generic.List<BlockDefinition.CollisionBox>(boxes);
                list.RemoveAt(removeAt);
                def.CollisionBoxes = list.ToArray();
                EditorUtility.SetDirty(def);
            }
        }

        private void OnSceneGUI()
        {
            var authoring = (BlockBoundsAuthoring)target;
            var def = authoring.Target;
            if (def == null || def.CollisionBoxes == null)
                return;

            using (new Handles.DrawingScope(authoring.transform.localToWorldMatrix))
            {
                Handles.color = new Color(0.2f, 1f, 0.4f, 0.9f);
                for (int i = 0; i < def.CollisionBoxes.Length; i++)
                {
                    // Оси совпадают с Unity; блок центрирован на объекте — только сдвиг ±0.5.
                    _handle.center = BlockBoundsAuthoring.BoxCenterToLocal(def.CollisionBoxes[i].Center);
                    _handle.size = def.CollisionBoxes[i].Size;

                    EditorGUI.BeginChangeCheck();
                    _handle.DrawHandle();
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(def, "Edit collision box");
                        def.CollisionBoxes[i].Center = BlockBoundsAuthoring.LocalToBoxCenter(_handle.center);
                        def.CollisionBoxes[i].Size = _handle.size;
                        EditorUtility.SetDirty(def);
                    }
                }
            }
        }

        // Кламп — в габарит объекта (object-space [0..Size]), не в один блок.
        private static Vector3 Quantize(Vector3 v, Vector3Int size)
            => new Vector3(Q(v.x, size.x), Q(v.y, size.y), Q(v.z, size.z));

        private static float Q(float v, int maxUnits) => Mathf.Clamp(Mathf.Round(v * 16f), 0f, 16f * maxUnits) / 16f;
    }
}
#endif
