using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine.UI;
using DG.Tweening;

public sealed class NetworkBootstrap : MonoBehaviour
{
    public static NetworkBootstrap Instance { get; private set; }

    [Header("NGO")]
    [SerializeField] NetworkManager networkManager;
    [SerializeField] UnityTransport transport;

    [Header("Scenes")]
    [SerializeField] string lobbySceneName = "Lobby";
    [SerializeField] string gameSceneName = "MainScene";

    [Header("Quick Play")]
    [SerializeField] int maxPlayers = 8;
    [SerializeField] float quickPlaySeconds = 30f;

    [Header("UI (bound from LobbySceneRefs)")]
    [SerializeField] TextMeshProUGUI statusText;

    [Header("Lobby Preview Rats")]
    [SerializeField] GameObject lobbyRatPrefab;
    [SerializeField] Transform[] lobbySlots;

    [Header("Lobby Start Sequence")]
    [SerializeField] float lobbyStartAnimDelay = 2f;
    [SerializeField] Image fadeImage;
    [SerializeField] float fadeDuration = 1f;

    [Header("Lobby Query Retry")]
    [SerializeField] int queryRetries = 2;
    [SerializeField] float queryRetryDelay = 0.75f;

    const string KEY_JOINCODE = "joinCode";
    const string KEY_STATE = "state";
    const string STATE_SEARCHING = "searching";
    const string STATE_STARTING = "starting";
    const string STATE_INGAME = "in_game";

    Lobby currentLobby;

    Coroutine heartbeatCo;
    Coroutine pollCo;
    Coroutine hostCountdownCo;

    GameObject[] slotInstances;

    Tween fadeTween;

    bool inFlow;
    bool isHost;
    bool clientConnectStarted;
    bool startSequenceTriggered;

    float hostCountdownEndRealtime;

    public bool IsSearching => inFlow;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        if (networkManager && !transport) transport = networkManager.GetComponent<UnityTransport>();

        SceneManager.sceneLoaded += OnSceneLoaded;

        BindLobbySceneRefsIfPresent();
        ResetRuntimeFlags();
        SetStatus("Ready.");
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == lobbySceneName)
        {
            BindLobbySceneRefsIfPresent();
            ResetFade();
            ClearLobbyPreview();
            ResetRuntimeFlags();
            inFlow = false;
            SetStatus("Ready.");
        }
    }

    void BindLobbySceneRefsIfPresent()
    {
        var refs = FindFirstObjectByType<LobbySceneRefs>();
        if (!refs) return;

        statusText = refs.statusText;
        fadeImage = refs.fadeImage;
        lobbySlots = refs.lobbySlots;

        slotInstances = new GameObject[lobbySlots != null ? lobbySlots.Length : 0];
        ResetFade();
    }

    async void OnApplicationQuit()
    {
        await CleanupLobbyAsync();
    }

    public async Task QuickPlayAsync()
    {
        if (inFlow) return;
        inFlow = true;

        ClearLobbyPreview();
        ResetFade();
        KillFadeTween();
        ResetRuntimeFlags();

        try
        {
            SetStatus("Init services...");
            await EnsureServicesAsync();

            SetStatus("Searching lobby...");
            var joined = await TryJoinSearchingLobbyAsync();
            if (joined != null)
            {
                isHost = false;
                currentLobby = joined;

                ApplyLobbyPreview(currentLobby);
                StartClientFlow();
                return;
            }

            SetStatus("No lobby found. Creating...");
            isHost = true;
            currentLobby = await CreateLobbyAsHostAsync();

            ApplyLobbyPreview(currentLobby);
            StartHostFlow();
        }
        catch (LobbyServiceException e)
        {
            SetStatus($"Lobby error: {e.Reason}");
            Debug.LogError(e);
            inFlow = false;
        }
        catch (System.Exception e)
        {
            SetStatus("Unexpected error");
            Debug.LogError(e);
            inFlow = false;
        }
    }

    public async Task CancelQuickPlayAsync()
    {
        StopAllFlows();

        if (networkManager && (networkManager.IsHost || networkManager.IsServer || networkManager.IsClient))
            networkManager.Shutdown();

        ClearLobbyPreview();
        KillFadeTween();
        ResetFade();

        await CleanupLobbyAsync();

        currentLobby = null;
        ResetRuntimeFlags();
        inFlow = false;

        SetStatus("Ready.");
    }

    async Task EnsureServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    async Task<Lobby> TryJoinSearchingLobbyAsync()
    {
        var options = new QueryLobbiesOptions
        {
            Count = 25,
            Filters = new System.Collections.Generic.List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                new QueryFilter(QueryFilter.FieldOptions.S1, STATE_SEARCHING, QueryFilter.OpOptions.EQ)
            }
        };

        for (int attempt = 0; attempt <= queryRetries; attempt++)
        {
            try
            {
                SetStatus(attempt == 0 ? "Searching lobby..." : $"Searching lobby... retry {attempt}/{queryRetries}");

                var res = await Lobbies.Instance.QueryLobbiesAsync(options);
                var pick = res.Results?.FirstOrDefault();
                if (pick == null) return null;

                SetStatus("Joining lobby...");
                return await Lobbies.Instance.JoinLobbyByIdAsync(pick.Id);
            }
            catch (LobbyServiceException e)
            {
                bool last = attempt == queryRetries;
                Debug.LogWarning($"[Lobby] Query/Join failed ({e.Reason}) attempt {attempt + 1}/{queryRetries + 1}");

                if (last) return null;
                await Task.Delay((int)(queryRetryDelay * 1000f));
            }
        }

        return null;
    }

    async Task<Lobby> CreateLobbyAsHostAsync()
    {
        var options = new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new System.Collections.Generic.Dictionary<string, DataObject>
            {
                { KEY_STATE, new DataObject(DataObject.VisibilityOptions.Public, STATE_SEARCHING, DataObject.IndexOptions.S1) },
                { KEY_JOINCODE, new DataObject(DataObject.VisibilityOptions.Public, "") }
            }
        };

        return await Lobbies.Instance.CreateLobbyAsync("QuickPlay", maxPlayers, options);
    }

    void StartHostFlow()
    {
        StopAllFlows();

        hostCountdownEndRealtime = Time.realtimeSinceStartup + quickPlaySeconds;

        heartbeatCo = StartCoroutine(HeartbeatLobby());
        pollCo = StartCoroutine(PollLobbyAndRefreshPreview());
        hostCountdownCo = StartCoroutine(HostCountdownAndStart());

        _ = SetupRelayAsHostAndStartNGO();
    }

    async Task SetupRelayAsHostAndStartNGO()
    {
        if (!transport || !networkManager)
        {
            SetStatus("Missing transport/networkManager");
            inFlow = false;
            return;
        }

        SetStatus("Host: creating Relay...");

        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

        await Lobbies.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions
        {
            Data = new System.Collections.Generic.Dictionary<string, DataObject>
            {
                { KEY_JOINCODE, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
            }
        });

        var serverData = new RelayServerData(alloc, "dtls");
        transport.SetRelayServerData(serverData);

        networkManager.StartHost();
        SetStatus("Host: waiting for players...");
    }

    void StartClientFlow()
    {
        StopAllFlows();
        pollCo = StartCoroutine(ClientPollLobbyThenConnectAndPreview());
    }

    IEnumerator HeartbeatLobby()
    {
        while (currentLobby != null)
        {
            var t = Lobbies.Instance.SendHeartbeatPingAsync(currentLobby.Id);
            while (!t.IsCompleted) yield return null;
            yield return new WaitForSeconds(15f);
        }
    }

    IEnumerator PollLobbyAndRefreshPreview()
    {
        while (currentLobby != null)
        {
            var t = Lobbies.Instance.GetLobbyAsync(currentLobby.Id);
            while (!t.IsCompleted) yield return null;

            if (!t.IsFaulted)
            {
                currentLobby = t.Result;
                ApplyLobbyPreview(currentLobby);

                int players = currentLobby.Players != null ? currentLobby.Players.Count : 0;
                float left = Mathf.Max(0f, hostCountdownEndRealtime - Time.realtimeSinceStartup);
                SetStatus($"Searching... {players}/{maxPlayers} | start in {left:0}s");
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator ClientPollLobbyThenConnectAndPreview()
    {
        while (currentLobby != null)
        {
            var t = Lobbies.Instance.GetLobbyAsync(currentLobby.Id);
            while (!t.IsCompleted) yield return null;

            if (t.IsFaulted)
            {
                SetStatus("Client: lobby poll failed");
                inFlow = false;
                yield break;
            }

            currentLobby = t.Result;
            ApplyLobbyPreview(currentLobby);

            string joinCode = currentLobby.Data != null && currentLobby.Data.ContainsKey(KEY_JOINCODE)
                ? currentLobby.Data[KEY_JOINCODE].Value
                : "";

            string state = currentLobby.Data != null && currentLobby.Data.ContainsKey(KEY_STATE)
                ? currentLobby.Data[KEY_STATE].Value
                : "";

            if (state == STATE_STARTING && !startSequenceTriggered)
                StartCoroutine(ClientStartSequence());

            if (!clientConnectStarted && !string.IsNullOrEmpty(joinCode))
            {
                clientConnectStarted = true;
                SetStatus("Client: connecting...");
                _ = SetupRelayAsClientAndStartNGO(joinCode);
            }

            int players = currentLobby.Players != null ? currentLobby.Players.Count : 0;
            SetStatus($"Lobby: {players}/{maxPlayers} | state: {state}");

            yield return new WaitForSeconds(0.5f);
        }
    }

    async Task SetupRelayAsClientAndStartNGO(string joinCode)
    {
        if (!transport || !networkManager)
        {
            SetStatus("Missing transport/networkManager");
            inFlow = false;
            return;
        }

        JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var serverData = new RelayServerData(join, "dtls");
        transport.SetRelayServerData(serverData);

        networkManager.StartClient();
    }

    IEnumerator HostCountdownAndStart()
    {
        while (Time.realtimeSinceStartup < hostCountdownEndRealtime)
            yield return null;

        if (currentLobby == null) yield break;

        var u = Lobbies.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions
        {
            Data = new System.Collections.Generic.Dictionary<string, DataObject>
            {
                { KEY_STATE, new DataObject(DataObject.VisibilityOptions.Public, STATE_STARTING, DataObject.IndexOptions.S1) }
            }
        });
        while (!u.IsCompleted) yield return null;

        if (!startSequenceTriggered)
        {
            startSequenceTriggered = true;

            PlayLobbyStartOnAllRats();
            yield return new WaitForSeconds(lobbyStartAnimDelay);
            yield return FadeToBlack();
        }

        if (networkManager && networkManager.SceneManager != null)
            networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);

        var u2 = Lobbies.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions
        {
            Data = new System.Collections.Generic.Dictionary<string, DataObject>
            {
                { KEY_STATE, new DataObject(DataObject.VisibilityOptions.Public, STATE_INGAME, DataObject.IndexOptions.S1) }
            }
        });
        while (!u2.IsCompleted) yield return null;
    }

    IEnumerator ClientStartSequence()
    {
        if (startSequenceTriggered) yield break;
        startSequenceTriggered = true;

        PlayLobbyStartOnAllRats();
        yield return new WaitForSeconds(lobbyStartAnimDelay);
        yield return FadeToBlack();
    }

    void ApplyLobbyPreview(Lobby lobby)
    {
        if (!lobbyRatPrefab || lobbySlots == null || lobbySlots.Length == 0) return;
        if (lobby.Players == null) return;

        if (slotInstances == null || slotInstances.Length != lobbySlots.Length)
            slotInstances = new GameObject[lobbySlots.Length];

        int slotsCount = lobbySlots.Length;
        int count = Mathf.Min(slotsCount, lobby.Players.Count);

        for (int i = 0; i < slotsCount; i++)
        {
            bool shouldExist = i < count;

            var slot = lobbySlots[i];
            if (!slot) continue;

            var slotView = slot.GetComponent<LobbySlot>();

            if (shouldExist)
            {
                if (slotInstances[i] == null)
                    slotInstances[i] = Instantiate(lobbyRatPrefab, slot.position, slot.rotation, slot);

                if (slotView)
                    slotView.SetOccupied(true, i == 0);
            }
            else
            {
                if (slotInstances[i] != null)
                {
                    Destroy(slotInstances[i]);
                    slotInstances[i] = null;
                }

                if (slotView)
                    slotView.Clear();
            }
        }
    }

    void ClearLobbyPreview()
    {
        if (slotInstances == null) return;

        for (int i = 0; i < slotInstances.Length; i++)
        {
            if (slotInstances[i] != null) Destroy(slotInstances[i]);
            slotInstances[i] = null;
        }
    }

    void PlayLobbyStartOnAllRats()
    {
        if (slotInstances == null) return;

        for (int i = 0; i < slotInstances.Length; i++)
        {
            var go = slotInstances[i];
            if (!go) continue;

            var anim = go.GetComponentInChildren<Animator>();
            if (!anim) continue;

            anim.Play("Lobby_Rat_Start", 0, 0f);
        }
    }

    IEnumerator FadeToBlack()
    {
        if (!fadeImage) yield break;

        KillFadeTween();
        fadeTween = fadeImage.DOFade(1f, fadeDuration);
        yield return fadeTween.WaitForCompletion();
    }

    void ResetFade()
    {
        if (!fadeImage) return;

        var c = fadeImage.color;
        c.a = 0f;
        fadeImage.color = c;
        fadeImage.gameObject.SetActive(true);
    }

    void KillFadeTween()
    {
        if (fadeTween != null && fadeTween.IsActive())
            fadeTween.Kill();
        fadeTween = null;
    }

    void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
    }

    void StopAllFlows()
    {
        if (heartbeatCo != null) StopCoroutine(heartbeatCo);
        if (pollCo != null) StopCoroutine(pollCo);
        if (hostCountdownCo != null) StopCoroutine(hostCountdownCo);

        heartbeatCo = null;
        pollCo = null;
        hostCountdownCo = null;
    }

    void ResetRuntimeFlags()
    {
        isHost = false;
        clientConnectStarted = false;
        startSequenceTriggered = false;
        hostCountdownEndRealtime = 0f;
    }

    async Task CleanupLobbyAsync()
    {
        if (currentLobby == null) return;

        try
        {
            if (isHost)
                await Lobbies.Instance.DeleteLobbyAsync(currentLobby.Id);
            else if (AuthenticationService.Instance.IsSignedIn)
                await Lobbies.Instance.RemovePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId);
        }
        catch { }
    }
}
