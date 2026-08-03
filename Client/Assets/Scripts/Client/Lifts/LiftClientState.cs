using System.Collections.Generic;
using UnityEngine;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Client.Lifts
{
    /// <summary>Клиентское состояние лифтов: сегменты, реестр, боксы препятствий и визуал кабины.
    /// Вынесено из NetworkRunner — тот и так god-класс, а панель с дисплеями требуют тех же данных.</summary>
    public sealed class LiftClientState
    {
        private readonly Dictionary<int, LiftSegment> _segments = new Dictionary<int, LiftSegment>();
        private readonly Dictionary<int, LiftRegistryEntry> _geometry = new Dictionary<int, LiftRegistryEntry>();
        private readonly Dictionary<int, LiftCabinVisual> _visuals = new Dictionary<int, LiftCabinVisual>();
        private readonly Dictionary<int, LiftRailVisual> _rails = new Dictionary<int, LiftRailVisual>();
        private readonly List<int> _staleVisuals = new List<int>();
        private readonly Dictionary<int, uint> _dwellUntil = new Dictionary<int, uint>();
        private readonly Dictionary<int, uint> _calls = new Dictionary<int, uint>();

        public IReadOnlyDictionary<int, LiftRegistryEntry> Geometry => _geometry;

        public void ApplySync(in LiftSync sync)
        {
            _segments[sync.LiftId] = new LiftSegment(sync.FromY, sync.ToY, sync.StartTick, sync.BlocksPerTick);
            _dwellUntil[sync.LiftId] = sync.DwellUntilTick;
            _calls[sync.LiftId] = sync.Calls;
        }

        public void ApplyRegistry(in LiftRegistry registry)
        {
            _geometry.Clear();
            var lifts = registry.Lifts;
            if (lifts == null)
                return;
            for (int i = 0; i < lifts.Length; i++)
                _geometry[lifts[i].LiftId] = lifts[i];
        }

        public bool TryGetEntry(int liftId, out LiftRegistryEntry entry) => _geometry.TryGetValue(liftId, out entry);

        public uint CallsOf(int liftId) => _calls.TryGetValue(liftId, out uint c) ? c : 0u;

        /// <summary>План лифта для LiftPhase.At: сегмент из LiftSync + DwellUntilTick оттуда же.</summary>
        public bool TryGetPlan(int liftId, out LiftPlan plan)
        {
            plan = default;
            if (!_segments.TryGetValue(liftId, out var seg))
                return false;
            plan.Segment = seg;
            plan.DwellUntilTick = _dwellUntil.TryGetValue(liftId, out uint d) ? d : 0u;
            return true;
        }

        public float CabinYAt(int liftId, uint tick)
            => _segments.TryGetValue(liftId, out var seg) ? LiftTrajectory.YAt(in seg, tick) : 0f;

        // Контракт лифтов (зеркало сервера): Y(tick) → боксы В НОВОЙ позиции с DeltaY → Step; тик — тот, что симулируется.
        // Геометрия — из LiftRegistry: без реестра лифт НЕ строится (лучше отсутствие боксов, чем боксы не там).
        public void AddObstacles(uint tick, DynamicObstacleSet target)
        {
            foreach (var kv in _segments)
            {
                if (!_geometry.TryGetValue(kv.Key, out var geom) || geom.Boxes == null)
                    continue;
                var seg = kv.Value;
                float y = LiftTrajectory.YAt(in seg, tick);
                float delta = LiftTrajectory.DeltaAt(in seg, tick);
                for (int b = 0; b < geom.Boxes.Length; b++)
                {
                    var box = geom.Boxes[b];
                    target.Add(geom.AnchorX + box.CenterX, y + box.Y, geom.AnchorZ + box.CenterZ,
                        box.HalfX, box.HalfZ, box.Height, delta);
                }
            }
        }

        /// <summary>На каком лифте едет игрок (предикат LiftRide — общий с сервером); -1 — ни на каком.</summary>
        public int FindRiding(float px, float py, float pz, uint tick)
        {
            foreach (var kv in _segments)
            {
                if (!_geometry.TryGetValue(kv.Key, out var geom) || geom.Boxes == null)
                    continue;
                var seg = kv.Value;
                float y = LiftTrajectory.YAt(in seg, tick);
                for (int b = 0; b < geom.Boxes.Length; b++)
                {
                    var box = geom.Boxes[b];
                    if (LiftRide.StandsOn(px, py, pz,
                            geom.AnchorX + box.CenterX, geom.AnchorZ + box.CenterZ,
                            box.HalfX, box.HalfZ, y + box.Y + box.Height))
                        return kv.Key;
                }
            }
            return -1;
        }

        public void PushDisplays(uint tick)
        {
            var registry = LiftDoorDisplayHub.Registry;
            foreach (var kv in _geometry)
            {
                var entry = kv.Value;
                if (entry.Stops == null || entry.Stops.Length == 0)
                    continue;

                bool hasPlan = TryGetPlan(entry.LiftId, out var plan);
                var phase = hasPlan ? LiftPhase.At(in plan, tick, entry.DoorLeadTicks) : LiftPhaseKind.Ready;
                var lift = LiftDoorDisplayState.ForLift(entry.Stops, CabinYAt(entry.LiftId, tick),
                    phase, in plan.Segment, hasPlan);
                uint calls = CallsOf(entry.LiftId);

                for (int s = 0; s < entry.Stops.Length; s++)
                {
                    var stop = entry.Stops[s];
                    if (!stop.HasDoor)
                        continue;
                    registry.Push(BlockGrid.PackCell(stop.DoorX, stop.DoorY, stop.DoorZ),
                        lift.WithCall(calls, stop.Floor));
                }
            }
        }

        // Высота кабины между тиками. Интерполяция идёт по ТОЙ ЖЕ временной базе, что и тик-луп, а не
        // экспофильтром: экспофильтр, догоняющий линейную рампу скорости v, держит УСТАНОВИВШУЮСЯ ошибку v/k
        // (при v=3 бл/сек и k=20 это 0.15 блока ВСЮ поездку). Скрытого состояния тут нет — позиция остаётся
        // чистой функцией (сегмент, тик, доля тика), поэтому «прилёта из нуля» после телепорта не будет.
        public float VisualY(int liftId, float renderTick)
        {
            if (!_segments.TryGetValue(liftId, out var seg))
                return 0f;
            return LiftMotion.VisualY(in seg, renderTick);
        }

        public void UpdateVisuals(uint displayTick, float renderTick, bool debugBoxes,
            Client.Map.BlockRenderer blocks)
        {
            PushDisplays(displayTick);

            var grid = blocks != null ? blocks.ContextFor(0, 0, 0, 0).Grid : null;

            foreach (var kv in _segments)
            {
                if (!_geometry.TryGetValue(kv.Key, out var geom) || geom.Boxes == null || geom.Boxes.Length == 0)
                    continue;

                if (!_visuals.TryGetValue(kv.Key, out var visual))
                {
                    visual = new LiftCabinVisual(kv.Key);
                    _visuals[kv.Key] = visual;
                }

                float y = VisualY(kv.Key, renderTick);
                float visualHeight = VisualHeight(in geom);
                float alpha = blocks != null
                    ? blocks.MovingObjectAlpha(geom.AnchorX, geom.AnchorZ, geom.PlanW, geom.PlanD,
                        y, visualHeight, LiftVisibilityGate.MaxRows)
                    : 1f;
                visual.Sync(in geom, y, debugBoxes, alpha, blocks, CabinDoorOpen(in geom, kv.Key, displayTick, grid));
            }

            UpdateRails(blocks);

            _staleVisuals.Clear();
            foreach (var kv in _visuals)
                if (!_segments.ContainsKey(kv.Key) || !_geometry.ContainsKey(kv.Key))
                    _staleVisuals.Add(kv.Key);
            for (int i = 0; i < _staleVisuals.Count; i++)
            {
                _visuals[_staleVisuals[i]].Destroy();
                _visuals.Remove(_staleVisuals[i]);
            }

            _staleVisuals.Clear();
            foreach (var kv in _rails)
                if (!_geometry.ContainsKey(kv.Key))
                    _staleVisuals.Add(kv.Key);
            for (int i = 0; i < _staleVisuals.Count; i++)
            {
                _rails[_staleVisuals[i]].Destroy();
                _rails.Remove(_staleVisuals[i]);
            }
        }

        // Рельсы идут по _geometry, а не по _segments: модули шахты существуют и до первого LiftSync.
        // Позы статичны и ставятся один раз при пересборке; покадрово обновляется только альфа среза.
        private void UpdateRails(Client.Map.BlockRenderer blocks)
        {
            foreach (var kv in _geometry)
            {
                var geom = kv.Value;
                if (geom.RailDefId == 0 || geom.FloorCount < 1)
                    continue;

                if (!_rails.TryGetValue(kv.Key, out var rail))
                {
                    rail = new LiftRailVisual(kv.Key);
                    _rails[kv.Key] = rail;
                }

                float alpha = blocks != null
                    ? blocks.MovingObjectAlpha(geom.AnchorX, geom.AnchorZ, geom.PlanW, geom.PlanD,
                        geom.FloorYAt(0), RailHeight(in geom), LiftVisibilityGate.MaxRows)
                    : 1f;
                rail.Sync(in geom, alpha, blocks);
            }
        }

        // Створки кабины зеркалят АВТОРИТЕТНЫЙ бит Open шахтной двери из грида, а не фазу лифта:
        // сервер открывает дверь только по заказу и молча пропускает этаж после MaxOpenAttempts,
        // поэтому «стоим на этаже» бывает и при закрытой двери. Позиция берётся на СИМ-тике,
        // а не на визуальном: решение о состоянии не должно зависеть от часов отрисовки.
        private bool CabinDoorOpen(in LiftRegistryEntry geom, int liftId, uint displayTick, BlockGrid grid)
        {
            if (grid == null || geom.Stops == null)
                return false;

            bool found = LiftCabinDoorState.TryStopAtY(geom.Stops, CabinYAt(liftId, displayTick),
                LiftCabinDoorState.DefaultEps, out var stop);
            if (!found)
                return false;

            var phase = TryGetPlan(liftId, out var plan)
                ? LiftPhase.At(in plan, displayTick, geom.DoorLeadTicks)
                : LiftPhaseKind.Ready;
            bool openBit = BlockState.GetOpen(grid.GetState(stop.DoorX, stop.DoorY, stop.DoorZ));
            return LiftCabinDoorState.ShouldOpen(phase, true, openBit);
        }

        private static float RailHeight(in LiftRegistryEntry geom)
        {
            byte moduleY = BlockCatalog.Get(geom.RailDefId).Lift.ModuleY;
            return moduleY > 0 ? moduleY : geom.FloorStep;
        }

        // Высота ВИЗУАЛА, а не коллизии: у боевой кабины бокс — плита пола толщиной 0.5, и гейт по ней
        // опрашивал бы ряд НИЖЕ ног пассажира. Габарит модели знает кодоген (ModuleY), провод для этого
        // не нужен. Кодоген, а не поле SO: на неперегенерированном каталоге SO дал бы другой сигнал.
        private static float VisualHeight(in LiftRegistryEntry geom)
        {
            byte moduleY = BlockCatalog.Get(geom.CabinDefId).Lift.ModuleY;
            return moduleY > 0 ? moduleY : BoxTop(in geom);
        }

        private static float BoxTop(in LiftRegistryEntry geom)
        {
            float top = 0f;
            for (int i = 0; i < geom.Boxes.Length; i++)
            {
                float t = geom.Boxes[i].Y + geom.Boxes[i].Height;
                if (t > top)
                    top = t;
            }
            return top;
        }
    }
}
