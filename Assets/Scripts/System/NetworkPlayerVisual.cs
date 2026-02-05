using Unity.Netcode;
using UnityEngine;

public sealed class NetworkPlayerVisual : NetworkBehaviour
{
    [SerializeField, Range(0f, 1f)] float otherAlpha = 0.5f;

    public SkinnedMeshRenderer playerRenderer;

    MaterialPropertyBlock mpb;

    public override void OnNetworkSpawn()
    {
        mpb = new MaterialPropertyBlock();
        ApplyAlpha(IsOwner ? 1f : otherAlpha);
    }

    void ApplyAlpha(float a)
    {
        if (!playerRenderer) return;

        mpb.Clear();
        playerRenderer.GetPropertyBlock(mpb);

        var mat = playerRenderer.sharedMaterial;
        if (!mat) return;

        if (mat.HasProperty("_BaseColor"))
        {
            var c = mat.GetColor("_BaseColor");
            c.a = a;
            mpb.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            var c = mat.GetColor("_Color");
            c.a = a;
            mpb.SetColor("_Color", c);
        }

        playerRenderer.SetPropertyBlock(mpb);
    }
}