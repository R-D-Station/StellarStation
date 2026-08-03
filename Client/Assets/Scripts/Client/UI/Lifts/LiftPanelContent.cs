using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;
using Client.Lifts;
using Client.Net;
using Client.UI.Windows;

namespace Client.UI.Lifts
{
    /// <summary>Содержимое окна панели: кнопки этажей из реестра + текущий этаж/направление/фаза.
    /// Всё, кроме заказов, ВЫЧИСЛЯЕТСЯ из LiftSync+реестра — байт сверх бампа не нужно.</summary>
    public sealed class LiftPanelContent : MonoBehaviour
    {
        [SerializeField] private LiftFloorButtonHud[] _buttons;
        [SerializeField] private TMP_Text _status;

        private static readonly string[] PhaseText =
        {
            "Едет", "Стоянка", "Двери закрываются", "Готов"
        };

        private readonly Dictionary<int, uint> _pendingUntilTick = new Dictionary<int, uint>();
        private int _liftId = -1;
        private LiftClientState _state;
        private NetworkRunner _runner;
        private string _lastStatus;

        private void Awake()
        {
            if (_buttons != null)
                for (int i = 0; i < _buttons.Length; i++)
                    if (_buttons[i] != null) _buttons[i].Clicked += OnFloorClicked;
        }

        private void OnDestroy()
        {
            if (_buttons != null)
                for (int i = 0; i < _buttons.Length; i++)
                    if (_buttons[i] != null) _buttons[i].Clicked -= OnFloorClicked;
        }

        public void Bind(int liftId, LiftClientState state, NetworkRunner runner)
        {
            _liftId = liftId;
            _state = state;
            _runner = runner;
            _pendingUntilTick.Clear();
            _lastStatus = null;

            var stops = state != null && state.TryGetEntry(liftId, out var entry) && entry.Stops != null
                ? entry.Stops
                : System.Array.Empty<LiftStopEntry>();

            if (_buttons == null)
                return;

            // Кнопка есть ⇔ этаж обслуживается; лишние кнопки префаба гасим (приём ContainerWindowContent).
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] == null) continue;
                bool used = i < stops.Length;
                _buttons[i].gameObject.SetActive(used);
                if (used) _buttons[i].Bind(stops[i].Floor,
                    LiftDisplayText.Floor(LiftStopTable.DisplayIndex(stops, stops[i].Floor)));
            }

            if (stops.Length > _buttons.Length)
                Debug.LogWarning($"[LiftPanelContent] в префабе {_buttons.Length} кнопок < остановок {stops.Length}");

            var window = GetComponent<UiWindow>();
            if (window != null) window.SetTitle($"Лифт {liftId}");
        }

        private void OnFloorClicked(int floor)
        {
            if (_runner == null || floor < 0) return;
            _runner.SendLiftFloor(_liftId, (byte)floor);
            // Локальный pending: LiftSync идёт ReliableOrdered, снапшот — Sequenced, поэтому эхо Calls может
            // прийти ПОЗЖЕ снапшота более позднего тика. Гасим pending по своему же эху, а не по времени кадра.
            _pendingUntilTick[floor] = _runner.PredictedTick + 90u;
        }

        private void Update()
        {
            if (_state == null || _liftId < 0 || !_state.TryGetEntry(_liftId, out var entry))
                return;

            uint tick = _runner != null ? _runner.PredictedTick : 0u;
            uint calls = _state.CallsOf(_liftId);

            if (_buttons != null)
                for (int i = 0; i < _buttons.Length; i++)
                {
                    var b = _buttons[i];
                    if (b == null || !b.gameObject.activeSelf || b.Floor < 0) continue;
                    bool called = LiftScanQueue.Has(calls, b.Floor);
                    bool pending = !called && _pendingUntilTick.TryGetValue(b.Floor, out uint until) && tick < until;
                    if (called) _pendingUntilTick.Remove(b.Floor);
                    b.SetState(called, pending);
                }

            if (_status == null || !_state.TryGetPlan(_liftId, out var plan))
                return;

            float cabinY = _state.CabinYAt(_liftId, tick);
            int floorNow = LiftStopTable.DisplayIndex(entry.Stops, LiftStopTable.FloorAtOrBelow(entry.Stops, cabinY));
            var phase = LiftPhase.At(in plan, tick, entry.DoorLeadTicks);
            int dir = LiftPhase.Direction(in plan.Segment);
            string arrow = phase == LiftPhaseKind.Travel ? (dir > 0 ? " ^" : dir < 0 ? " v" : "") : "";
            string line = $"Этаж {floorNow + 1}{arrow} — {PhaseText[(int)phase]}";

            if (!string.Equals(line, _lastStatus))
            {
                _lastStatus = line;
                _status.text = line;
            }
        }
    }
}
