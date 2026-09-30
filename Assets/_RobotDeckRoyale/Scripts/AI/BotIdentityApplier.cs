using UnityEngine;

/// <summary>
/// Gives the fallback AI opponent its own name, skin and play style.
///
/// Applied to the bot root. Runs once at match start, and only when the opponent
/// is actually AI - in a human match the opponent's identity comes from the other
/// player, not from here.
/// </summary>
[DisallowMultipleComponent]
public sealed class BotIdentityApplier : MonoBehaviour
{
    [Tooltip("Leave empty to resolve the bot AI on this object.")]
    [SerializeField] private FortressBotAI botAI;

    [Tooltip("Visual root the skin is applied to. Defaults to this object.")]
    [SerializeField] private Transform visualRoot;

    public BotIdentity Identity { get; private set; }
    public bool HasApplied { get; private set; }

    private void Start()
    {
        // Deliberately not in Awake: the skin system and the duel manager both need
        // to have finished their own setup first.
        Apply();
    }

    public void Apply()
    {
        if (!BotMatchPolicy.RequireBotMatch("BotIdentityApplier.Apply", this)) return;
        if (HasApplied)
        {
            return;
        }

        HasApplied = true;

        // Never dress a real opponent. Overwriting a human player's name and skin
        // with generated ones would present them as someone they are not.

        if (botAI == null)
        {
            botAI = GetComponent<FortressBotAI>();
        }

        if (botAI == null)
        {
            Debug.Log("[BOT_FALLBACK] No FortressBotAI here; leaving opponent identity untouched.");
            return;
        }

        string playerSkin = PlayerProfileService.CurrentEquippedSkinId;
        Identity = BotIdentityProvider.Create(playerSkin);

        botAI.ApplyBehaviourProfile(Identity.Profile);
        ApplySkin(Identity.SkinId);
        ApplyName(Identity.DisplayName);
    }

    /// <summary>Applies the already-authorized bot identity to its visual.</summary>
    private void ApplySkin(string skinId)
    {
        if (string.IsNullOrEmpty(skinId))
        {
            return;
        }

        Transform target = visualRoot != null ? visualRoot : transform;
        RobotCosmeticApplier.ApplySkin(target.gameObject, skinId);
        Debug.Log($"[SKIN] Bot wearing {skinId}.");
    }

    private void ApplyName(string displayName)
    {
        FortressDuelManager manager = FindAnyObjectByType<FortressDuelManager>();
        if (manager == null)
        {
            return;
        }

        manager.SetOpponentDisplayName(displayName);
        Debug.Log($"[BOT_FALLBACK] Opponent presented as \"{displayName}\".");
    }
}

