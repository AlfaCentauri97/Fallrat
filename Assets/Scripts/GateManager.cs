using System.Collections.Generic;
using UnityEngine;
using Project.Pooling;

public sealed class GateManager : MonoBehaviour
{
    [SerializeField] PoolKey gateKey;
    [SerializeField] Transform spawnPoint;
    [SerializeField] float spawnInterval = 1.2f;
    [SerializeField] float speed = 8f;
    [SerializeField] float despawnZ = -40f;

    readonly List<GameObject> active = new();
    float timer;

    void Update()
    {
        timer += Time.deltaTime;
        while (timer >= spawnInterval)
        {
            timer -= spawnInterval;
            SpawnGate();
        }

        for (int i = active.Count - 1; i >= 0; i--)
        {
            var go = active[i];
            if (!go) { active.RemoveAt(i); continue; }

            var p = go.transform.position;
            p.z -= speed * Time.deltaTime;
            go.transform.position = p;

            if (p.z <= despawnZ)
            {
                PoolManager.Despawn(gateKey, go);
                active.RemoveAt(i);
            }
        }
    }

    void SpawnGate()
    {
        if (!spawnPoint) return;

        var go = PoolManager.Spawn(gateKey, spawnPoint.position, spawnPoint.rotation);
        if (go) active.Add(go);
    }
}