using System;
using System.Collections.Generic;
using Server.Doors;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Server.Lifts
{
    public sealed class LiftSystem
    {
        private readonly BlockGrid _world;
        private readonly DoorSystem _doors;
        private readonly SVars _config;
        private readonly List<LiftController> _controllers = new();
        private readonly List<LiftRuntime> _runtimes = new();
        private readonly List<LiftController> _changed = new();
        private int[] _skipReported = Array.Empty<int>();

        public LiftScanResult Result { get; private set; } = new LiftScanResult();
        public int NormalizedDoors { get; private set; }
        public IReadOnlyList<LiftController> Controllers => _controllers;
        public IReadOnlyList<LiftRuntime> Runtimes => _runtimes;
        public IReadOnlyList<LiftController> Changed => _changed;

        public LiftSystem(BlockGrid world, DoorSystem doors, SVars config = null)
        {
            _world = world;
            _doors = doors;
            _config = config;
        }

        public LiftScanResult Build()
        {
            Result = LiftShaftScanner.Scan(_world, _doors?.Registry);
            NormalizedDoors = CloseAllExternalDoors();
            InjectDebugShaft();
            BuildControllers();
            return Result;
        }

        private int CloseAllExternalDoors()
        {
            if (_doors == null)
                return 0;

            var keys = new List<long>();
            foreach (var record in _doors.Registry.Records)
            {
                if (!record.Open)
                    continue;
                var info = BlockCatalog.Get(record.Type);
                if (info.Openable && info.Opening == DoorOpening.External)
                    keys.Add(BlockGrid.PackCell(record.Ax, record.Ay, record.Az));
            }

            int closed = 0;
            foreach (long key in keys)
                if (_doors.TrySetOpen(key, false) == DoorCommandResult.Applied)
                    closed++;
            return closed;
        }

        private void InjectDebugShaft()
        {
            if (_config == null || !_config.DebugLiftEnabled)
                return;
            int step = (int)MathF.Round(_config.DebugLiftToY - _config.DebugLiftFromY);
            if (step < 1 || _config.DebugLiftSpeed <= 0f)
                return;

            int railX = (int)MathF.Floor(_config.DebugLiftX);
            int railZ = (int)MathF.Floor(_config.DebugLiftZ);
            float halfW = _config.DebugLiftHalfW;
            float cornerX = _config.DebugLiftX - railX;
            float cornerZ = _config.DebugLiftZ - railZ;
            var spec = new LiftShaftSpec
            {
                RailX = railX,
                RailZ = railZ,
                BaseY = (int)MathF.Round(_config.DebugLiftFromY),
                FloorStep = step,
                FloorCount = 2,
                ModuleX = 1, ModuleY = 1, ModuleZ = 1,
                CabinY = (int)MathF.Round(_config.DebugLiftFromY),
                AnchorX = railX,
                AnchorZ = railZ,
                PlanX0 = railX, PlanX1 = railX, PlanZ0 = railZ, PlanZ1 = railZ,
                SpeedBlocksPerSec = _config.DebugLiftSpeed * _config.TickRate,
                DwellSec = _config.DebugLiftPauseTicks / (float)Math.Max(1, _config.TickRate),
                DoorLeadSec = 0f,
                CabinBoxes = new[]
                {
                    new TriggerBox(
                        (short)MathF.Round((cornerX - halfW) * 16f), 0,
                        (short)MathF.Round((cornerZ - halfW) * 16f),
                        (short)MathF.Round((cornerX + halfW) * 16f),
                        (short)MathF.Round(_config.DebugLiftHeight * 16f),
                        (short)MathF.Round((cornerZ + halfW) * 16f))
                }
            };
            spec.Stops.Add(new LiftStopSpec { FloorIndex = 0, FloorY = spec.BaseY });
            spec.Stops.Add(new LiftStopSpec { FloorIndex = 1, FloorY = spec.BaseY + step });
            Result.Shafts.Add(spec);

            Console.WriteLine($"[Lifts] стенд из config.json: синтетическая шахта у ({spec.AnchorX}, {spec.AnchorZ}), " +
                              $"Y {spec.BaseY}..{spec.BaseY + step}, без дверей (путь «этаж без двери»)");
        }

        private void BuildControllers()
        {
            _controllers.Clear();
            _runtimes.Clear();
            int tickRate = Math.Max(1, _config?.TickRate ?? 30);

            for (int i = 0; i < Result.Shafts.Count; i++)
            {
                var shaft = Result.Shafts[i];
                shaft.LiftId = i + 1;
                int floors = Math.Min(shaft.FloorCount, LiftScanQueue.MaxFloors);
                if (floors <= 0)
                    continue;

                var stopY = new float[floors];
                var stopDoor = new long[floors];
                var served = new bool[floors];
                for (int k = 0; k < floors; k++)
                    stopY[k] = shaft.FloorYAt(k);
                foreach (var stop in shaft.Stops)
                {
                    if ((uint)stop.FloorIndex >= (uint)floors)
                        continue;
                    served[stop.FloorIndex] = true;
                    if (stop.HasDoor)
                        stopDoor[stop.FloorIndex] = stop.DoorKey;
                }

                float blocksPerTick = shaft.SpeedBlocksPerSec / tickRate;
                var runtime = new LiftRuntime(shaft.AnchorX, shaft.AnchorZ, ToBoxes(shaft),
                    new LiftSegment(shaft.CabinY, shaft.CabinY, 0u, blocksPerTick), shaft.LiftId);

                _controllers.Add(new LiftController(shaft.LiftId, runtime, stopY, stopDoor, served,
                    (uint)MathF.Round(shaft.DwellSec * tickRate),
                    (uint)MathF.Round(shaft.DoorLeadSec * tickRate),
                    (uint)Math.Max(1, tickRate / 2), blocksPerTick, _doors));
                _runtimes.Add(runtime);
            }

            if (_skipReported.Length < _controllers.Count)
                _skipReported = new int[_controllers.Count];
        }

        public static LiftBox[] ToBoxes(LiftShaftSpec shaft)
            => LiftCabinPlacement.LocalBoxes(shaft.ModuleX, shaft.ModuleZ, shaft.Facing, shaft.CabinBoxes);

        public void Tick(uint tick)
        {
            _changed.Clear();
            for (int i = 0; i < _controllers.Count; i++)
            {
                var controller = _controllers[i];
                if (controller.Decide(tick))
                    _changed.Add(controller);
                if (controller.SkippedFloors != _skipReported[i])
                {
                    _skipReported[i] = controller.SkippedFloors;
                    Console.WriteLine($"[Lifts] лифт {controller.LiftId}: этаж {controller.LastSkippedFloor} пропущен — " +
                                      $"дверь не открылась за {LiftController.MaxOpenAttempts} попыток");
                }
            }
        }

        public LiftController FindByDoorKey(long key, out int floor)
        {
            floor = -1;
            for (int i = 0; i < Result.Shafts.Count && i < _controllers.Count; i++)
            {
                var shaft = Result.Shafts[i];
                foreach (var stop in shaft.Stops)
                    if (stop.HasDoor && stop.DoorKey == key)
                    {
                        floor = stop.FloorIndex;
                        return _controllers[i];
                    }
            }
            return null;
        }

        public void PrintReport()
        {
            LiftScanReport.Print(Result);
            if (NormalizedDoors > 0)
                Console.WriteLine($"[Lifts] шахтных дверей нормализовано в закрытые: {NormalizedDoors}");
        }
    }
}
