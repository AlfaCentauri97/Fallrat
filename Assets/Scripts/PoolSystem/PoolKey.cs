using UnityEngine;
/// <summary>
/// ScriptableObject identifier used as a stable key for pooling.
/// </summary>
namespace Project.Pooling
{
    [CreateAssetMenu(menuName = "Project/Pooling/Pool Key")]
    public sealed class PoolKey : ScriptableObject
    {
        public string Id => name;
    }
}