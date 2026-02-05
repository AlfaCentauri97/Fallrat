using System.Collections.Generic;
using UnityEngine;
using Project.Core;
/// <summary>
/// Singleton manager that initializes pools from a PoolRegistry and provides static Spawn/Despawn access by PoolKey.
/// </summary>
namespace Project.Pooling
{
    public sealed class PoolManager : SingletonMonoBehaviour<PoolManager>
    {
        [SerializeField] PoolRegistry registry;

        readonly Dictionary<PoolKey, Pool> _pools = new();

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            if (!registry || registry.Entries == null) return;

            for (int i = 0; i < registry.Entries.Length; i++)
            {
                var e = registry.Entries[i];
                if (e == null || !e.Key || !e.Prefab) continue;
                if (_pools.ContainsKey(e.Key)) continue;

                var root = new GameObject($"Pool_{e.Key.Id}").transform;
                root.SetParent(transform, false);

                _pools[e.Key] = new Pool(e.Prefab, e.Prewarm, e.MaxSize, root);
            }
        }

        public static GameObject Spawn(PoolKey key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (!Instance || !key) return null;
            return Instance._pools.TryGetValue(key, out var pool) ? pool.Spawn(position, rotation, parent) : null;
        }

        public static bool Despawn(PoolKey key, GameObject go)
        {
            if (!Instance || !key) return false;
            return Instance._pools.TryGetValue(key, out var pool) && pool.Despawn(go);
        }
    }
}