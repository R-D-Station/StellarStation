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

        private static readonly GUIContent LEditing = new GUIContent("Набор", "Какой набор боксов правят хендлы в сцене.");
        private static readonly string[] SetNames = { "Коллизия", "Открытая", "Триггеры", "Кабина" };
        private static readonly string[] SetNamesNoCabin = { "Коллизия", "Открытая", "Триггеры" };
        private static readonly GUIContent LShowAll = new GUIContent("Показать все",
            "Снять скрытие со всех боксов набора.");
        private static readonly GUIContent LBoxShown = new GUIContent("Вид",
            "Бокс виден и хватается хендлами в сцене. Нажми, чтобы скрыть его на время правки соседних.");
        private static readonly GUIContent LBoxHidden = new GUIContent("Скрыт",
            "Бокс скрыт в сцене (только вид — на коллизию, кодоген и экспорт не влияет). Нажми, чтобы вернуть.");

        private BlockDefinition _maskDef;
        private BlockBoundsAuthoring.BoxSet _maskSet;
        private int _maskCount = -1;
        private ulong _mask;

        private ulong EnsureMask(BlockDefinition def, BlockBoundsAuthoring.BoxSet set, int count)
        {
            if (_maskDef != def || _maskSet != set || _maskCount != count)
            {
                _maskDef = def;
                _maskSet = set;
                _maskCount = count;
                _mask = BoxVisibilityMask.Load(def, set.ToString(), count);
            }
            return _mask;
        }

        public override void OnInspectorGUI()
        {
            var authoring = (BlockBoundsAuthoring)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Target"));
            serializedObject.ApplyModifiedProperties();

            var def = authoring.Target;
            bool cabinAvailable = BlockBoundsAuthoring.HasCabinSet(def);
            var set = EffectiveSet(authoring);

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(LEditing, (int)set, cabinAvailable ? SetNames : SetNamesNoCabin);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authoring, "Change box set");
                authoring.Editing = (BlockBoundsAuthoring.BoxSet)picked;
                EditorUtility.SetDirty(authoring);
                set = EffectiveSet(authoring);
            }

            if (def == null)
            {
                EditorGUILayout.HelpBox("Назначь BlockDefinition — его боксы появятся в сцене.", MessageType.Info);
                return;
            }

            var boxes = authoring.Boxes(set) ?? System.Array.Empty<BlockDefinition.CollisionBox>();
            bool clamp = set != BlockBoundsAuthoring.BoxSet.Trigger; // триггеры могут выходить за габарит
            var extent = BlockBoundsAuthoring.ObjectExtent(def, set);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Боксы: {Label(set)} (оси Unity, Y = высота)", EditorStyles.boldLabel);
            if (set == BlockBoundsAuthoring.BoxSet.Cabin)
                EditorGUILayout.HelpBox($"Object-space кабины — МОДУЛЬ {extent.x}×{extent.y}×{extent.z} (не габарит блока).", MessageType.Info);

            if (clamp)
            {
                var sv = extent;
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

            ulong mask = EnsureMask(def, set, boxes.Length);
            int hidden = BoxVisibilityMask.HiddenCount(mask, boxes.Length);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(hidden > 0 ? $"Скрыто {hidden} из {boxes.Length}" : $"Показаны все ({boxes.Length})",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(LShowAll, GUILayout.Width(100)))
                {
                    _mask = 0UL;
                    BoxVisibilityMask.Save(def, set.ToString(), boxes.Length, _mask);
                    mask = 0UL;
                    SceneView.RepaintAll();
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
                    bool visible = !BoxVisibilityMask.IsHidden(mask, i);
                    bool next = GUILayout.Toggle(visible, visible ? LBoxShown : LBoxHidden,
                        EditorStyles.miniButton, GUILayout.Width(52));
                    if (next != visible)
                    {
                        _mask = BoxVisibilityMask.Toggle(mask, i);
                        BoxVisibilityMask.Save(def, set.ToString(), boxes.Length, _mask);
                        mask = _mask;
                        SceneView.RepaintAll();
                    }
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
                        Vector3 min = Quantize(boxes[i].Center - boxes[i].Size * 0.5f, extent, clamp);
                        Vector3 max = Quantize(boxes[i].Center + boxes[i].Size * 0.5f, extent, clamp);
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
            var set = EffectiveSet(authoring);
            var boxes = authoring.Boxes(set);
            if (boxes == null)
                return;
            var extent = BlockBoundsAuthoring.ObjectExtent(def, set);
            ulong mask = EnsureMask(def, set, boxes.Length);

            using (new Handles.DrawingScope(authoring.transform.localToWorldMatrix))
            {
                Handles.color = BlockBoundsAuthoring.SetColor(set);
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (BoxVisibilityMask.IsHidden(mask, i))
                        continue;
                    // Тот же сдвиг object→local, что и в гизмо (пивот = центр низа футпринта).
                    _handle.center = BlockBoundsAuthoring.ObjectToLocal(boxes[i].Center, extent);
                    _handle.size = boxes[i].Size;

                    EditorGUI.BeginChangeCheck();
                    _handle.DrawHandle();
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(def, "Edit box");
                        boxes[i].Center = BlockBoundsAuthoring.LocalToObject(_handle.center, extent);
                        boxes[i].Size = _handle.size;
                        EditorUtility.SetDirty(def);
                    }
                }
            }
        }

        private static BlockBoundsAuthoring.BoxSet EffectiveSet(BlockBoundsAuthoring authoring)
        {
            var set = authoring.Editing;
            if (set == BlockBoundsAuthoring.BoxSet.Cabin && !BlockBoundsAuthoring.HasCabinSet(authoring.Target))
                return BlockBoundsAuthoring.BoxSet.Collision;
            return set;
        }

        private static void Assign(BlockDefinition def, BlockBoundsAuthoring.BoxSet set,
                                   BlockDefinition.CollisionBox[] boxes)
        {
            switch (set)
            {
                case BlockBoundsAuthoring.BoxSet.Open: def.CollisionBoxesOpen = boxes; break;
                case BlockBoundsAuthoring.BoxSet.Trigger: def.TriggerBoxes = boxes; break;
                case BlockBoundsAuthoring.BoxSet.Cabin: def.LiftCabinBoxes = boxes; break;
                default: def.CollisionBoxes = boxes; break;
            }
        }

        private static string Label(BlockBoundsAuthoring.BoxSet set) => set switch
        {
            BlockBoundsAuthoring.BoxSet.Open => "открытая коллизия",
            BlockBoundsAuthoring.BoxSet.Trigger => "триггеры",
            BlockBoundsAuthoring.BoxSet.Cabin => "кабина",
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
