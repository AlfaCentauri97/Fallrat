using System.Collections.Generic;
using UnityEngine;
using Project.Core;

namespace Project.Pooling
{
    public sealed class PoolManager : SingletonMonoBehaviour<PoolManager>
    {
        [SerializeField] PoolDefinition[] _definitions;

        readonly Dictionary<string, Pool> _pools = new();

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            for (int i = 0; i < _definitions.Length; i++)
            {
                var d = _definitions[i];
                if (d == null || string.IsNullOrWhiteSpace(d.Id) || d.Prefab == null) continue;

                var root = new GameObject($"Pool_{d.Id}").transform;
                root.SetParent(transform, false);

                _pools[d.Id] = new Pool(d.Prefab, d.Prewarm, d.MaxSize, root);
            }
        }

        public static GameObject Spawn(string id, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (!Instance) return null;
            return Instance._pools.TryGetValue(id, out var pool) ? pool.Spawn(position, rotation, parent) : null;
        }

        public static bool Despawn(string id, GameObject go)
        {
            if (!Instance) return false;
            return Instance._pools.TryGetValue(id, out var pool) && pool.Despawn(go);
        }
    }
}