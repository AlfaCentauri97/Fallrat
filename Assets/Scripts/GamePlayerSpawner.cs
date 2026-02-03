using Unity.Netcode;
using UnityEngine;

public sealed class GamePlayerSpawner : NetworkBehaviour
{
    [SerializeField] NetworkObject playerPrefab;
    [SerializeField] Transform[] spawnPoints;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SpawnFor(id);
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    void OnClientConnected(ulong clientId)
    {
        SpawnFor(clientId);
    }

    void SpawnFor(ulong clientId)
    {
        if (!playerPrefab) return;

        int index = (int)(clientId % (ulong)Mathf.Max(1, spawnPoints.Length));
        var sp = spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[index] ? spawnPoints[index] : null;

        Vector3 pos = sp ? sp.position : Vector3.zero;
        Quaternion rot = sp ? sp.rotation : Quaternion.identity;

        var obj = Instantiate(playerPrefab, pos, rot);
        obj.SpawnAsPlayerObject(clientId, true);
    }
}