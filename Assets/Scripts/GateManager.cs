using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Project.Pooling;

public sealed class GateManager : NetworkBehaviour
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

    System.Random rng;
    int trapCountHint;

    void Awake()
    {
        if (ramp == null || ramp.length == 0)
            ramp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    public override void OnNetworkSpawn()
    {
        if (!spawnPoint) return;

        if (IsServer)
            rng = new System.Random((int)System.DateTime.UtcNow.Ticks);

        trapCountHint = ResolveTrapCountHint();
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        float t = rampDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / rampDuration);
        float k = Mathf.Clamp01(ramp.Evaluate(t));

        float currentSpeed = Mathf.Lerp(speed, speedMax, k);
        float currentInterval = Mathf.Lerp(spawnInterval, intervalMin, k);

        if (IsServer)
        {
            timer += Time.deltaTime;
            while (timer >= currentInterval)
            {
                timer -= currentInterval;
                int idx = trapCountHint > 0 ? rng.Next(0, trapCountHint) : 0;
                SpawnGateClientRpc(idx);
            }
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

    int ResolveTrapCountHint()
    {
        if (!gateKey) return 0;

        var go = PoolManager.Spawn(gateKey, Vector3.one * 99999f, Quaternion.identity);
        if (!go) return 0;

        int count = 0;
        var gate = go.GetComponent<Gate>();
        if (gate) count = gate.TrapCount;

        PoolManager.Despawn(gateKey, go);
        return count;
    }

    [ClientRpc]
    void SpawnGateClientRpc(int trapIndex)
    {
        SpawnGateLocal(trapIndex);
    }

    void SpawnGateLocal(int trapIndex)
    {
        if (!spawnPoint) return;

        var go = PoolManager.Spawn(gateKey, spawnPoint.position, spawnPoint.rotation);
        if (!go) return;

        active.Add(go);

        var gate = go.GetComponent<Gate>();
        if (gate) gate.SetTrapIndex(trapIndex);
    }
}
