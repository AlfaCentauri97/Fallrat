using Project.Core;
using UnityEngine;
using TMPro;
using DG.Tweening;
/// <summary>
/// Singleton UI manager that tracks score, updates the score text, plays a punch tween, and toggles end-game UI.
/// </summary>
public sealed class UIManager : SingletonMonoBehaviour<UIManager>
{
    [Header("Score")]
    [SerializeField] RectTransform endGameUI;
    [Header("Score")]
    [SerializeField] TextMeshProUGUI scoreText;

    [Header("Tween")]
    [SerializeField] float punchScale = 0.25f;
    [SerializeField] float punchDuration = 0.2f;
    [SerializeField] int punchVibrato = 8;
    [SerializeField] float punchElasticity = 0.8f;

    int score;
    Tween punchTween;

    protected override void Awake()
    {
        base.Awake();
        UpdateText();
    }

    public void AddScore()
    {
        score++;
        UpdateText();

        if (!scoreText) return;

        punchTween?.Kill();
        punchTween = scoreText.transform.DOPunchScale(
            Vector3.one * punchScale,
            punchDuration,
            punchVibrato,
            punchElasticity
        );
    }

    void UpdateText()
    {
        if (!scoreText) return;
        scoreText.text = score.ToString();
    }
    
    public void ShowEndGameUI(bool show)
    {
        if (!endGameUI) return;
        endGameUI.gameObject.SetActive(show);
    }

}