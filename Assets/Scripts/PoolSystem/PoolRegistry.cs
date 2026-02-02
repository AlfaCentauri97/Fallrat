using UnityEngine;

namespace Project.Pooling
{
    [CreateAssetMenu(menuName = "Project/Pooling/Pool Registry")]
    public sealed class PoolRegistry : ScriptableObject
    {
        public Entry[] Entries;

        [System.Serializable]
        public sealed class Entry
        {
            public PoolKey Key;
            public GameObject Prefab;
            public int Prewarm;
            public int MaxSize;
        }
    }
}