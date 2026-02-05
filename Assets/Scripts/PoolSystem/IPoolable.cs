/// <summary>
/// Pool callbacks for spawn/despawn.
/// </summary>
public interface IPoolable
{
    void OnSpawned();
    void OnDespawned();
}