using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Project.Core;

public sealed class GameManager : SingletonMonoBehaviour<GameManager>
{
    [SerializeField] float endGameUIDelay = 2f;
    Coroutine endGameRoutine;
    public Transform endGamePoint;
    public void ServerMoveToEndPoint(NetworkPlayerController player)
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer) return;
        if (!endGamePoint || !player) return;

        player.ServerTeleportTo(endGamePoint.position, endGamePoint.rotation);
    }

    public void LocalPlayerDied()
    {
        if (endGameRoutine != null) StopCoroutine(endGameRoutine);
        endGameRoutine = StartCoroutine(EndGameSequence());
    }


    IEnumerator EndGameSequence()
    {
        yield return new WaitForSeconds(endGameUIDelay);
        if (UIManager.Instance) UIManager.Instance.ShowEndGameUI(true);
    }

    public void ReturnToLobby()
    {
        if (NetworkManager.Singleton)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene("LobbyScene");
    }
}