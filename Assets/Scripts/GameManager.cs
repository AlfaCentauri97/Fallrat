using Project.Core;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
public sealed class GameManager : SingletonMonoBehaviour<GameManager>
{
    [SerializeField] Transform endGamePoint;
    [SerializeField] float endGameUIDelay = 2f;

    Coroutine endGameRoutine;

    public void PlayerDied(PlayerController player)
    {
        if (!endGamePoint) return;

        player.transform.position = endGamePoint.position;
        player.transform.rotation = endGamePoint.rotation;

        CameraMgr.Instance.SetEndCamera();
        player.PlayDeath();

        if (endGameRoutine != null)
            StopCoroutine(endGameRoutine);

        endGameRoutine = StartCoroutine(EndGameSequence());
    }

    IEnumerator EndGameSequence()
    {
        yield return new WaitForSeconds(endGameUIDelay);
        UIManager.Instance.ShowEndGameUI(true);
    }
    
    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}