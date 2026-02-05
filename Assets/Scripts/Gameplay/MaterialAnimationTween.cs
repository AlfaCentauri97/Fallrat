using UnityEngine;
using DG.Tweening;

/// <summary>
/// Scrolls the material _BaseMap UV offset on X using DOTween.
/// </summary>
    
public sealed class MaterialAnimationTweenURP : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private float speed = 1f;

    private Tween _tween;

    private void Start()
    {
        OffSetOnXAnimation();
    }

    private void OnDisable()
    {
        _tween?.Kill();
        _tween = null;
    }

    public void OffSetOnXAnimation()
    {
        _tween?.Kill();

        Vector2 startOffset = material.GetTextureOffset("_BaseMap");
        float x = startOffset.x;

        _tween = DOTween.To(
                () => x,
                v =>
                {
                    x = Mathf.Repeat(v, 1f);
                    material.SetTextureOffset("_BaseMap", new Vector2(x, startOffset.y));
                },
                x - 1f,
                1f / Mathf.Max(0.0001f, speed)
            )
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Incremental)
            .SetUpdate(true);
    }
}