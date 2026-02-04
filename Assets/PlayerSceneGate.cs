using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PlayerSceneGate : NetworkBehaviour
{
    [SerializeField] string gameplaySceneName = "MainScene";
    [SerializeField] GameObject visualRoot;
    [SerializeField] MonoBehaviour[] enableOnlyInGameplay;

    public override void OnNetworkSpawn()
    {
        Apply();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m) => Apply();

    void Apply()
    {
        bool inGameplay = SceneManager.GetActiveScene().name == gameplaySceneName;

        if (visualRoot) visualRoot.SetActive(inGameplay);

        if (enableOnlyInGameplay != null)
        {
            foreach (var b in enableOnlyInGameplay)
                if (b) b.enabled = inGameplay;
        }
    }
}