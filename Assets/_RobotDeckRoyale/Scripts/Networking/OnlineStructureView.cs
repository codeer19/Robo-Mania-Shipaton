using Fusion;
using UnityEngine;

/// <summary>A collider and art view of one authoritative structure record. Never runs offline attacks.</summary>
public sealed class OnlineStructureView : MonoBehaviour
{
    public int Slot { get; private set; }
    public bool IsPreview { get; private set; }
    public PlayerRef OwnerPlayerRef { get; private set; }
    public TeamSide OwnerSide { get; private set; }
    private Damageable health;
    private Transform head;
    private BuildStructure card;
    private int lastHealth = -1;

    public static OnlineStructureView Create(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, int slot, bool preview = false)
    {
        // An inactive parent keeps Start/Update from firing before the offline components are disabled.
        var staging = new GameObject("Structure staging");
        staging.SetActive(false);
        var root = Instantiate(prefab, position, rotation, staging.transform);
        var view = root.AddComponent<OnlineStructureView>();
        view.Slot = slot; view.IsPreview = preview;
        view.health = root.GetComponent<Damageable>();
        var turret = root.GetComponent<AutoTurret>();
        if (turret != null) { view.head = turret.RotatingHead; turret.enabled = false; }
        // The build cards present replicated state only; the authority runs them.
        view.card = root.GetComponent<BuildStructure>();
        if (view.card != null) view.card.SetOnlineView(true);
        if (preview) BuildPlacementController.DisableForPreview(root);
        root.transform.SetParent(parent, true);
        BuildableGrounding.SnapToGround(root, position.y);
        Destroy(staging);
        return view;
    }

    public void Apply(OnlineStructure structure)
    {
        OwnerPlayerRef = structure.OwnerPlayerRef; OwnerSide = structure.OwnerSide;
        health.ApplyReplicatedHealth(structure.Health, structure.MaximumHealth);
        if (head != null) head.rotation = structure.HeadRotation;
        // Each card turns its replicated counters (pulse, heal, boost, burst,
        // interception) into the same visuals it shows offline.
        if (card != null) card.ApplyOnline(structure);
        // Shot down this frame: the same break effect as offline before hiding.
        if (lastHealth > 0 && structure.Health <= 0)
        {
            var death = GetComponent<DeathEffectOnDestroy>();
            if (death != null) death.PlayDeathEffectOnly();
        }
        lastHealth = structure.Health;
        gameObject.SetActive(structure.Health > 0);
    }
}
