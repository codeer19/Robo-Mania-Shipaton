using UnityEngine;

/// <summary>The only activation path for the authored offline opponent.</summary>
[DisallowMultipleComponent]
public sealed class BotMatchBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject botRoot;
    [SerializeField] private GameObject plannerRoot;
    private bool initialized;
    public bool InitializeBotMatch()
    {
        if (!BotMatchPolicy.RequireBotMatch("BotMatchBootstrap.InitializeBotMatch", this)) return false;
        if (initialized) return true;
        if (botRoot == null || plannerRoot == null)
        {
            Debug.LogError("[BOT BLOCKED] Bot match scene references are missing.", this);
            return false;
        }
        Debug.Log($"[BOT SPAWN REQUEST] MatchType={MatchSessionContext.Type} EntryMode={MatchSessionContext.EntryMode} Scene={gameObject.scene.name}\n{System.Environment.StackTrace}");
        initialized = true;
        botRoot.SetActive(true);
        var identity = botRoot.GetComponent<BotIdentityApplier>();
        if (identity != null) identity.Apply();
        plannerRoot.SetActive(true);
        return true;
    }
}

public static class BotMatchPolicy
{
    public static bool RequireBotMatch(string callSite, Object context = null)
    {
        if (MatchSessionContext.CanInitializeAI) return true;
        Debug.LogError($"[BOT BLOCKED] {callSite} MatchType={MatchSessionContext.Type} EntryMode={MatchSessionContext.EntryMode}\n{System.Environment.StackTrace}", context);
        return false;
    }

    public static bool AllowActivation(Behaviour component, string callSite)
    {
        if (RequireBotMatch(callSite, component)) return true;
        // Diagnostic backstop for an invalid caller. Normal scenes never activate these roots.
        component.gameObject.SetActive(false);
        return false;
    }
}
