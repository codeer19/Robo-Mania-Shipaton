using Fusion;
using UnityEngine;

/// <summary>
/// One replicated record per human in a private room.
///
/// Carries only identifiers - a name, a skin id and a team - never meshes,
/// materials or prefabs. Each client owns its own record and is the only writer
/// of it; every other client reads it to draw the opponent side of the lobby.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkedLobbyPlayer : NetworkBehaviour
{
    private TeamSide? requestedTeam;
    private bool refreshRequested;
    [Networked] public NetworkString<_32> DisplayName { get; set; }
    [Networked] public NetworkString<_32> SkinId { get; set; }
    [Networked] public TeamSide Team { get; set; }
    [Networked] public NetworkBool SideAssigned { get; set; }

    /// <summary>True once this record carries a usable identity for the lobby.</summary>
    public bool HasIdentity =>
        !string.IsNullOrEmpty(DisplayName.Value) && !string.IsNullOrEmpty(SkinId.Value);

    public bool IsLocalRecord => HasStateAuthority;
    public PlayerRef Owner => Object.StateAuthority;

    /// <summary>Every lobby record currently known to this client, local and remote.</summary>
    public static readonly System.Collections.Generic.List<NetworkedLobbyPlayer> All =
        new System.Collections.Generic.List<NetworkedLobbyPlayer>();

    public override void Spawned()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }

        if (HasStateAuthority)
        {
            PublishLocalIdentity();
        }

        Debug.Log(
            $"[LOBBY PROFILE] PlayerRef={Object.StateAuthority} " +
            $"Name={DisplayName.Value} Skin={SkinId.Value} Team={Team} " +
            $"local={HasStateAuthority}");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        All.Remove(this);
        Debug.Log($"[LOBBY PROFILE] Record removed for {(Object != null ? Object.StateAuthority.ToString() : "?")}.");
    }

    /// <summary>
    /// Writes this device's real profile into the replicated record.
    ///
    /// The name comes from the player's own profile rather than a generated one:
    /// showing a real human a made-up handle would misrepresent who they are
    /// playing against. Generated names belong only to the AI fallback match.
    /// </summary>
    private void PublishLocalIdentity()
    {
        // The overrides are only ever set when this process was launched as a
        // replication test client; in normal play both are null and the real
        // profile is published.
        string name = NetworkTestHarness.NameOverride ?? PlayerProfileService.DisplayName;
        string skin = NetworkTestHarness.SkinOverride ?? PlayerProfileService.CurrentEquippedSkinId;

        DisplayName = Truncate(name, 31);
        SkinId = Truncate(skin, 31);
    }

    /// <summary>
    /// Assigns the team this player holds for the whole match.
    ///
    /// Called by the owner once the creator is known, so both clients derive the
    /// same answer from the same replicated value rather than from anything local
    /// such as spawn order, hierarchy position or who is rendering the record.
    /// </summary>
    public void AssignTeam(TeamSide team)
    {
        if (!HasStateAuthority || (SideAssigned && Team == team))
        {
            return;
        }

        requestedTeam = team;
    }

    /// <summary>Republishes the identity after the player changes skins in the shop.</summary>
    public void RefreshIdentity()
    {
        if (HasStateAuthority)
        {
            refreshRequested = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (requestedTeam.HasValue)
        {
            Team = requestedTeam.Value;
            SideAssigned = true;
            requestedTeam = null;
            Debug.Log($"[TEAM] Player={Object.StateAuthority} Team={Team}");
        }
        if (refreshRequested)
        {
            refreshRequested = false;
            PublishLocalIdentity();
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    /// <summary>The record owned by this client, or null before it spawns.</summary>
    public static NetworkedLobbyPlayer Local
    {
        get
        {
            foreach (NetworkedLobbyPlayer record in All)
            {
                if (record != null && record.IsLocalRecord) return record;
            }

            return null;
        }
    }

    /// <summary>The opponent's record, or null while the room holds one player.</summary>
    public static NetworkedLobbyPlayer Remote
    {
        get
        {
            foreach (NetworkedLobbyPlayer record in All)
            {
                if (record != null && !record.IsLocalRecord) return record;
            }

            return null;
        }
    }
}
