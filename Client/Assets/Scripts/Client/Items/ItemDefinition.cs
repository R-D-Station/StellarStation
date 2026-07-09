using UnityEngine;

namespace Client.Items
{
    /// <summary>Определение предмета (клиентское): визуал + метаданные по ItemDefId (server-authoritative id).</summary>
    [CreateAssetMenu(menuName = "Station/Item Definition", fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Tooltip("Server-authoritative id предмета (совпадает с ItemInstance.ItemDefId на проводе).")]
        public ushort ItemDefId;

        [Tooltip("Отображаемое имя (тултип/UI).")]
        public string DisplayName;

        [Tooltip("Спрайт наземного предмета.")]
        public Sprite Sprite;

        // Размер предмета как ДОЛЯ тайла (1 = целый тайл); мировой размер = RenderScale·TileSize. Инспектор показывает %↔юниты (EditorCoder).
        [SerializeField] private float _renderScale = 1f;
        public float RenderScale => _renderScale;
    }
}
