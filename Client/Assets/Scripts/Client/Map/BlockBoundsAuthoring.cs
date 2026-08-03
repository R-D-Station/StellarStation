using UnityEngine;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Авторинг боксов блока в сцене (à la BoxCollider): гизмо трёх наборов AABB BlockDefinition, правка — в BlockBoundsAuthoringEditor.</summary>
    public sealed class BlockBoundsAuthoring : MonoBehaviour
    {
        /// <summary>Какой набор боксов правят хендлы в сцене.</summary>
        public enum BoxSet { Collision, Open, Trigger, Cabin }

        [Tooltip("Редактируемый тип блока.")]
        public BlockDefinition Target;
        [Tooltip("Набор боксов для правки хендлами: коллизия / открытая коллизия / триггеры двери.")]
        public BoxSet Editing = BoxSet.Collision;

        private static readonly Color Closed = new Color(0.2f, 1f, 0.4f, 0.9f);
        private static readonly Color Opened = new Color(0.3f, 0.8f, 1f, 0.8f);
        private static readonly Color Trigger = new Color(1f, 0.8f, 0.2f, 0.8f);
        private static readonly Color Cabin = new Color(0.9f, 0.4f, 1f, 0.85f);

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
            BoxSet.Cabin => Target.LiftCabinBoxes,
            _ => Target.CollisionBoxes
        };

        /// <summary>Гизмо-цвет набора боксов.</summary>
        public static Color SetColor(BoxSet set) => set switch
        {
            BoxSet.Open => Opened,
            BoxSet.Trigger => Trigger,
            BoxSet.Cabin => Cabin,
            _ => Closed
        };

        public static bool HasCabinSet(BlockDefinition def)
            => def != null && def.IsLiftPart && def.LiftKind == LiftPartKind.Cabin;

        public static Vector3Int ObjectExtent(BlockDefinition def, BoxSet set)
        {
            if (def == null)
                return Vector3Int.one;
            return set == BoxSet.Cabin && HasCabinSet(def) ? def.LiftModule : def.Size;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            var extent = ObjectExtent(Target, Editing);

            // Якорная часть (part 0) — блок [0..1]³, ориентир origin/поворота.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(ObjectToLocal(new Vector3(0.5f, 0.5f, 0.5f), extent), Vector3.one);

            if (extent.x * extent.y * extent.z > 1)
            {
                Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.35f);
                Gizmos.DrawWireCube(new Vector3(0f, extent.y * 0.5f, 0f), new Vector3(extent.x, extent.y, extent.z));
            }

            if (Target == null)
                return;

            // Все наборы цветом (правит хендлами только выбранный — в редакторе).
            var size = Target.Size;
            DrawSet(Target.CollisionBoxes, Closed, size);
            DrawSet(Target.CollisionBoxesOpen, Opened, size);
            DrawSet(Target.TriggerBoxes, Trigger, size);
            if (HasCabinSet(Target))
                DrawSet(Target.LiftCabinBoxes, Cabin, Target.LiftModule);
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
