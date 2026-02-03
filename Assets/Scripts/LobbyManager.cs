using UnityEngine;

public sealed class LobbyManager : MonoBehaviour
{
    [SerializeField] RectTransform lobbyUI;
    [SerializeField] RectTransform menuUI;

    void Awake()
    {
        ShowMenu();
    }

    public async void OnClickQuickPlay()
    {
        ShowLobby();
        await NetworkBootstrap.Instance.QuickPlayAsync();
    }

    public async void OnClickCancel()
    {
        await NetworkBootstrap.Instance.CancelQuickPlayAsync();
        ShowMenu();
    }

    public void QuitApplication()
    {
        Application.Quit();
    }

    void ShowMenu()
    {
        if (menuUI) menuUI.gameObject.SetActive(true);
        if (lobbyUI) lobbyUI.gameObject.SetActive(false);
    }

    void ShowLobby()
    {
        if (menuUI) menuUI.gameObject.SetActive(false);
        if (lobbyUI) lobbyUI.gameObject.SetActive(true);
    }
}