using UnityEngine;

namespace Client.Map
{
    public sealed class BlockGizmo : MonoBehaviour
    {
        public enum Shape { None, WireSphere, WireCube, SolidCube }

        [SerializeField] private Color _color = Color.green;
        [SerializeField] private Shape _shape = Shape.WireSphere;
        [SerializeField, Tooltip("Радиус сферы / полу-габарит куба (блоков).")]
        private float _size = 0.5f;
        [SerializeField, Tooltip("Подпись над гизмо (напр. «Spawn»).")]
        private string _label = "";
        [SerializeField, Tooltip("Иконка (лежит в Assets/Gizmos/); рисуется в точке блока.")]
        private Texture2D _icon;

        public void Draw()
        {
            Vector3 c = transform.position;
            Gizmos.color = _color;
            switch (_shape)
            {
                case Shape.WireSphere: Gizmos.DrawWireSphere(c, _size); break;
                case Shape.WireCube: Gizmos.DrawWireCube(c, Vector3.one * (_size * 2f)); break;
                case Shape.SolidCube: Gizmos.DrawCube(c, Vector3.one * (_size * 2f)); break;
            }
            if (_icon != null)
                Gizmos.DrawIcon(c, _icon.name, true);
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(_label))
                UnityEditor.Handles.Label(c + Vector3.up * (_size + 0.15f), _label);
#endif
        }

        private void OnDrawGizmos()
        {
            if (GetComponentInParent<BlockMapAuthoring>() != null)
                return;
            Draw();
        }
    }
}
