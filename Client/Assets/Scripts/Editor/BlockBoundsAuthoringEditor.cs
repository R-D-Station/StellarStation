#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Client.Map;

namespace Client.Editor.Inspectors
{
    /// <summary>Редактирование боксов <see cref="BlockBoundsAuthoring"/>: числа + SceneView-хендлы для выбранного набора (коллизия/открытая/триггеры).</summary>
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

            var set = authoring.Editing;
            var boxes = authoring.Boxes(set) ?? System.Array.Empty<BlockDefinition.CollisionBox>();
            bool clamp = set != BlockBoundsAuthoring.BoxSet.Trigger; // триггеры могут выходить за габарит

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Боксы: {Label(set)} (оси Unity, Y = высота)", EditorStyles.boldLabel);

            if (clamp)
            {
                var sv = def.Size;
                foreach (var b in boxes)
                {
                    Vector3 min = b.Center - b.Size * 0.5f, max = b.Center + b.Size * 0.5f;
                    if (min.x < -0.001f || min.y < -0.001f || min.z < -0.001f ||
                        max.x > sv.x + 0.001f || max.y > sv.y + 0.001f || max.z > sv.z + 0.001f)
                    {
                        EditorGUILayout.HelpBox(
                            $"Бокс за габаритом [0..{sv.x}]×[0..{sv.y}]×[0..{sv.z}] — кодоген нарежет по частям.",
                            MessageType.Warning);
                        break;
                    }
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
                    Undo.RecordObject(def, "Edit box");
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
                    Undo.RecordObject(def, "Add box");
                    var list = new System.Collections.Generic.List<BlockDefinition.CollisionBox>(boxes)
                        { BlockDefinition.CollisionBox.Full };
                    Assign(def, set, list.ToArray());
                    EditorUtility.SetDirty(def);
                }
                if (GUILayout.Button("Квантовать 1/16"))
                {
                    Undo.RecordObject(def, "Quantize boxes");
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        Vector3 min = Quantize(boxes[i].Center - boxes[i].Size * 0.5f, def.Size, clamp);
                        Vector3 max = Quantize(boxes[i].Center + boxes[i].Size * 0.5f, def.Size, clamp);
                        boxes[i].Center = (min + max) * 0.5f;
                        boxes[i].Size = max - min;
                    }
                    EditorUtility.SetDirty(def);
                }
            }

            if (removeAt >= 0)
            {
                Undo.RecordObject(def, "Remove box");
                var list = new System.Collections.Generic.List<BlockDefinition.CollisionBox>(boxes);
                list.RemoveAt(removeAt);
                Assign(def, set, list.ToArray());
                EditorUtility.SetDirty(def);
            }
        }

        private void OnSceneGUI()
        {
            var authoring = (BlockBoundsAuthoring)target;
            var def = authoring.Target;
            if (def == null)
                return;
            var set = authoring.Editing;
            var boxes = authoring.Boxes(set);
            if (boxes == null)
                return;

            using (new Handles.DrawingScope(authoring.transform.localToWorldMatrix))
            {
                Handles.color = BlockBoundsAuthoring.SetColor(set);
                for (int i = 0; i < boxes.Length; i++)
                {
                    // Тот же сдвиг object→local, что и в гизмо (пивот = центр низа футпринта).
                    _handle.center = BlockBoundsAuthoring.ObjectToLocal(boxes[i].Center, def.Size);
                    _handle.size = boxes[i].Size;

                    EditorGUI.BeginChangeCheck();
                    _handle.DrawHandle();
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(def, "Edit box");
                        boxes[i].Center = BlockBoundsAuthoring.LocalToObject(_handle.center, def.Size);
                        boxes[i].Size = _handle.size;
                        EditorUtility.SetDirty(def);
                    }
                }
            }
        }

        private static void Assign(BlockDefinition def, BlockBoundsAuthoring.BoxSet set,
                                   BlockDefinition.CollisionBox[] boxes)
        {
            switch (set)
            {
                case BlockBoundsAuthoring.BoxSet.Open: def.CollisionBoxesOpen = boxes; break;
                case BlockBoundsAuthoring.BoxSet.Trigger: def.TriggerBoxes = boxes; break;
                default: def.CollisionBoxes = boxes; break;
            }
        }

        private static string Label(BlockBoundsAuthoring.BoxSet set) => set switch
        {
            BlockBoundsAuthoring.BoxSet.Open => "открытая коллизия",
            BlockBoundsAuthoring.BoxSet.Trigger => "триггеры",
            _ => "коллизия"
        };

        // Триггеры не клампятся в габарит (могут торчать за грань), коллизия/открытая — клампятся.
        private static Vector3 Quantize(Vector3 v, Vector3Int size, bool clamp)
            => new Vector3(Q(v.x, size.x, clamp), Q(v.y, size.y, clamp), Q(v.z, size.z, clamp));

        private static float Q(float v, int maxUnits, bool clamp)
        {
            float q = Mathf.Round(v * 16f);
            if (clamp) q = Mathf.Clamp(q, 0f, 16f * maxUnits);
            return q / 16f;
        }
    }
}
#endif
