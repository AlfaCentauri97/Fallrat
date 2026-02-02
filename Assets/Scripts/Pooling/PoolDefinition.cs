using UnityEngine;

namespace Project.Pooling
{
    [System.Serializable]
    public sealed class PoolDefinition
    {
        public string Id;
        public GameObject Prefab;
        public int Prewarm;
        public int MaxSize;
    }
}