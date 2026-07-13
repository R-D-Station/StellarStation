using UnityEngine;

namespace Client.Map
{
    /// <summary>Авторинг боксов блока в сцене (à la BoxCollider): гизмо трёх наборов AABB BlockDefinition, правка — в BlockBoundsAuthoringEditor.</summary>
    public sealed class BlockBoundsAuthoring : MonoBehaviour
    {
        /// <summary>Какой набор боксов правят хендлы в сцене.</summary>
        public enum BoxSet { Collision, Open, Trigger }

        [Tooltip("Редактируемый тип блока.")]
        public BlockDefinition Target;
        [Tooltip("Набор боксов для правки хендлами: коллизия / открытая коллизия / триггеры двери.")]
        public BoxSet Editing = BoxSet.Collision;

        private static readonly Color Closed = new Color(0.2f, 1f, 0.4f, 0.9f);
        private static readonly Color Opened = new Color(0.3f, 0.8f, 1f, 0.8f);
        private static readonly Color Trigger = new Color(1f, 0.8f, 0.2f, 0.8f);

        /// <summary>Сдвиг object-space [0..Size] → локальные координаты префаба: пивот = центр низа футпринта.</summary>
        public static Vector3 PivotOffset(Vector3Int size) => new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        /// <summary>Object-space [0..Size] → локальные координаты префаба.</summary>
        public static Vector3 ObjectToLocal(Vector3 objectPos, Vector3Int size) => objectPos - PivotOffset(size);
        /// <summary>Локальные координаты префаба → object-space [0..Size].</summary>
        public static Vector3 LocalToObject(Vector3 local, Vector3Int size) => local + PivotOffset(size);

        /// <summary>Массив боксов Target по набору (для редактора).</summary>
        public BlockDefinition.CollisionBox[] Boxes(BoxSet set) => Target == null ? null : set switch
        {
            BoxSet.Open => Target.CollisionBoxesOpen,
            BoxSet.Trigger => Target.TriggerBoxes,
            _ => Target.CollisionBoxes
        };

        /// <summary>Гизмо-цвет набора боксов.</summary>
        public static Color SetColor(BoxSet set) => set switch
        {
            BoxSet.Open => Opened,
            BoxSet.Trigger => Trigger,
            _ => Closed
        };

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            var size = Target != null ? Target.Size : Vector3Int.one;

            // Якорная часть (part 0) — блок [0..1]³, ориентир origin/поворота.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(ObjectToLocal(new Vector3(0.5f, 0.5f, 0.5f), size), Vector3.one);

            if (size.x * size.y * size.z > 1)
            {
                Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.35f);
                Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), new Vector3(size.x, size.y, size.z));
            }

            if (Target == null)
                return;

            // Все три набора цветом (правит хендлами только выбранный — в редакторе).
            DrawSet(Target.CollisionBoxes, Closed, size);
            DrawSet(Target.CollisionBoxesOpen, Opened, size);
            DrawSet(Target.TriggerBoxes, Trigger, size);
        }

        private static void DrawSet(BlockDefinition.CollisionBox[] boxes, Color color, Vector3Int size)
        {
            if (boxes == null)
                return;
            Gizmos.color = color;
            foreach (var box in boxes)
                Gizmos.DrawWireCube(ObjectToLocal(box.Center, size), box.Size);
        }
    }
}
