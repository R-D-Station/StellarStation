using UnityEngine;

namespace Client.Map
{
    /// <summary>
    /// Авторинг коллизии блока в сцене (à la BoxCollider): визуализирует единичный куб и AABB выбранного
    /// BlockDefinition; правка хендлами/числами — в BlockBoundsAuthoringEditor. Оси блок-мира = оси Unity
    /// (Y — высота), координаты бокса [0..1]; блок 1×1×1 ЦЕНТРИРОВАН на объекте (сдвиг −0.5).
    /// </summary>
    public sealed class BlockBoundsAuthoring : MonoBehaviour
    {
        [Tooltip("Редактируемый тип блока.")]
        public BlockDefinition Target;

        /// <summary>Центр бокса [0..1] → локальные координаты (блок центрирован на объекте).</summary>
        public static Vector3 BoxCenterToLocal(Vector3 c) => c - new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>Обратно: локальный центр → координаты бокса [0..1].</summary>
        public static Vector3 LocalToBoxCenter(Vector3 l) => l + new Vector3(0.5f, 0.5f, 0.5f);

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            // Якорная часть 1×1×1 (центр = объект) + габарит всего объекта [0..Size]:
            // боксы авторятся в OBJECT-space и нарезаются кодогеном по частям.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

            if (Target == null || Target.CollisionBoxes == null)
                return;

            var size = Target.Size;
            if (size.x * size.y * size.z > 1)
            {
                Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.35f);
                Gizmos.DrawWireCube(
                    new Vector3((size.x - 1) * 0.5f, (size.y - 1) * 0.5f, (size.z - 1) * 0.5f),
                    new Vector3(size.x, size.y, size.z));
            }

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            foreach (var box in Target.CollisionBoxes)
                Gizmos.DrawWireCube(BoxCenterToLocal(box.Center), box.Size);
        }
    }
}
