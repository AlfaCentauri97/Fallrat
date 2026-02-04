using Unity.Netcode;
using UnityEngine;

public sealed class GamePlayerSpawner : NetworkBehaviour
{
    [SerializeField] NetworkObject playerPrefab;

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

    void OnClientConnected(ulong clientId) => SpawnFor(clientId);

    void SpawnFor(ulong clientId)
    {
        if (!playerPrefab) return;
        
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var cc) && cc.PlayerObject != null)
            return;

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        var obj = Instantiate(playerPrefab, pos, rot);
        obj.SpawnAsPlayerObject(clientId, true);
    }
}