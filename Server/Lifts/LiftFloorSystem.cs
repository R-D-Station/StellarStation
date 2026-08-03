using System.Collections.Generic;
using LiteNetLib;
using Server.Network;
using Shared.Simulation.Blocks;

namespace Server.Lifts
{
    public sealed class LiftFloorSystem
    {
        public const int MaxRequestsPerTick = 32;

        private readonly LiftSystem _lifts;
        private readonly Dictionary<NetPeer, ClientConnection> _clients;

        public int RejectedNotRiding { get; private set; }
        public int Applied { get; private set; }
        public int Dropped { get; private set; }

        public static void OnClientDisconnect(ClientConnection client)
        {
            if (client == null)
                return;
            while (client.LiftFloorQueue.TryDequeue(out _)) { }
        }

        public LiftFloorSystem(LiftSystem lifts, Dictionary<NetPeer, ClientConnection> clients)
        {
            _lifts = lifts;
            _clients = clients;
        }

        public void Process(uint tick)
        {
            if (_clients == null)
                return;

            foreach (var client in _clients.Values)
            {
                int applied = 0;
                while (applied < MaxRequestsPerTick && client.LiftFloorQueue.TryDequeue(out var request))
                {
                    applied++;

                    // Сначала контроллер ПО LiftId из запроса и только ЕГО боксы через LiftRide:
                    // стоимость O(боксы одного лифта), а не обход всех шахт.
                    var controller = FindById(request.LiftId);
                    if (controller == null)
                        continue;

                    if (!IsRiding(client, controller, tick))
                    {
                        RejectedNotRiding++; // штатная гонка «вышел, пока летел пакет», не ошибка
                        continue;
                    }

                    if (controller.Request(request.Floor))
                        Applied++;
                }

                // Хвост сбрасываем, как ProcessIntents/ProcessInteractions: предохранитель ограничивает
                // ОБРАБОТКУ, а очередь без слива росла бы неограниченно под флудом.
                if (applied == MaxRequestsPerTick && !client.LiftFloorQueue.IsEmpty)
                {
                    int dropped = 0;
                    while (client.LiftFloorQueue.TryDequeue(out _)) dropped++;
                    Dropped += dropped;
                    Console.WriteLine($"[Server] lift floor flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
                }
            }
        }

        private LiftController FindById(int liftId)
        {
            var controllers = _lifts?.Controllers;
            if (controllers == null)
                return null;
            for (int i = 0; i < controllers.Count; i++)
                if (controllers[i].LiftId == liftId)
                    return controllers[i];
            return null;
        }

        public static bool IsRiding(ClientConnection client, LiftController controller, uint tick)
        {
            if (client == null || controller == null || client.PlayerNetId == 0)
                return false;

            var runtime = controller.Runtime;
            float y = runtime.YAt(tick);
            var boxes = runtime.CabinBoxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (LiftRide.StandsOn(client.Mover.X, client.Mover.Y, client.Mover.Z,
                        runtime.AnchorX + box.CenterX, runtime.AnchorZ + box.CenterZ,
                        box.HalfX, box.HalfZ, y + box.Y + box.Height))
                    return true;
            }
            return false;
        }
    }
}
