using UnityEngine;
using Client.Map;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;

namespace Client.Lifts
{
    public sealed class LiftRailVisual
    {
        private readonly int _liftId;

        private GameObject[] _roots;
        private BlockView[] _views;
        private BlockComponentPairing[] _pairing;

        private ushort _builtDefId;
        private byte _builtFacing, _builtPlanW, _builtPlanD, _builtStep, _builtCount;
        private int _builtBaseY;
        private bool _built;
        private bool _missingWarned;

        public LiftRailVisual(int liftId) => _liftId = liftId;

        public int InstanceCount => _roots != null ? _roots.Length : 0;

        public void Sync(in LiftRegistryEntry geom, float alpha, BlockRenderer blocks)
        {
            if (!_built || Signature(in geom) != BuiltSignature())
                Rebuild(in geom, blocks);

            if (_views == null)
                return;
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null)
                    _views[i].SetAlpha(alpha);
        }

        public void Destroy()
        {
            if (_roots != null)
                for (int i = 0; i < _roots.Length; i++)
                {
                    if (_pairing[i].BeginDespawn() && _views[i] != null)
                        _views[i].DespawnComponents();
                    if (_roots[i] != null)
                        Object.Destroy(_roots[i]);
                }
            _roots = null;
            _views = null;
            _pairing = null;
            _built = false;
        }

        private (ushort, byte, byte, byte, byte, byte, int) Signature(in LiftRegistryEntry geom)
            => (geom.RailDefId, geom.Facing, geom.PlanW, geom.PlanD, geom.FloorStep, geom.FloorCount, geom.BaseY);

        private (ushort, byte, byte, byte, byte, byte, int) BuiltSignature()
            => (_builtDefId, _builtFacing, _builtPlanW, _builtPlanD, _builtStep, _builtCount, _builtBaseY);

        private void Rebuild(in LiftRegistryEntry geom, BlockRenderer blocks)
        {
            Destroy();
            _built = true;
            _builtDefId = geom.RailDefId;
            _builtFacing = geom.Facing;
            _builtPlanW = geom.PlanW;
            _builtPlanD = geom.PlanD;
            _builtStep = geom.FloorStep;
            _builtCount = geom.FloorCount;
            _builtBaseY = geom.BaseY;

            var def = geom.RailDefId != 0 ? BlockDefinitionResolver.Find(geom.RailDefId) : null;
            if (def == null || def.Prefab == null)
            {
                WarnMissing(geom.RailDefId);
                return;
            }
            if (geom.FloorCount < 1)
                return;

            int count = geom.FloorCount;
            _roots = new GameObject[count];
            _views = new BlockView[count];
            _pairing = new BlockComponentPairing[count];

            var rotation = Quaternion.Euler(0f, 90f * (geom.Facing & 3), 0f);
            int cellX = Mathf.FloorToInt(geom.AnchorX);
            int cellZ = Mathf.FloorToInt(geom.AnchorZ);

            for (int k = 0; k < count; k++)
            {
                LiftCabinPlacement.WorldPivot(geom.AnchorX, geom.AnchorZ, geom.PlanW, geom.PlanD,
                    geom.FloorYAt(k), out float px, out float py, out float pz);

                var go = Object.Instantiate(def.Prefab);
                go.name = $"LiftRail{_liftId}_{k}";
                go.transform.SetPositionAndRotation(new Vector3(px, py, pz), rotation);
                _roots[k] = go;

                var view = go.GetComponent<BlockView>();
                if (view == null)
                    view = go.AddComponent<BlockView>();
                view.Bind(cellX, Mathf.FloorToInt(py), cellZ, 0, null);
                _views[k] = view;

                if (blocks != null && _pairing[k].BeginSpawn())
                {
                    var ctx = blocks.ContextFor(cellX, Mathf.FloorToInt(py), cellZ, geom.RailDefId);
                    view.SpawnComponents(in ctx);
                }
            }
        }

        private void WarnMissing(ushort railDefId)
        {
            if (_missingWarned)
                return;
            _missingWarned = true;
            Debug.LogWarning($"[Lifts] лифт {_liftId}: модули шахты не рисуются — у типа рельса {railDefId} " +
                             "не найден BlockDefinition или не назначен Prefab. Ездить это не мешает.");
        }
    }
}
