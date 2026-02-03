using System.Collections.Generic;
using UnityEngine;
using Project.Pooling;

public sealed class GateManager : MonoBehaviour
{
    [SerializeField] PoolKey gateKey;
    [SerializeField] Transform spawnPoint;

    [Header("Base")]
    [SerializeField] float spawnInterval = 1.2f;
    [SerializeField] float speed = 8f;
    [SerializeField] float despawnZ = -40f;

    [Header("Scaling")]
    [SerializeField] float intervalMin = 0.55f;
    [SerializeField] float speedMax = 18f;
    [SerializeField] float rampDuration = 90f;
    [SerializeField] AnimationCurve ramp = null;

    readonly List<GameObject> active = new();
    float timer;
    float elapsed;

    float BaseInterval => spawnInterval;
    float BaseSpeed => speed;

    void Awake()
    {
        if (ramp == null || ramp.length == 0)
            ramp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        float t = rampDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / rampDuration);
        float k = Mathf.Clamp01(ramp.Evaluate(t));

        float currentSpeed = Mathf.Lerp(BaseSpeed, speedMax, k);
        float currentInterval = Mathf.Lerp(BaseInterval, intervalMin, k);

        timer += Time.deltaTime;
        while (timer >= currentInterval)
        {
            timer -= currentInterval;
            SpawnGate();
        }

        for (int i = active.Count - 1; i >= 0; i--)
        {
            var go = active[i];
            if (!go) { active.RemoveAt(i); continue; }

            var p = go.transform.position;
            p.z -= currentSpeed * Time.deltaTime;
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