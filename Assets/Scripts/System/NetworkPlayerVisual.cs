using Unity.Netcode;
using UnityEngine;

public sealed class NetworkPlayerVisual : NetworkBehaviour
{
    public SkinnedMeshRenderer playerRenderer;

    [Header("Materials")]
    public Material localPlayerMaterial;
    public Material otherPlayerMaterial;

    public override void OnNetworkSpawn()
    {
        if (!playerRenderer) return;

        playerRenderer.sharedMaterial = IsOwner ? localPlayerMaterial : otherPlayerMaterial;
    }
}