using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Core;
using Shared.Simulation.Blocks;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    public class ContainerLidTests : IDisposable
    {
        private const ushort CrateDef = 90;
        private const ushort WhiteCrateDef = 91;
        private const ushort BigCrateDef = 92;
        private const ushort AllowedDef = 60;
        private const ushort OtherDef = 61;

        private readonly SVars _config;
        private GameServer? _server;
        private readonly int _testPort;
        private static int _portCounter = 9100;
        private static readonly object _portLock = new object();

        public ContainerLidTests()
        {
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 9300) _portCounter = 9100;
            }

            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = _testPort,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = $"LidTest_{_testPort}"
            };
        }

        public void Dispose()
        {
            _server?.Stop();
            Thread.Sleep(50);
        }

        private GameServer StartServer(GridMap? map = null)
        {
            _server = new GameServer(_config, map);
            _server.Start();
            Thread.Sleep(50);
            _server.ProtoLookup = defId => defId switch
            {
                CrateDef => new ItemProto(CrateDef, SlotCategory.None, false, 1, 1, true, 4, containerMode: ContainerMode.SS14, suckRadius: 2f),
                WhiteCrateDef => new ItemProto(WhiteCrateDef, SlotCategory.None, false, 1, 1, true, 4,
                    filterMode: ContainerFilterMode.Whitelist, filterItemIds: new ushort[] { AllowedDef },
                    containerMode: ContainerMode.SS14, suckRadius: 2f),
                BigCrateDef => new ItemProto(BigCrateDef, SlotCategory.None, false, 1, 1, true, 25,
                    containerMode: ContainerMode.SS14, suckRadius: 3f),
                50 => new ItemProto(50, SlotCategory.None, false, 1, 1, true, 4),
                _ => new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0)
            };
            return _server;
        }

        private NetPeer CreateConnectedPeer()
        {
            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();

            NetPeer? connectedPeer = null;
            bool connected = false;
            clientListener.PeerConnectedEvent += peer => { connectedPeer = peer; connected = true; };

            clientManager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);
            for (int i = 0; i < 100 && !connected; i++) { clientManager.PollEvents(); Thread.Sleep(10); }

            if (connectedPeer == null)
            {
                clientManager.Stop();
                throw new Exception($"Failed to connect to server on port {_testPort}");
            }
            return connectedPeer;
        }

        private static readonly FieldInfo MainThreadActionsField =
            typeof(GameServer).GetField("_mainThreadActions", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static void OnGameLoop(GameServer server, Action action)
        {
            var queue = (System.Collections.Concurrent.ConcurrentQueue<Action>)MainThreadActionsField.GetValue(server)!;
            Exception? error = null;
            bool completed = false;
            queue.Enqueue(() =>
            {
                try { action(); }
                catch (Exception e) { error = e; }
                finally { Volatile.Write(ref completed, true); }
            });
            for (int i = 0; i < 500 && !Volatile.Read(ref completed); i++) Thread.Sleep(10);
            if (!Volatile.Read(ref completed)) throw new TimeoutException("GameLoop did not run the queued action");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static object ContainersObj(GameServer s) =>
            typeof(GameServer).GetField("_containers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;

        private static void HandleOpen(GameServer s, ClientConnection client, int netId)
        {
            var obj = ContainersObj(s);
            var m = obj.GetType().GetMethod("HandleOpen", BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(obj, new object[] { client, netId });
        }

        private static bool IsWorldOpen(GameServer s, int netId)
        {
            var obj = ContainersObj(s);
            return (bool)obj.GetType().GetMethod("IsWorldOpen")!.Invoke(obj, new object[] { netId })!;
        }

        private static Dictionary<int, List<HeldItem>> ContentsOf(GameServer s)
        {
            var obj = ContainersObj(s);
            return (Dictionary<int, List<HeldItem>>)obj.GetType()
                .GetField("_contents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(30);
        }

        [Fact]
        public void Lid_TogglesWorldOpen()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
            });
            Assert.True(IsWorldOpen(server, crate));

            OnGameLoop(server, () => HandleOpen(server, client, crate));
            Assert.False(IsWorldOpen(server, crate));

            CleanupPeer(peer);
        }

        [Fact]
        public void Close_SucksItemsInRadius_DespawnsFromWorld_KeepsOutside()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0, near = 0, far = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
                near = server.SpawnGroundItem(OtherDef, 3, 5.5f, 6f, 0);
                far = server.SpawnGroundItem(OtherDef, 1, 5f, 9f, 0);
                HandleOpen(server, client, crate);
            });

            var contents = ContentsOf(server)[crate];
            Assert.Contains(contents, h => h.NetId == near && h.ItemDefId == OtherDef && h.StackCount == 3);
            Assert.DoesNotContain(contents, h => h.NetId == far);
            Assert.False(OnGameLoopFunc(server, () => server.DespawnGroundItem(near)));
            Assert.True(OnGameLoopFunc(server, () => server.DespawnGroundItem(far)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Suck_RespectsWhitelist()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0, allowed = 0, other = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(WhiteCrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
                allowed = server.SpawnGroundItem(AllowedDef, 1, 5.2f, 5.2f, 0);
                other = server.SpawnGroundItem(OtherDef, 1, 5.3f, 5.3f, 0);
                HandleOpen(server, client, crate);
            });

            var contents = ContentsOf(server)[crate];
            Assert.Contains(contents, h => h.NetId == allowed);
            Assert.DoesNotContain(contents, h => h.NetId == other);

            CleanupPeer(peer);
        }

        [Fact]
        public void Suck_CapsAtMaxContents()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
                for (int i = 0; i < 6; i++)
                    server.SpawnGroundItem(OtherDef, 1, 5f + i * 0.1f, 5f, 0);
                HandleOpen(server, client, crate);
            });

            Assert.Equal(4, ContentsOf(server)[crate].Count);

            CleanupPeer(peer);
        }

        [Fact]
        public void Open_SpillsContents_SameNetIds_ContentsEmptied()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                ContentsOf(server)[crate] = new List<HeldItem>
                {
                    new HeldItem { NetId = 7001, ItemDefId = OtherDef, StackCount = 1 },
                    new HeldItem { NetId = 7002, ItemDefId = OtherDef, StackCount = 2 }
                };
                HandleOpen(server, client, crate);
            });

            Assert.Empty(ContentsOf(server)[crate]);
            var ground = server.GroundItemsInInterest(5f, 5f, 0);
            Assert.Contains(ground, it => it.NetId == 7001);
            Assert.Contains(ground, it => it.NetId == 7002);

            CleanupPeer(peer);
        }

        [Fact]
        public void CloseOpen_RoundTrip_PreservesNetIds()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0, a = 0, b = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
                a = server.SpawnGroundItem(OtherDef, 1, 5.2f, 5.2f, 0);
                b = server.SpawnGroundItem(OtherDef, 1, 4.8f, 5.2f, 0);
                HandleOpen(server, client, crate);
                HandleOpen(server, client, crate);
            });

            var ground = server.GroundItemsInInterest(5f, 5f, 0);
            Assert.Contains(ground, it => it.NetId == a);
            Assert.Contains(ground, it => it.NetId == b);
            Assert.Empty(ContentsOf(server)[crate]);

            CleanupPeer(peer);
        }

        [Fact]
        public void UiContainer_StillUsesViewerPath()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(50, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
            });

            Assert.True(IsWorldOpen(server, crate));
            Assert.Contains(crate, client.OpenContainers);

            CleanupPeer(peer);
        }

        private static int LidOpenCount(GameServer s)
        {
            var obj = ContainersObj(s);
            var d = (System.Collections.IDictionary)obj.GetType()
                .GetField("_lidOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
            return d.Count;
        }

        private static void ProcessContainerOps(GameServer s)
        {
            var obj = ContainersObj(s);
            obj.GetType().GetMethod("ProcessContainerOps")!.Invoke(obj, null);
        }

        [Fact]
        public void ReDropSameNetId_ResetsLidToClosed()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
            });
            Assert.True(IsWorldOpen(server, crate));

            OnGameLoop(server, () =>
            {
                server.DespawnGroundItem(crate);
                server.SpawnGroundItemWithId(crate, CrateDef, 1, 5, 5, 0);
            });
            Assert.False(IsWorldOpen(server, crate));

            int spilledProbe = 0;
            OnGameLoop(server, () =>
            {
                spilledProbe = server.SpawnGroundItem(OtherDef, 1, 5.3f, 5.3f, 0);
                HandleOpen(server, client, crate);
            });
            Assert.True(IsWorldOpen(server, crate));
            Assert.True(OnGameLoopFunc(server, () => server.DespawnGroundItem(spilledProbe)));

            CleanupPeer(peer);
        }

        [Fact]
        public void DestroyedOpenLid_PrunedFromState()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
            });
            Assert.Equal(1, LidOpenCount(server));

            OnGameLoop(server, () =>
            {
                server.DespawnGroundItem(crate);
                ProcessContainerOps(server);
            });
            Assert.Equal(0, LidOpenCount(server));

            CleanupPeer(peer);
        }

        private static Dictionary<int, (float x, float y)> GroundByNet(GameServer s, float cx, float cy, int cz)
        {
            var d = new Dictionary<int, (float x, float y)>();
            foreach (var it in s.GroundItemsInInterest(cx, cy, cz))
                d[it.NetId] = (it.X, it.Y);
            return d;
        }

        private static float Dist((float x, float y) p, float cx, float cy)
        {
            float dx = p.x - cx, dy = p.y - cy;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        [Fact]
        public void Spill_Full25_MaxSlotItemAtCenter_RingsElse()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(BigCrateDef, 1, 5, 5, 0);
                var l = new List<HeldItem>();
                for (int i = 0; i < 25; i++)
                    l.Add(new HeldItem { NetId = 7001 + i, ItemDefId = OtherDef, StackCount = 1 });
                ContentsOf(server)[crate] = l;
                HandleOpen(server, client, crate);
            });

            var g = GroundByNet(server, 5f, 5f, 0);
            Assert.True(Dist(g[7025], 5f, 5f) < 0.05f);
            for (int i = 0; i < 8; i++)
                Assert.True(MathF.Abs(Dist(g[7001 + i], 5f, 5f) - 1.5f) < 0.03f);
            for (int i = 8; i < 24; i++)
                Assert.True(MathF.Abs(Dist(g[7001 + i], 5f, 5f) - 2.7f) < 0.03f);
            for (int i = 0; i < 25; i++)
                Assert.Contains(7001 + i, g.Keys);
            Assert.Empty(ContentsOf(server)[crate]);

            CleanupPeer(peer);
        }

        [Fact]
        public void Spill_NotFull_NoItemAtCenter()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(BigCrateDef, 1, 5, 5, 0);
                var l = new List<HeldItem>();
                for (int i = 0; i < 5; i++)
                    l.Add(new HeldItem { NetId = 7001 + i, ItemDefId = OtherDef, StackCount = 1 });
                ContentsOf(server)[crate] = l;
                HandleOpen(server, client, crate);
            });

            var g = GroundByNet(server, 5f, 5f, 0);
            for (int i = 0; i < 5; i++)
            {
                Assert.Contains(7001 + i, g.Keys);
                Assert.True(MathF.Abs(Dist(g[7001 + i], 5f, 5f) - 1.5f) < 0.03f);
            }

            CleanupPeer(peer);
        }

        [Fact]
        public void SuckSpill_RoundTrip25_NetIdsStable()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int crate = 0;
            var ids = new List<int>();
            OnGameLoop(server, () =>
            {
                crate = server.SpawnGroundItem(BigCrateDef, 1, 5, 5, 0);
                HandleOpen(server, client, crate);
                for (int gx = 0; gx < 5; gx++)
                    for (int gy = 0; gy < 5; gy++)
                        ids.Add(server.SpawnGroundItem(OtherDef, 1, 5f + (gx - 2) * 0.15f, 5f + (gy - 2) * 0.15f, 0));
                HandleOpen(server, client, crate);
            });

            Assert.Equal(25, ContentsOf(server)[crate].Count);

            OnGameLoop(server, () => HandleOpen(server, client, crate));

            var g = GroundByNet(server, 5f, 5f, 0);
            for (int i = 0; i < ids.Count; i++)
                Assert.Contains(ids[i], g.Keys);
            Assert.Empty(ContentsOf(server)[crate]);

            CleanupPeer(peer);
        }

        private static readonly FieldInfo ClientsField =
            typeof(GameServer).GetField("_clients", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static ClientConnection WaitServerClient(GameServer s)
        {
            for (int i = 0; i < 300; i++)
            {
                var d = (System.Collections.IDictionary)ClientsField.GetValue(s)!;
                foreach (var v in d.Values) return (ClientConnection)v;
                Thread.Sleep(10);
            }
            throw new Exception("server ClientConnection not registered");
        }

        private static List<ClientConnection> WaitServerClients(GameServer s, int n)
        {
            for (int i = 0; i < 400; i++)
            {
                var d = (System.Collections.IDictionary)ClientsField.GetValue(s)!;
                if (d.Count >= n)
                {
                    var list = new List<ClientConnection>();
                    foreach (var v in d.Values) list.Add((ClientConnection)v);
                    return list;
                }
                Thread.Sleep(10);
            }
            throw new Exception($"expected {n} server clients");
        }

        private static void OnCell(ClientConnection c, float x, float z, int y)
        {
            c.Mover = new BlockMoverState { X = x, Y = y, Z = z };
            c.X = x;
            c.Y = z;
            c.Z = y;
        }

        private static void InvokeApplyClientIntent(GameServer s, ClientConnection c, MoveIntent intent, bool hasIntent)
            => typeof(GameServer).GetMethod("ApplyClientIntent", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(s, new object[] { c, intent, hasIntent });

        private static void InvokeBroadcast(GameServer s)
            => typeof(GameServer).GetMethod("BroadcastWorldSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(s, null);

        private static bool BroadcastContainedFirst(GameServer s, int expectNetId)
        {
            var ents = (EntitySnapshot[])typeof(GameServer)
                .GetField("_broadcastEntities", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var cont = (bool[])typeof(GameServer)
                .GetField("_broadcastContained", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            return ents.Length > 0 && ents[0].NetId == expectNetId && cont[0];
        }

        [Fact]
        public void Contain_PlayerOnCell_OnClose()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var sc = WaitServerClient(server);

            int crate = 0;
            OnGameLoop(server, () =>
            {
                OnCell(sc, 5.5f, 5.5f, 0);
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, sc, crate);
                HandleOpen(server, sc, crate);
            });

            Assert.Equal(crate, sc.ContainedInNetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Contained_MoveIntentIgnored_SeqConsumed()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var sc = WaitServerClient(server);

            float mx = 0f, mz = 0f;
            uint seq = 0;
            OnGameLoop(server, () =>
            {
                OnCell(sc, 5.5f, 5.5f, 0);
                sc.ContainedInNetId = 4242;
                InvokeApplyClientIntent(server, sc, new MoveIntent { Direction = IntentDirection.East, Sequence = 7 }, true);
                mx = sc.Mover.X;
                mz = sc.Mover.Z;
                seq = sc.LastProcessedSequence;
            });

            Assert.Equal(5.5f, mx, 3);
            Assert.Equal(5.5f, mz, 3);
            Assert.Equal(7u, seq);

            CleanupPeer(peer);
        }

        [Fact]
        public void Contained_HiddenInBroadcastSet()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var sc = WaitServerClient(server);

            int crate = 0;
            bool flagged = false;
            OnGameLoop(server, () =>
            {
                OnCell(sc, 5.5f, 5.5f, 0);
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, sc, crate);
                HandleOpen(server, sc, crate);
                InvokeBroadcast(server);
                flagged = BroadcastContainedFirst(server, sc.PlayerNetId);
            });

            Assert.True(flagged);

            CleanupPeer(peer);
        }

        [Fact]
        public void Release_OnOpen_PlayerAdjacent()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var sc = WaitServerClient(server);

            int crate = 0;
            OnGameLoop(server, () =>
            {
                OnCell(sc, 5.5f, 5.5f, 0);
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, sc, crate);
                HandleOpen(server, sc, crate);
                HandleOpen(server, sc, crate);
            });

            Assert.Equal(0, sc.ContainedInNetId);
            Assert.Equal(6.5f, sc.Mover.X, 3);
            Assert.Equal(5.5f, sc.Mover.Z, 3);

            CleanupPeer(peer);
        }

        [Fact]
        public void Escape_ContainedOpensSelf_Released()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var sc = WaitServerClient(server);

            int crate = 0;
            bool containedBefore = false;
            OnGameLoop(server, () =>
            {
                OnCell(sc, 5.5f, 5.5f, 0);
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, sc, crate);
                HandleOpen(server, sc, crate);
                containedBefore = sc.ContainedInNetId == crate;
                HandleOpen(server, sc, crate);
            });

            Assert.True(containedBefore);
            Assert.Equal(0, sc.ContainedInNetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Release_TwoPlayers_NoStack()
        {
            var server = StartServer();
            var peer1 = CreateConnectedPeer();
            var peer2 = CreateConnectedPeer();
            var clients = WaitServerClients(server, 2);
            var a = clients[0];
            var b = clients[1];

            int crate = 0;
            OnGameLoop(server, () =>
            {
                OnCell(a, 5.5f, 5.5f, 0);
                OnCell(b, 5.5f, 5.5f, 0);
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, a, crate);
                HandleOpen(server, a, crate);
                HandleOpen(server, a, crate);
            });

            Assert.Equal(0, a.ContainedInNetId);
            Assert.Equal(0, b.ContainedInNetId);
            int acx = (int)MathF.Floor(a.Mover.X), acz = (int)MathF.Floor(a.Mover.Z);
            int bcx = (int)MathF.Floor(b.Mover.X), bcz = (int)MathF.Floor(b.Mover.Z);
            Assert.False(acx == bcx && acz == bcz);

            CleanupPeer(peer1);
            CleanupPeer(peer2);
        }

        [Fact]
        public void Prune_ContainerDespawned_ReleasesInPlace()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var sc = WaitServerClient(server);

            int crate = 0;
            OnGameLoop(server, () =>
            {
                OnCell(sc, 5.5f, 5.5f, 0);
                crate = server.SpawnGroundItem(CrateDef, 1, 5, 5, 0);
                HandleOpen(server, sc, crate);
                HandleOpen(server, sc, crate);
            });
            Assert.Equal(crate, sc.ContainedInNetId);

            OnGameLoop(server, () =>
            {
                server.DespawnGroundItem(crate);
                ProcessContainerOps(server);
            });
            Assert.Equal(0, sc.ContainedInNetId);

            CleanupPeer(peer);
        }

        private static T OnGameLoopFunc<T>(GameServer server, Func<T> func)
        {
            var queue = (System.Collections.Concurrent.ConcurrentQueue<Action>)MainThreadActionsField.GetValue(server)!;
            T result = default!;
            Exception? error = null;
            bool completed = false;
            queue.Enqueue(() =>
            {
                try { result = func(); }
                catch (Exception e) { error = e; }
                finally { Volatile.Write(ref completed, true); }
            });
            for (int i = 0; i < 500 && !Volatile.Read(ref completed); i++) Thread.Sleep(10);
            if (!Volatile.Read(ref completed)) throw new TimeoutException("GameLoop did not run the queued action");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }
    }
}
