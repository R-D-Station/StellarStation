using UnityEngine;
using UnityEngine.Events;
using Client.Gameplay;

namespace Client.Map
{
    /// <summary>Триггер-объём на префабе блока с событиями входа/выхода/нахождения внутри.
    /// ⚠ ЧИСТО ВИЗУАЛЬНЫЙ механизм: клиентская презентация, симуляцией НЕ управляет.
    /// Игровая логика (выдача предметов, урон, открытие дверей) авторитарна на сервере — вешать её сюда НЕЛЬЗЯ.</summary>
    public sealed class TriggerZone : MonoBehaviour, IBlockComponent
    {
        [Tooltip("Центр бокса в ЛОКАЛЬНЫХ координатах префаба (поворот блока учитывается сам).")]
        [SerializeField] private Vector3 _boxCenter = Vector3.zero;
        [Tooltip("Размер бокса в локальных координатах.")]
        [SerializeField] private Vector3 _boxSize = new Vector3(3f, 3f, 3f);
        [Tooltip("Запас на выход: войти можно только в бокс, выйти — только за бокс+запас (антидребезг на грани).")]
        [SerializeField] private float _exitMargin = 0.3f;

        [Tooltip("Игрок вошёл в объём (один раз на вход).")]
        [SerializeField] private UnityEvent _onEnter;
        [Tooltip("Игрок вышел из объёма (один раз на выход).")]
        [SerializeField] private UnityEvent _onExit;
        [Tooltip("Игрок внутри — вызывается КАЖДЫЙ кадр. Тяжёлая реакция здесь бьёт по частоте кадров.")]
        [SerializeField] private UnityEvent _onStand;

        private bool _inside;

        public bool IsInside => _inside;

        public void Evaluate(Vector3 worldPos)
        {
            Vector3 p = transform.InverseTransformPoint(worldPos);
            bool next = ZoneBox.NextInside(_inside, p.x, p.y, p.z,
                                           _boxCenter.x, _boxCenter.y, _boxCenter.z,
                                           _boxSize.x, _boxSize.y, _boxSize.z, _exitMargin);

            if (next != _inside)
            {
                _inside = next;
                if (next)
                    _onEnter?.Invoke();
                else
                    _onExit?.Invoke();
            }

            if (_inside)
                _onStand?.Invoke();
        }

        public void OnSpawn(in BlockContext ctx)
        {
            _inside = false;
            TriggerZoneRegistry.Register(this);
        }

        public void OnDespawn()
        {
            TriggerZoneRegistry.Unregister(this);
            if (_inside)
            {
                _inside = false;
                _onExit?.Invoke();
            }
        }

        public void OnVisibility(float alpha) { }

        private void OnDisable() => TriggerZoneRegistry.Unregister(this);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = _inside ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(1f, 0.5f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(_boxCenter, _boxSize);
            if (_exitMargin > 0f)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.25f);
                Gizmos.DrawWireCube(_boxCenter, _boxSize + Vector3.one * (_exitMargin * 2f));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
