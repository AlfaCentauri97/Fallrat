using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple object pool for a prefab with optional prewarm, max size, and IPoolable spawn/despawn callbacks.
/// </summary>

namespace Project.Pooling
{
    public sealed class Pool
    {
        readonly GameObject _prefab;
        readonly Transform _root;
        readonly Stack<GameObject> _inactive = new();
        readonly HashSet<GameObject> _active = new();
        readonly int _maxSize;

        public Pool(GameObject prefab, int prewarm, int maxSize, Transform root)
        {
            _prefab = prefab;
            _maxSize = Mathf.Max(0, maxSize);
            _root = root;

            for (int i = 0; i < Mathf.Max(0, prewarm); i++)
                _inactive.Push(CreateNew(false));
        }

        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            GameObject go;

            if (_inactive.Count > 0)
            {
                go = _inactive.Pop();
            }
            else
            {
                if (_maxSize > 0 && _active.Count + _inactive.Count >= _maxSize)
                    return null;

                go = CreateNew(true);
            }

            _active.Add(go);

            var t = go.transform;
            if (parent != null) t.SetParent(parent, false);
            t.SetPositionAndRotation(position, rotation);

            go.SetActive(true);
            if (go.TryGetComponent<IPoolable>(out var p)) p.OnSpawned();

            return go;
        }

        public bool Despawn(GameObject go)
        {
            if (go == null || !_active.Remove(go))
                return false;

            if (go.TryGetComponent<IPoolable>(out var p)) p.OnDespawned();
            go.SetActive(false);
            go.transform.SetParent(_root, false);

            _inactive.Push(go);
            return true;
        }

        GameObject CreateNew(bool active)
        {
            var go = Object.Instantiate(_prefab, _root);
            go.SetActive(active);
            return go;
        }
    }
}