namespace Project.Pooling
{
    public interface IPoolable
    {
        void OnSpawned();
        void OnDespawned();
    }
}