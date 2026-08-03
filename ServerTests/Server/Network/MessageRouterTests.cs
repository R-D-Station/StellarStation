using System;
using Shared.Messages;
using Server.Network;
using Server.Network.Messages;

namespace ServerTests.Server.Network
{
    /// <summary>Страхует полноту таблицы MessageRouter: новый клиент→сервер тип обязан попасть в ClientToServerTypes.</summary>
    public class MessageRouterTests
    {
        // держать в синхроне с MessageRouter.CreateDefault() — источник регрессии "забыли зарегистрировать хендлер"
        private static readonly MessageType[] ClientToServerTypes =
        {
            MessageType.MoveIntent,
            MessageType.UseIntent,
            MessageType.InteractIntent,
            MessageType.PickupItem,
            MessageType.DropItem,
            MessageType.SwapHand,
            MessageType.MoveSlot,
            MessageType.OpenContainer,
            MessageType.CloseContainer,
            MessageType.PutInContainer,
            MessageType.TakeFromContainer,
            MessageType.PullItem,
            MessageType.LiftFloorRequest,
        };

        [Fact]
        public void CreateDefault_HasHandlerForEveryClientToServerType()
        {
            var router = MessageRouter.CreateDefault();
            foreach (var type in ClientToServerTypes)
            {
                var handler = router.Resolve((ushort)type);
                Assert.NotNull(handler);
                Assert.Equal(type, handler!.Type);
            }
        }

        [Fact]
        public void Resolve_ServerToClientAndUnknownIds_ReturnNull()
        {
            var router = MessageRouter.CreateDefault();
            Assert.Null(router.Resolve((ushort)MessageType.WorldSnapshot));
            Assert.Null(router.Resolve((ushort)MessageType.InventorySync));
            Assert.Null(router.Resolve(9999));
        }

        [Fact]
        public void Dispatch_BatteredMoveIntentFrame_LogsAndSurvives()
        {
            var router = MessageRouter.CreateDefault();
            var client = new ClientConnection(null!, 1);

            var ex = Record.Exception(() => router.Dispatch(client, (ushort)MessageType.MoveIntent, new byte[] { 1, 2, 3 }));

            Assert.Null(ex);
            Assert.True(client.IntentQueue.IsEmpty);
        }

        [Fact]
        public void Dispatch_ValidMoveIntent_Enqueues()
        {
            var router = MessageRouter.CreateDefault();
            var client = new ClientConnection(null!, 1);
            var intent = new global::Shared.Messages.Core.MoveIntent
            {
                Direction = global::Shared.Messages.Core.IntentDirection.North,
                Sprint = false,
                Sequence = 5
            };

            router.Dispatch(client, (ushort)MessageType.MoveIntent, intent.Serialize());

            Assert.True(client.IntentQueue.TryDequeue(out var queued));
            Assert.Equal(5u, queued.Sequence);
        }

        [Fact]
        public void Dispatch_UnknownId_NoThrow()
        {
            var router = MessageRouter.CreateDefault();
            var client = new ClientConnection(null!, 1);

            var ex = Record.Exception(() => router.Dispatch(client, 9999, Array.Empty<byte>()));

            Assert.Null(ex);
        }

        [Fact]
        public void Dispatch_UseIntent_SetsFlag()
        {
            var router = MessageRouter.CreateDefault();
            var client = new ClientConnection(null!, 1);

            router.Dispatch(client, (ushort)MessageType.UseIntent, Array.Empty<byte>());

            Assert.True(client.UseRequested);
        }
    }
}
