using UnityEngine;
using Project.Pooling;

public sealed class ObstacleFade : MonoBehaviour, IPoolable
{
    [SerializeField] float minAlpha = 0.15f;
    [SerializeField] float fadeSpeed = 12f;

    Renderer[] renderers;
    MaterialPropertyBlock mpb;

    // URP:
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");   // URP Lit/Unlit
    // Built-in / legacy fallback:
    static readonly int ColorId     = Shader.PropertyToID("_Color");       // Standard shader / custom

    float current = 1f;
    float target = 1f;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        mpb = new MaterialPropertyBlock();
        Apply(1f);
    }

    void Update()
    {
        if (Mathf.Approximately(current, target)) return;

        current = Mathf.MoveTowards(current, target, fadeSpeed * Time.deltaTime);
        Apply(current);
    }

    public void SetOccluded(bool occluded)
    {
        target = occluded ? minAlpha : 1f;
    }

    void Apply(float a)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r) continue;

            // ważne: czyścimy per-renderer, żeby nie przenosić wartości między rendererami
            mpb.Clear();
            r.GetPropertyBlock(mpb);

            var mat = r.sharedMaterial;
            if (!mat)
            {
                r.SetPropertyBlock(mpb);
                continue;
            }

            // URP: _BaseColor
            if (mat.HasProperty(BaseColorId))
            {
                // UWAGA: GetColor z mat czyta z materiału, nie z MPB,
                // ale do fade zwykle wystarcza (kolor bazowy ma być stały).
                var c = mat.GetColor(BaseColorId);
                c.a = a;
                mpb.SetColor(BaseColorId, c);
            }
            // Fallback: _Color
            else if (mat.HasProperty(ColorId))
            {
                var c = mat.GetColor(ColorId);
                c.a = a;
                mpb.SetColor(ColorId, c);
            }

            r.SetPropertyBlock(mpb);
        }
    }

    public void OnSpawned()
    {
        current = 1f;
        target = 1f;
        Apply(1f);
    }

    public void OnDespawned()
    {
        current = 1f;
        target = 1f;
        Apply(1f);
    }
}
