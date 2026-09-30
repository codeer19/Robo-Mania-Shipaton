using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Development-only evidence. Does not create bots or remote representations.</summary>
public sealed class PrivateRoomAIAudit : MonoBehaviour
{
    public static bool IsAI(Component c) => c is FortressBotAI || c is BotRespawnController ||
        c is BotIdentityApplier || c is RedBuildPlanner || c is SwarmBotAI ||
        c.GetType().Name == "BotController" || c.GetType().Name == "AIController";

    public static bool ContainsAI(GameObject root) => root != null &&
        root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && IsAI(c));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static string path;
    private static string previousState;
    private static int maximumActive;
    private float nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        previousState = null;
        maximumActive = 0;
        path = Path.Combine(Application.persistentDataPath, "private-room-ai-audit.log");
        File.WriteAllText(path, "Private room AI audit " + DateTime.UtcNow.ToString("O") + "\n");
        var root = new GameObject("PrivateRoomAIAudit");
        DontDestroyOnLoad(root);
        root.AddComponent<PrivateRoomAIAudit>();
        Application.logMessageReceived -= CaptureBotRequest;
        Application.logMessageReceived += CaptureBotRequest;
    }

    private static void CaptureBotRequest(string message, string stack, LogType type)
    {
        if (message.StartsWith("[BOT") || message.StartsWith("[PRIVATE ROOM VIOLATION]"))
            Write(message);
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Capture("SceneLoaded");

    private void Update()
    {
        if (MatchSessionContext.EntryMode != MatchEntryMode.PrivateRoom) return;
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 1f;
        string state = SceneManager.GetActiveScene().name + "/" + MatchSessionContext.Type + "/" +
            PrivateLobbySession.Instance?.State + "/" + FortressDuelManager.ActiveArena?.CurrentPhase + "/" +
            PrivateLobbyDirector.Instance?.CountPlayers() + "/" + (OnlineMatchDirector.Instance?.RemoteAvatar != null);
        if (state != previousState)
        {
            previousState = state;
            Capture("State=" + state);
        }
    }

    public static void Capture(string stage)
    {
        if (MatchSessionContext.EntryMode != MatchEntryMode.PrivateRoom) return;
        MatchSessionContext.AssertPrivateRoom(stage);
        var ai = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c != null && IsAI(c)).ToArray();
        var activeRoots = ai.Where(c => c.gameObject.activeInHierarchy).Select(c => c.gameObject).Distinct().ToArray();
        maximumActive = Math.Max(maximumActive, activeRoots.Length);
        var director = PrivateLobbyDirector.Instance;
        var runner = director != null ? director.Runner : OnlineMatchDirector.Instance?.Runner;
        string report = $"[AI AUDIT] Stage={stage} Scene={SceneManager.GetActiveScene().name} MatchType={MatchSessionContext.Type} EntryMode={MatchSessionContext.EntryMode} ActiveAI={activeRoots.Length} MaxActiveAI={maximumActive} Session={runner?.SessionInfo?.Name} LocalPlayer={runner?.LocalPlayer} PlayerCount={runner?.SessionInfo?.PlayerCount}";
        foreach (var group in ai.GroupBy(c => c.gameObject))
        {
            var obj = group.Key;
            string hierarchy = obj.name;
            for (Transform parent = obj.transform.parent; parent != null; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
            report += $"\nName={obj.name} Active={obj.activeInHierarchy} HierarchyPath={hierarchy} EntityID={obj.GetEntityId()} Components=" +
                string.Join(",", obj.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name));
        }
        foreach (var avatar in FindObjectsByType<NetworkedPlayerAvatar>(FindObjectsSortMode.None))
            if (avatar.Object != null && avatar.Object.IsValid)
                report += $"\n[HUMAN AVATAR] Name={avatar.name} NetworkId={avatar.Object.Id} Owner={avatar.OwnerPlayerRef} StateAuthority={avatar.Object.StateAuthority} IsLocal={avatar.IsLocalAvatar} HasAI={ContainsAI(avatar.gameObject)}";
        Write(report);
        Debug.Log(report);
        foreach (var obj in activeRoots)
        {
            Debug.LogError($"[PRIVATE ROOM VIOLATION] Active AI GameObject={obj.name} Scene={obj.scene.name}. See audit for components.");
            obj.SetActive(false);
        }
    }

    private static void Write(string message)
    {
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + message + "\n");
    }
#else
    public static void Capture(string stage) { }
#endif
}
