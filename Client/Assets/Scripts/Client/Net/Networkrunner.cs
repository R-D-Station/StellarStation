using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Messages.Core;
using Client.Net.View;
using Client.Gameplay.Input; // PlayerControl (сгенерирован Input System)

namespace Client.Net
{
    /// <summary>
    /// Точка сборки сетевого клиента. Создаёт NetClient поверх транспорта
    /// (на этапе 0 — LocalLoopbackTransport), читает input -> шлёт intent,
    /// принимает снапшоты и раздаёт их по NetEntityView (спавнит при нужде).
    ///
    /// НЕ трогает офлайн-Player/FSM. Это отдельный сетевой контур.
    /// Когда товарищ подключит реальный транспорт на LiteNetLib — здесь
    /// меняется ОДНА строка создания транспорта, остальное не трогается.
    /// </summary>
    public class NetworkRunner : MonoBehaviour
    {
        [SerializeField] private NetEntityView _entityViewPrefab;

        private NetClient _net;
        private PlayerControl _controls;
        private readonly Dictionary<int, NetEntityView> _views = new Dictionary<int, NetEntityView>();
        private string _address = "127.0.0.1";
        private int _port = 7777;

        private void Awake()
        {
            // ЕДИНСТВЕННАЯ строка, которую товарищ заменит на реальный транспорт:
            ITransport transport = new LiteNetLibTransport();

            _net = new NetClient(transport);
            _net.OnWorldSnapshot += OnSnapshot;

            _controls = new PlayerControl();
        }

        private void OnEnable()
        {
            _controls?.Enable();
            _net?.Connect(_address, _port);
        }

        private void OnDisable()
        {
            _net?.Disconnect();
            _controls?.Disable();
        }

        private void Update()
        {
            _net.Poll();

            // input -> intent. Доминантная ось, как в Player.UpdateFacing.
            Vector2 move = _controls.Player.Move.ReadValue<Vector2>();
            bool sprint = _controls.Player.Sprint.IsPressed();

            IntentDirection dir = ToIntent(move);
            if (dir != IntentDirection.None)
                _net.SendMove(dir, sprint);
        }

        private static IntentDirection ToIntent(Vector2 move)
        {
            if (move == Vector2.zero) return IntentDirection.None;

            if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
                return move.x > 0 ? IntentDirection.East : IntentDirection.West;
            return move.y > 0 ? IntentDirection.North : IntentDirection.South;
        }

        private void OnSnapshot(WorldSnapshot snap)
        {
            if (snap.Entities == null) return;

            float now = Time.time;
            for (int i = 0; i < snap.Entities.Length; i++)
            {
                var e = snap.Entities[i];
                if (!_views.TryGetValue(e.NetId, out var view))
                {
                    view = Instantiate(_entityViewPrefab, transform);
                    view.Init(e.NetId);
                    _views.Add(e.NetId, view);
                }
                view.Receive(e, now);
            }
            // TODO (этап 2): удаление вью для сущностей, пропавших из снапшота/PVS.
        }
    }
}