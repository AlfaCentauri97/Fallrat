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

/// <summary>
/// Quick Play networking bootstrap: UGS Lobbies + Relay + NGO (host or client), with lobby polling and scene transition.
/// </summary>
    
public sealed class NetworkBootstrap : MonoBehaviour
{
    public static NetworkBootstrap Instance { get; private set; }

    [Header("NGO")]
    [SerializeField] NetworkManager networkManager;
    [SerializeField] UnityTransport transport;

    [Header("Scenes")]
    [SerializeField] string lobbySceneName = "LobbyScene";
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
    const string KEY_START_AT = "startAt";

    const string STATE_SEARCHING = "searching";
    const string STATE_STARTING = "starting";
    const string STATE_INGAME = "in_game";

    Lobby currentLobby;

    Coroutine heartbeatCo;
    Coroutine pollCo;
    Coroutine hostCountdownCo;
    Coroutine bindLobbyRefsCo;

    GameObject[] slotInstances;
    Tween fadeTween;

    bool inFlow;
    bool clientConnectStarted;
    bool startSequenceTriggered;

    float hostCountdownEndRealtime;
    bool hostRelayReady;

    int lastPlayers;
    string lastState = "";
    long lastStartAtUnixMs = -1;

    int lastPreviewPlayers = -1;
    string lastPreviewState = "";

    public bool IsSearching => inFlow;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        DontDestroyOnLoad(gameObject);

        if (!networkManager) networkManager = GetComponent<NetworkManager>();
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
            if (bindLobbyRefsCo != null) StopCoroutine(bindLobbyRefsCo);
            bindLobbyRefsCo = StartCoroutine(BindLobbyRefsRetry());
        }
    }

    IEnumerator BindLobbyRefsRetry()
    {
        const float timeout = 1.5f;
        float end = Time.realtimeSinceStartup + timeout;

        LobbySceneRefs refs = null;

        while (Time.realtimeSinceStartup < end)
        {
            refs = FindFirstObjectByType<LobbySceneRefs>(FindObjectsInactive.Include);
            if (refs != null) break;
            yield return null;
        }

        if (refs == null)
        {
            Debug.LogWarning("[NetworkBootstrap] LobbySceneRefs not found (retry timeout).");
            yield break;
        }

        statusText = refs.statusText;
        fadeImage = refs.fadeImage;
        lobbySlots = refs.lobbySlots;

        slotInstances = new GameObject[lobbySlots != null ? lobbySlots.Length : 0];

        ResetFade();
        ClearLobbyPreview();
        ResetRuntimeFlags();
        inFlow = false;

        SetStatus("Ready.");
    }

    void BindLobbySceneRefsIfPresent()
    {
        var refs = FindFirstObjectByType<LobbySceneRefs>(FindObjectsInactive.Include);
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
                currentLobby = joined;
                ApplyLobbyPreviewIfNeeded(currentLobby);
                StartClientFlow();
                return;
            }

            SetStatus("No lobby found. Creating...");
            currentLobby = await CreateLobbyAsHostAsync();

            ApplyLobbyPreviewIfNeeded(currentLobby);
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
            SetStatus($"Unexpected error: {e.GetType().Name}: {e.Message}");
            Debug.LogError(e);
            inFlow = false;
        }
    }

    public async Task CancelQuickPlayAsync()
    {
        StopAllFlows();

        if (networkManager && (networkManager.IsHost || networkManager.IsServer || networkManager.IsClient))
        {
            networkManager.Shutdown();
            await Task.Yield();
            await Task.Yield();
        }

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        string profile = $"dev_{pid}";
#else
        string profile = "default";
#endif

        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            options.SetProfile(profile);
            await UnityServices.InitializeAsync(options);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[Auth] Signed in | Profile={profile} | PlayerId={AuthenticationService.Instance.PlayerId}");
        }
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

        float end = Time.realtimeSinceStartup + 6f;
        int attempt = 0;

        while (Time.realtimeSinceStartup < end)
        {
            attempt++;
            try
            {
                SetStatus($"Searching lobby... attempt {attempt}");

                var res = await Lobbies.Instance.QueryLobbiesAsync(options);
                var list = res.Results;
                if (list == null || list.Count == 0)
                {
                    await Task.Delay(300);
                    continue;
                }

                foreach (var pick in list)
                {
                    try
                    {
                        SetStatus("Joining lobby...");
                        return await Lobbies.Instance.JoinLobbyByIdAsync(pick.Id);
                    }
                    catch (LobbyServiceException) { }
                }

                await Task.Delay(300);
            }
            catch (LobbyServiceException)
            {
                await Task.Delay(300);
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
                { KEY_STATE,    new DataObject(DataObject.VisibilityOptions.Public, STATE_SEARCHING, DataObject.IndexOptions.S1) },
                { KEY_JOINCODE, new DataObject(DataObject.VisibilityOptions.Public, "") },
                { KEY_START_AT, new DataObject(DataObject.VisibilityOptions.Public, "") }
            }
        };

        return await Lobbies.Instance.CreateLobbyAsync("QuickPlay", maxPlayers, options);
    }

    void StartHostFlow()
    {
        StopAllFlows();

        hostRelayReady = false;
        hostCountdownEndRealtime = 0f;

        heartbeatCo = StartCoroutine(HeartbeatLobby());
        pollCo = StartCoroutine(PollLobbyAndRefreshPreview());

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

        hostRelayReady = true;
        hostCountdownEndRealtime = Time.realtimeSinceStartup + quickPlaySeconds;

        if (hostCountdownCo != null) StopCoroutine(hostCountdownCo);
        hostCountdownCo = StartCoroutine(HostCountdownAndStart());

        SetStatus("Host: waiting for players...");
    }

    void StartClientFlow()
    {
        StopAllFlows();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnNGOClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnNGOClientConnected;

            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNGOClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnNGOClientDisconnected;
        }

        pollCo = StartCoroutine(ClientPollLobbyThenConnectAndPreview());
    }

    void OnNGOClientConnected(ulong id)
    {
        if (NetworkManager.Singleton != null && id == NetworkManager.Singleton.LocalClientId)
            SetStatus("Client: connected to host");
    }

    void OnNGOClientDisconnected(ulong id)
    {
        if (NetworkManager.Singleton != null && id == NetworkManager.Singleton.LocalClientId)
        {
            SetStatus("Client: disconnected");
            _ = CancelQuickPlayAsync();
        }
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

                UpdateLastKnownLobbyData(currentLobby);
                ApplyLobbyPreviewIfNeeded(currentLobby);

                if (hostRelayReady)
                {
                    float left = Mathf.Max(0f, hostCountdownEndRealtime - Time.realtimeSinceStartup);
                    SetStatus($"Searching... {lastPlayers}/{maxPlayers} | start in {left:0}s");
                }
                else
                {
                    SetStatus($"Creating relay... {lastPlayers}/{maxPlayers}");
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator ClientPollLobbyThenConnectAndPreview()
    {
        int fails = 0;

        while (currentLobby != null)
        {
            var t = Lobbies.Instance.GetLobbyAsync(currentLobby.Id);
            while (!t.IsCompleted) yield return null;

            if (t.IsFaulted)
            {
                fails++;
                ShowClientLobbyStatus();

                yield return new WaitForSeconds(0.75f);

                if (fails >= 6)
                {
                    SetStatus("Client: lobby lost. Canceling...");
                    _ = CancelQuickPlayAsync();
                    yield break;
                }

                continue;
            }

            fails = 0;
            currentLobby = t.Result;

            UpdateLastKnownLobbyData(currentLobby);
            ApplyLobbyPreviewIfNeeded(currentLobby);

            string joinCode = (currentLobby.Data != null && currentLobby.Data.ContainsKey(KEY_JOINCODE))
                ? currentLobby.Data[KEY_JOINCODE].Value
                : "";

            if (lastState == STATE_STARTING && !startSequenceTriggered)
                StartCoroutine(ClientStartSequence());

            if (!clientConnectStarted && !string.IsNullOrEmpty(joinCode))
            {
                clientConnectStarted = true;
                SetStatus("Client: connecting...");
                _ = SetupRelayAsClientAndStartNGO(joinCode);
            }

            ShowClientLobbyStatus();

            yield return new WaitForSeconds(0.5f);
        }
    }

    void UpdateLastKnownLobbyData(Lobby lobby)
    {
        lastPlayers = lobby.Players != null ? lobby.Players.Count : 0;

        lastState = (lobby.Data != null && lobby.Data.ContainsKey(KEY_STATE))
            ? lobby.Data[KEY_STATE].Value
            : "";

        if (lobby.Data != null && lobby.Data.ContainsKey(KEY_START_AT))
        {
            var v = lobby.Data[KEY_START_AT].Value;
            if (long.TryParse(v, out var unixMs))
                lastStartAtUnixMs = unixMs;
        }
    }

    void ShowClientLobbyStatus()
    {
        if (string.IsNullOrEmpty(lastState))
        {
            SetStatus("Lobby: syncing...");
            return;
        }

        if (lastState == STATE_STARTING && lastStartAtUnixMs > 0)
        {
            float left = GetSecondsLeftFromUnixMs(lastStartAtUnixMs);
            SetStatus($"Lobby: {lastPlayers}/{maxPlayers} | start in {left:0}s");
            return;
        }

        SetStatus($"Lobby: {lastPlayers}/{maxPlayers} | state: {lastState}");
    }

    float GetSecondsLeftFromUnixMs(long unixMs)
    {
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long diff = unixMs - now;
        return Mathf.Max(0f, diff / 1000f);
    }

    async Task SetupRelayAsClientAndStartNGO(string joinCode)
    {
        if (!transport || !networkManager)
        {
            SetStatus("Missing transport/networkManager");
            inFlow = false;
            return;
        }

        try
        {
            JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var serverData = new RelayServerData(join, "dtls");
            transport.SetRelayServerData(serverData);

            SetStatus("Client: starting NGO...");
            networkManager.StartClient();
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
            SetStatus("Client: Relay join failed");
            _ = CancelQuickPlayAsync();
        }
    }

    IEnumerator HostCountdownAndStart()
    {
        while (!hostRelayReady)
            yield return null;

        while (Time.realtimeSinceStartup < hostCountdownEndRealtime)
            yield return null;

        if (currentLobby == null) yield break;

        long startAtUnixMs =
            System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            + (long)(lobbyStartAnimDelay * 1000f)
            + (long)(fadeDuration * 1000f);

        var u = Lobbies.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions
        {
            Data = new System.Collections.Generic.Dictionary<string, DataObject>
            {
                { KEY_STATE,    new DataObject(DataObject.VisibilityOptions.Public, STATE_STARTING, DataObject.IndexOptions.S1) },
                { KEY_START_AT, new DataObject(DataObject.VisibilityOptions.Public, startAtUnixMs.ToString()) }
            }
        });
        while (!u.IsCompleted) yield return null;

        lastState = STATE_STARTING;
        lastStartAtUnixMs = startAtUnixMs;
        lastPlayers = currentLobby.Players != null ? currentLobby.Players.Count : 1;

        if (!startSequenceTriggered)
        {
            startSequenceTriggered = true;
            PlayLobbyStartOnAllRats();
            yield return new WaitForSeconds(lobbyStartAnimDelay);
            yield return FadeToBlack();
        }

        float waitEnd = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < waitEnd)
        {
            if (NetworkManager.Singleton != null)
            {
                int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
                int lobbyCount = currentLobby.Players != null ? currentLobby.Players.Count : 1;
                if (connected >= lobbyCount) break;
            }
            yield return null;
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

    void ApplyLobbyPreviewIfNeeded(Lobby lobby)
    {
        if (startSequenceTriggered) return;
        if (lobby == null || lobby.Players == null) return;

        int players = lobby.Players.Count;
        string state = (lobby.Data != null && lobby.Data.ContainsKey(KEY_STATE)) ? lobby.Data[KEY_STATE].Value : "";

        if (players == lastPreviewPlayers && state == lastPreviewState)
            return;

        lastPreviewPlayers = players;
        lastPreviewState = state;

        ApplyLobbyPreview(lobby);
    }

    void ApplyLobbyPreview(Lobby lobby)
    {
        if (lobbySlots == null || lobbySlots.Length == 0) return;

        if (slotInstances == null || slotInstances.Length != lobbySlots.Length)
            slotInstances = new GameObject[lobbySlots.Length];

        ClearLobbyPreview();

        if (lobby == null || lobby.Players == null) return;
        if (!lobbyRatPrefab) return;

        string localId = AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;
        int localIndex = -1;

        if (!string.IsNullOrEmpty(localId))
        {
            for (int i = 0; i < lobby.Players.Count; i++)
            {
                if (lobby.Players[i] != null && lobby.Players[i].Id == localId)
                {
                    localIndex = i;
                    break;
                }
            }
        }

        int count = Mathf.Min(lobbySlots.Length, lobby.Players.Count);

        for (int i = 0; i < count; i++)
        {
            var slot = lobbySlots[i];
            if (!slot) continue;

            slotInstances[i] = Instantiate(lobbyRatPrefab, slot.position, slot.rotation, slot);

            var slotView = slot.GetComponent<LobbySlot>();
            if (slotView) slotView.SetOccupied(true, i == localIndex);
        }
    }

    void ClearLobbyPreview()
    {
        if (lobbySlots != null)
        {
            for (int i = 0; i < lobbySlots.Length; i++)
            {
                var slot = lobbySlots[i];
                if (!slot) continue;

                var slotView = slot.GetComponent<LobbySlot>();
                if (slotView) slotView.Clear();
            }
        }

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
        clientConnectStarted = false;
        startSequenceTriggered = false;

        hostCountdownEndRealtime = 0f;
        hostRelayReady = false;

        lastPlayers = 0;
        lastState = "";
        lastStartAtUnixMs = -1;

        lastPreviewPlayers = -1;
        lastPreviewState = "";
    }

    async Task CleanupLobbyAsync()
    {
        if (currentLobby == null) return;

        try
        {
            if (AuthenticationService.Instance.IsSignedIn && currentLobby != null)
            {
                bool amHost = currentLobby.HostId == AuthenticationService.Instance.PlayerId;

                if (amHost)
                    await Lobbies.Instance.DeleteLobbyAsync(currentLobby.Id);
                else
                    await Lobbies.Instance.RemovePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId);
            }
        }
        catch { }
    }
}
