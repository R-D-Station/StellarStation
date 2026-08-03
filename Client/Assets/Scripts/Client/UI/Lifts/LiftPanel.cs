using UnityEngine;
using Client.Net;
using Client.UI.Windows;

namespace Client.UI.Lifts
{
    /// <summary>Сервис сцены: окно панели этажей в кабине (роль-зеркало ContainerWindows).
    /// Гейт — ТОЛЬКО LiftRide: InteractionRules.InReachBlocks считает чебышев по ЦЕЛЫМ уровням против дробного Y
    /// едущей кабины, а клиентский IsGroundContainerReachable ключуется NetId предмета и на неизвестном id
    /// возвращает true («не закрывать никогда»).</summary>
    public sealed class LiftPanel : MonoBehaviour
    {
        [Tooltip("Сколько секунд подряд игрок обязан быть ВНЕ кабины, чтобы окно закрылось само.")]
        [SerializeField] private float _exitGrace = 0.25f;
        [SerializeField] private UiWindowManager _manager;
        [SerializeField] private UiWindow _windowPrefab;
        [SerializeField] private NetworkRunner _runner;

        private UiWindow _window;
        private LiftPanelContent _content;
        private int _liftId = -1;
        private float _outsideSince = -1f;
        private bool _suppressedUntilNextUse;

        public bool IsOpen => _window != null;

        /// <summary>Текущий лифт под игроком; -1 — не в кабине.</summary>
        public int RidingLiftId()
        {
            if (_runner == null || !_runner.IsPredictorInitialized) return -1;
            return _runner.Lifts.FindRiding(_runner.PredictedPlanX, _runner.PredictedHeight, _runner.PredictedPlanZ,
                _runner.PredictedTick);
        }

        /// <summary>E в кабине: открыть/закрыть. Возвращает true, если клик поглощён панелью.</summary>
        public bool Toggle()
        {
            if (IsOpen) { Close(); return true; }

            int liftId = RidingLiftId();
            if (liftId < 0) return false;
            if (_manager == null || _windowPrefab == null || _runner == null) return false;

            var w = _manager.Open(_windowPrefab);
            if (w == null) return false;

            var c = w.GetComponent<LiftPanelContent>();
            if (c == null) { _manager.Close(w); return false; }

            c.Bind(liftId, _runner.Lifts, _runner);
            w.CloseRequested += _ => Close();

            _window = w;
            _content = c;
            _liftId = liftId;
            _outsideSince = -1f;
            _suppressedUntilNextUse = false;
            return true;
        }

        public void Close()
        {
            if (_window == null) return;
            _manager?.Close(_window);
            _window = null;
            _content = null;
            _liftId = -1;
            _outsideSince = -1f;
            // Ручное закрытие крестиком/повторным E запрещает АВТО-открытие до следующего явного E,
            // иначе крестик визуально «не работает».
            _suppressedUntilNextUse = true;
        }

        /// <summary>Снимает запрет авто-открытия — зовётся на явном нажатии E.</summary>
        public void ClearSuppression() => _suppressedUntilNextUse = false;

        public bool IsSuppressed => _suppressedUntilNextUse;

        private void Update()
        {
            if (_window == null)
            {
                if (RidingLiftId() < 0) _suppressedUntilNextUse = false; // вышел из кабины — запрет снят
                return;
            }

            int liftId = RidingLiftId();
            if (liftId == _liftId)
            {
                _outsideSince = -1f;
                return;
            }

            // Гистерезис: одиночный false переживают апекс прыжка и запоздавший LiftSync.
            if (_outsideSince < 0f) _outsideSince = Time.unscaledTime;
            if (Time.unscaledTime - _outsideSince >= _exitGrace)
                Close();
        }
    }
}
