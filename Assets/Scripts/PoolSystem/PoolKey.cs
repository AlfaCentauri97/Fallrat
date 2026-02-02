using UnityEngine;

namespace Project.Pooling
{
    [CreateAssetMenu(menuName = "Project/Pooling/Pool Key")]
    public sealed class PoolKey : ScriptableObject
    {
        public string Id => name;
    }
}