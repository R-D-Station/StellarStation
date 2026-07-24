#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Client.Items;
using Client.Config;

namespace Client.Editor.Inspectors
{
    /// <summary>Инспектор ItemDefinition: цветные сворачиваемые группы (паттерн BlockDefinitionEditor).</summary>
    [CustomEditor(typeof(ItemDefinition))]
    public sealed class ItemDefinitionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LId = new GUIContent("ID", "Server-authoritative id предмета (ItemInstance.ItemDefId на проводе). Не перенумеровывать.");
        private static readonly GUIContent LName = new GUIContent("Название", "Отображаемое имя (тултип/UI/заголовок окна контейнера).");
        private static readonly GUIContent LSprite = new GUIContent("Спрайт", "Спрайт наземного предмета.");
        private static readonly GUIContent LParent = new GUIContent("Родитель", "Наследование: незаданные поля берутся отсюда рекурсивно.");
        private static readonly GUIContent LCategory = new GUIContent("Категория", "Контейнер хранит предметы; рюкзак = Контейнер + Экипируемо.");
        private static readonly string[] CategoryNames = { "Предмет", "Контейнер" };
        private static readonly GUIContent LPct = new GUIContent("Размер %", "Размер спрайта как % от тайла (100 = целый тайл).");
        private static readonly GUIContent LUnits = new GUIContent("Размер, юниты", "То же в мировых юнитах (= % · TileSize). Правишь одно — второе пересчитывается.");
        private static readonly GUIContent LStack = new GUIContent("Стак", "Макс. размер стека. 0 = унаследовать от Родителя (корень → 1).");
        private static readonly GUIContent LSpan = new GUIContent("Слоты", "Сколько слотов инвентаря занимает. 0 = унаследовать от Родителя (корень → 1).");
        private static readonly GUIContent LEquipSlot = new GUIContent("Слот", "Категория слота экипировки. Inherit = унаследовать от Родителя (корень → None).");
        private static readonly GUIContent LMode = new GUIContent("Режим", "UI = наше окно; SS14 = физическая крышка (E: закрыл — всосал; открыл — высыпал).");
        private static readonly GUIContent LCapacity = new GUIContent("Слотов", "Вместимость (4–25). UI-окно показывает до 16; выше — SS14-объём высыпа.");
        private static readonly GUIContent LOpenSprite = new GUIContent("Откр. спрайт", "Мировой вид открытого контейнера; пусто → рисуем закрытый.");
        private static readonly GUIContent LWindow = new GUIContent("Окно", "Кастомный префаб окна; пусто → дефолтный из ContainerWindows.");
        private static readonly GUIContent LSuck = new GUIContent("Радиус всоса", "Планарный радиус всасывания наземных предметов при закрытии крышки.");
        private static readonly GUIContent LFilter = new GUIContent("Фильтр", "Белый — пускает только перечисленное; Чёрный — всё, кроме перечисленного.");
        private static readonly string[] FilterNames = { "Нет", "Белый", "Чёрный" };
        private static readonly GUIContent LFilterItems = new GUIContent("Предметы", "Конкретные предметы, разрешённые/запрещённые фильтром.");
        private static readonly GUIContent LCollision = new GUIContent("Коллизия", "Наземный предмет — препятствие в блок-мире (форма = целая ячейка).");
        private static readonly GUIContent LPullable = new GUIContent("Тянется", "Предмет можно тянуть (штраф скорости при тяге).");
        private static readonly GUIContent LWornVisible = new GUIContent("На игроке", "Рисовать надетый предмет поверх спрайта игрока.");
        private static readonly GUIContent[] LWornDir =
        {
            new GUIContent("Сев", "Спрайт надетого предмета, вид с севера."),
            new GUIContent("Юг", "Спрайт надетого предмета, вид с юга."),
            new GUIContent("Вос", "Спрайт надетого предмета, вид с востока."),
            new GUIContent("Зап", "Спрайт надетого предмета, вид с запада."),
        };

        private static readonly Color CWhite = new Color(0.80f, 0.80f, 0.82f);
        private static readonly Color CBlue = new Color(0.22f, 0.34f, 0.55f);
        private static readonly Color CGreen = new Color(0.24f, 0.44f, 0.28f);
        private static readonly Color CPurple = new Color(0.40f, 0.30f, 0.52f);

        private static bool _gCommon = true, _gEquip = true, _gContainer = true, _gPhysics = true;
        private static GUIStyle _hDark, _hLight;

        private SerializedProperty _itemDefId;
        private SerializedProperty _displayName;
        private SerializedProperty _sprite;
        private SerializedProperty _renderScale;
        private SerializedProperty _equippable;
        private SerializedProperty _equipSlot;
        private SerializedProperty _parent;
        private SerializedProperty _slotSpan;
        private SerializedProperty _maxStack;
        private SerializedProperty _isContainer;
        private SerializedProperty _maxContents;
        private SerializedProperty _openSprite;
        private SerializedProperty _windowPrefab;
        private SerializedProperty _hasCollision;
        private SerializedProperty _pullable;
        private SerializedProperty _wornVisible;
        private SerializedProperty _wornSprites;
        private SerializedProperty _tags;
        private SerializedProperty _filterMode;
        private SerializedProperty _filterItems;
        private SerializedProperty _filterTags;
        private SerializedProperty _containerMode;
        private SerializedProperty _suckRadius;
        private SerializedProperty _renderLayer;

        private TagCatalog _tagCatalog;
        private string[] _tagNames = System.Array.Empty<string>();
        private ushort[] _tagIds = System.Array.Empty<ushort>();
        private string _newTagName = "";

        private RenderLayerCatalog _renderLayerCatalog;
        private string[] _layerNames = System.Array.Empty<string>();
        private ushort[] _layerIds = System.Array.Empty<ushort>();
        private string _newLayerName = "";
        private int _newLayerOrder;

        private void OnEnable()
        {
            _itemDefId = serializedObject.FindProperty("ItemDefId");
            _displayName = serializedObject.FindProperty("DisplayName");
            _sprite = serializedObject.FindProperty("Sprite");
            _renderScale = serializedObject.FindProperty("_renderScale");
            _equippable = serializedObject.FindProperty("_equippable");
            _equipSlot = serializedObject.FindProperty("_equipSlot");
            _parent = serializedObject.FindProperty("_parent");
            _slotSpan = serializedObject.FindProperty("_slotSpan");
            _maxStack = serializedObject.FindProperty("_maxStack");
            _isContainer = serializedObject.FindProperty("_isContainer");
            _maxContents = serializedObject.FindProperty("_maxContents");
            _openSprite = serializedObject.FindProperty("OpenSprite");
            _windowPrefab = serializedObject.FindProperty("WindowPrefab");
            _hasCollision = serializedObject.FindProperty("_hasCollision");
            _pullable = serializedObject.FindProperty("_pullable");
            _wornVisible = serializedObject.FindProperty("_wornVisible");
            _wornSprites = serializedObject.FindProperty("_wornSprites");
            _tags = serializedObject.FindProperty("_tags");
            _filterMode = serializedObject.FindProperty("_filterMode");
            _filterItems = serializedObject.FindProperty("_filterItems");
            _filterTags = serializedObject.FindProperty("_filterTags");
            _containerMode = serializedObject.FindProperty("_containerMode");
            _suckRadius = serializedObject.FindProperty("_suckRadius");
            _renderLayer = serializedObject.FindProperty("_renderLayer");
            RefreshTagCatalog();
            RefreshRenderLayerCatalog();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Draw(_itemDefId, LId);
            Draw(_displayName, LName);
            Draw(_sprite, LSprite);
            Draw(_parent, LParent);

            if (_isContainer != null)
            {
                // "Категория" — UI-вью над _isContainer, отдельного поля данных нет.
                EditorGUI.BeginChangeCheck();
                int picked = EditorGUILayout.Popup(LCategory, _isContainer.boolValue ? 1 : 0, CategoryNames);
                if (EditorGUI.EndChangeCheck())
                    _isContainer.boolValue = picked == 1;
            }

            DrawPropertiesExcluding(serializedObject, "m_Script", "ItemDefId", "DisplayName", "Sprite",
                "OpenSprite", "WindowPrefab", "_renderScale", "_equippable", "_equipSlot", "_parent",
                "_slotSpan", "_maxStack", "_isContainer", "_maxContents", "_hasCollision", "_pullable",
                "_tags", "_filterMode", "_filterItems", "_filterTags", "_containerMode", "_suckRadius", "_renderLayer",
                "_wornVisible", "_wornSprites");

            EditorGUILayout.Space(6);

            if (BeginGroup(ref _gCommon, "Общие", CWhite, true))
            {
                DrawSizePair();
                EditorGUILayout.Space(4);
                DrawLayerDropdown();
                DrawNewLayerRow();
                EditorGUILayout.Space(4);
                DrawTagList(_tags, "Теги");
                DrawNewTagRow();
                EditorGUILayout.Space(4);
                Draw(_maxStack, LStack);
                Draw(_slotSpan, LSpan);
            }
            EndGroup(_gCommon);

            if (_equippable != null)
            {
                if (BeginGroupToggle(ref _gEquip, "Экипировка", CBlue, false, _equippable))
                {
                    using (new EditorGUI.DisabledScope(!_equippable.boolValue))
                        Draw(_equipSlot, LEquipSlot);
                    if (_equippable.boolValue)
                    {
                        Draw(_wornVisible, LWornVisible);
                        if (_wornVisible != null && _wornVisible.boolValue)
                            DrawWornSprites();
                    }
                }
                EndGroup(_gEquip);
            }

            if (_isContainer != null && _isContainer.boolValue)
            {
                if (BeginGroup(ref _gContainer, "Контейнер", CGreen, false))
                {
                    Draw(_containerMode, LMode);
                    DrawCapacitySlider();
                    Draw(_openSprite, LOpenSprite);
                    bool ss14 = _containerMode != null && _containerMode.enumValueIndex == 1;
                    if (ss14) Draw(_suckRadius, LSuck);
                    else Draw(_windowPrefab, LWindow);
                    DrawFilter();
                }
                EndGroup(_gContainer);
            }

            if (BeginGroup(ref _gPhysics, "Физика", CPurple, false))
            {
                Draw(_hasCollision, LCollision);
                Draw(_pullable, LPullable);
            }
            EndGroup(_gPhysics);

            serializedObject.ApplyModifiedProperties();
        }

        private static void Draw(SerializedProperty p, GUIContent label, bool children = false)
        {
            if (p != null) EditorGUILayout.PropertyField(p, label, children);
        }

        private void DrawSizePair()
        {
            if (_renderScale == null)
            {
                EditorGUILayout.HelpBox("Поле '_renderScale' не найдено.", MessageType.Info);
                return;
            }
            float ts = RenderConfig.TileSize;
            EditorGUI.BeginChangeCheck();
            float pct = EditorGUILayout.FloatField(LPct, _renderScale.floatValue * 100f);
            if (EditorGUI.EndChangeCheck())
                _renderScale.floatValue = Mathf.Max(0f, pct / 100f);
            EditorGUI.BeginChangeCheck();
            float units = EditorGUILayout.FloatField(LUnits, _renderScale.floatValue * ts);
            if (EditorGUI.EndChangeCheck())
                _renderScale.floatValue = Mathf.Max(0f, ts > 0f ? units / ts : _renderScale.floatValue);
        }

        private void DrawWornSprites()
        {
            if (_wornSprites == null) return;
            if (_wornSprites.arraySize != 4) _wornSprites.arraySize = 4;
            for (int i = 0; i < 4; i++)
                EditorGUILayout.PropertyField(_wornSprites.GetArrayElementAtIndex(i), LWornDir[i]);
        }

        private void DrawCapacitySlider()
        {
            if (_maxContents == null) return;
            int cur = Mathf.Clamp(_maxContents.intValue, 4, 25);
            EditorGUI.BeginChangeCheck();
            int slots = EditorGUILayout.IntSlider(LCapacity, cur, 4, 25);
            if (EditorGUI.EndChangeCheck())
                _maxContents.intValue = slots;
        }

        private void DrawFilter()
        {
            if (_filterMode == null) return;
            int mi = EditorGUILayout.Popup(LFilter, _filterMode.enumValueIndex, FilterNames);
            _filterMode.enumValueIndex = Mathf.Clamp(mi, 0, 2);
            if (_filterMode.enumValueIndex == 0) return;
            Draw(_filterItems, LFilterItems, true);
            DrawTagList(_filterTags, "Теги фильтра");
        }

        private static bool BeginGroup(ref bool expanded, string title, Color bg, bool darkText)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(r, bg);
            Rect fr = new Rect(r.x + 14f, r.y + 1f, r.width - 16f, r.height);
            expanded = EditorGUI.Foldout(fr, expanded, title, true, HeaderStyle(darkText));
            if (expanded) EditorGUI.indentLevel++;
            return expanded;
        }

        private static bool BeginGroupToggle(ref bool expanded, string title, Color bg, bool darkText, SerializedProperty enabled)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(r, bg);
            Rect fr = new Rect(r.x + 14f, r.y + 1f, r.width - 40f, r.height);
            expanded = EditorGUI.Foldout(fr, expanded, title, true, HeaderStyle(darkText));
            if (enabled != null)
            {
                Rect tr = new Rect(r.xMax - 22f, r.y + 2f, 16f, 16f);
                enabled.boolValue = EditorGUI.Toggle(tr, enabled.boolValue);
            }
            if (expanded) EditorGUI.indentLevel++;
            return expanded;
        }

        private static void EndGroup(bool expanded)
        {
            if (expanded) EditorGUI.indentLevel--;
            EditorGUILayout.Space(3);
        }

        private static GUIStyle HeaderStyle(bool darkText)
        {
            if (darkText)
                return _hDark ??= MakeHeaderStyle(Color.black);
            return _hLight ??= MakeHeaderStyle(Color.white);
        }

        private static GUIStyle MakeHeaderStyle(Color c)
        {
            var s = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            s.normal.textColor = c; s.onNormal.textColor = c;
            s.hover.textColor = c; s.onHover.textColor = c;
            s.focused.textColor = c; s.onFocused.textColor = c;
            s.active.textColor = c; s.onActive.textColor = c;
            return s;
        }

        private void RefreshTagCatalog()
        {
            _tagCatalog = null;
            var guids = AssetDatabase.FindAssets("t:TagCatalog");
            if (guids.Length > 0)
                _tagCatalog = AssetDatabase.LoadAssetAtPath<TagCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            BuildTagArrays();
        }

        private void BuildTagArrays()
        {
            if (_tagCatalog == null)
            {
                _tagNames = System.Array.Empty<string>();
                _tagIds = System.Array.Empty<ushort>();
                return;
            }
            var entries = _tagCatalog.Entries;
            _tagNames = new string[entries.Count];
            _tagIds = new ushort[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                _tagNames[i] = entries[i].Name;
                _tagIds[i] = entries[i].Id;
            }
        }

        private int IndexOfId(ushort id)
        {
            for (int i = 0; i < _tagIds.Length; i++)
                if (_tagIds[i] == id) return i;
            return -1;
        }

        private void DrawTagList(SerializedProperty arr, string label)
        {
            if (arr == null) return;
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            if (_tagCatalog == null)
            {
                EditorGUILayout.HelpBox("Создай Tag Catalog (Create → Station → Tag Catalog).", MessageType.Info);
                return;
            }
            for (int i = 0; i < arr.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var el = arr.GetArrayElementAtIndex(i);
                    int cur = IndexOfId((ushort)el.intValue);
                    int picked = EditorGUILayout.Popup(cur < 0 ? 0 : cur, _tagNames);
                    if (_tagIds.Length > 0)
                        el.intValue = _tagIds[Mathf.Clamp(picked, 0, _tagIds.Length - 1)];
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        arr.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }
            if (_tagIds.Length > 0 && GUILayout.Button("+ тег", GUILayout.Width(80)))
            {
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).intValue = _tagIds[0];
            }
        }

        private void DrawNewTagRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _newTagName = EditorGUILayout.TextField("Новый тег", _newTagName);
                using (new EditorGUI.DisabledScope(_tagCatalog == null || string.IsNullOrWhiteSpace(_newTagName)))
                {
                    if (GUILayout.Button("Создать", GUILayout.Width(80)))
                    {
                        Undo.RecordObject(_tagCatalog, "Add Tag");
                        ushort id = _tagCatalog.AddTag(_newTagName.Trim());
                        EditorUtility.SetDirty(_tagCatalog);
                        AssetDatabase.SaveAssetIfDirty(_tagCatalog);
                        BuildTagArrays();
                        if (_tags != null)
                        {
                            _tags.arraySize++;
                            _tags.GetArrayElementAtIndex(_tags.arraySize - 1).intValue = id;
                        }
                        _newTagName = "";
                    }
                }
            }
        }

        private void RefreshRenderLayerCatalog()
        {
            _renderLayerCatalog = null;
            var guids = AssetDatabase.FindAssets("t:RenderLayerCatalog");
            if (guids.Length > 0)
                _renderLayerCatalog = AssetDatabase.LoadAssetAtPath<RenderLayerCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            BuildLayerArrays();
        }

        private void BuildLayerArrays()
        {
            if (_renderLayerCatalog == null)
            {
                _layerNames = System.Array.Empty<string>();
                _layerIds = System.Array.Empty<ushort>();
                return;
            }
            var entries = _renderLayerCatalog.Entries;
            _layerNames = new string[entries.Count];
            _layerIds = new ushort[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                _layerNames[i] = $"{entries[i].Name} ({entries[i].Order})";
                _layerIds[i] = entries[i].Id;
            }
        }

        private int LayerIndexOfId(ushort id)
        {
            for (int i = 0; i < _layerIds.Length; i++)
                if (_layerIds[i] == id) return i;
            return -1;
        }

        private void DrawLayerDropdown()
        {
            if (_renderLayer == null) return;
            if (_renderLayerCatalog == null)
            {
                EditorGUILayout.HelpBox("Создай Render Layer Catalog (Create → Station → Render Layer Catalog).", MessageType.Info);
                return;
            }
            if (_layerIds.Length == 0) return;
            int cur = LayerIndexOfId((ushort)_renderLayer.intValue);
            int picked = EditorGUILayout.Popup("Слой", cur < 0 ? 0 : cur, _layerNames);
            _renderLayer.intValue = _layerIds[Mathf.Clamp(picked, 0, _layerIds.Length - 1)];
        }

        private void DrawNewLayerRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _newLayerName = EditorGUILayout.TextField("Новый слой", _newLayerName);
                _newLayerOrder = EditorGUILayout.IntField(_newLayerOrder, GUILayout.Width(50));
                using (new EditorGUI.DisabledScope(_renderLayerCatalog == null || string.IsNullOrWhiteSpace(_newLayerName)))
                {
                    if (GUILayout.Button("Создать", GUILayout.Width(80)))
                    {
                        Undo.RecordObject(_renderLayerCatalog, "Add Render Layer");
                        ushort id = _renderLayerCatalog.AddLayer(_newLayerName.Trim(), _newLayerOrder);
                        EditorUtility.SetDirty(_renderLayerCatalog);
                        AssetDatabase.SaveAssetIfDirty(_renderLayerCatalog);
                        BuildLayerArrays();
                        if (_renderLayer != null) _renderLayer.intValue = id;
                        _newLayerName = "";
                        _newLayerOrder = 0;
                    }
                }
            }
        }
    }
}
#endif
