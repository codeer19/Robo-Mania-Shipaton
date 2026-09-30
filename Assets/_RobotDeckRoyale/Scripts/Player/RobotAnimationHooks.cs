using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation only. Combat and recharge remain authoritative in their existing owners.</summary>
[DefaultExecutionOrder(100), DisallowMultipleComponent]
public class RobotAnimationHooks : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Damageable damageable;
    [SerializeField] private RobotBlaster blaster;
    [SerializeField] private CharacterController movementSource;
    [SerializeField] private string fireTrigger = "Fire";
    [SerializeField] private string hitTrigger = "Hit";
    [SerializeField] private string deathTrigger = "Death";
    [SerializeField] private string respawnTrigger = "Respawn";
    [SerializeField] private string moveSpeedFloat = "MoveSpeed";
    [SerializeField, Min(0.1f)] private float speedForFullBlend = 5.5f;
    private readonly Dictionary<int, AnimatorControllerParameterType> parameters = new Dictionary<int, AnimatorControllerParameterType>();
    private RuntimeAnimatorController cachedController;
    private FortressBotAI bot;
    private FortressDuelManager match;
    private Transform movementRoot;
    private Vector3 previousPosition, previousForward;
    private bool wasDead, pendingFire, pendingHit;
    private float lastHit = float.NegativeInfinity;
    private int result, weaponLayer = -1, impactLayer = -1;
    private int fireId, hitId, deathId, respawnId, speedId;
    private static readonly int DeadId = Animator.StringToHash("Dead");
    private static readonly int CombatId = Animator.StringToHash("CombatActive");
    private static readonly int TurnId = Animator.StringToHash("TurnRate");
    private static readonly int ReloadId = Animator.StringToHash("Reloading");
    private static readonly int ReloadPhaseId = Animator.StringToHash("ReloadPhase");
    private static readonly int ResultId = Animator.StringToHash("Result");
    private static readonly int FireState = Animator.StringToHash("Fire");
    private static readonly int ReloadState = Animator.StringToHash("Reload");
    private static readonly int HitState = Animator.StringToHash("TakeHit");
    public bool HasAuthoredAnimator => animator != null && animator.runtimeAnimatorController != null;
    public bool SuppressProcedural { get; private set; }
    public float DeathVisualSeconds => HasAuthoredAnimator ? 0.9f : 0f;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (damageable == null) damageable = GetComponentInParent<Damageable>();
        if (blaster == null) blaster = GetComponentInParent<RobotBlaster>();
        if (movementSource == null) movementSource = GetComponentInParent<CharacterController>();
        bot = GetComponentInParent<FortressBotAI>();
        match = FindFirstObjectByType<FortressDuelManager>();
        movementRoot = damageable != null ? damageable.transform : transform.parent;
        if (movementRoot == null) movementRoot = transform;
        fireId = Hash(fireTrigger); hitId = Hash(hitTrigger);
        deathId = Hash(deathTrigger); respawnId = Hash(respawnTrigger); speedId = Hash(moveSpeedFloat);
    }
    private void OnEnable()
    {
        if (damageable != null) { damageable.Damaged += HandleDamaged; damageable.Died += HandleDied; }
        if (blaster != null) blaster.Fired += HandleFired;
        ResetPresentation();
    }
    private void OnDisable()
    {
        if (damageable != null) { damageable.Damaged -= HandleDamaged; damageable.Died -= HandleDied; }
        if (blaster != null) blaster.Fired -= HandleFired;
        pendingFire = pendingHit = false;
    }
    public void ResetPresentation()
    {
        pendingFire = pendingHit = false; lastHit = float.NegativeInfinity; result = 0;
        wasDead = damageable != null && damageable.IsDead;
        if (movementRoot != null) { previousPosition = movementRoot.position; previousForward = movementRoot.forward; }
        if (!HasAuthoredAnimator) return;
        CacheParameters();
        animator.Rebind();
        if (weaponLayer >= 0) animator.SetLayerWeight(weaponLayer, 0f);
        if (impactLayer >= 0) animator.SetLayerWeight(impactLayer, 0f);
        SetBool(DeadId, wasDead);
        string state = wasDead ? "Body.Death" : "Body.Spawn";
        if (animator.HasState(0, Animator.StringToHash(state))) animator.Play(state, 0, 0f);
        SuppressProcedural = true;
    }
    private void CacheParameters()
    {
        cachedController = animator.runtimeAnimatorController; parameters.Clear();
        foreach (var p in animator.parameters) parameters[p.nameHash] = p.type;
        weaponLayer = animator.GetLayerIndex("Weapon"); impactLayer = animator.GetLayerIndex("Impact");
    }
    private void Update()
    {
        if (!HasAuthoredAnimator) return;
        if (cachedController != animator.runtimeAnimatorController) ResetPresentation();
        bool dead = damageable != null && damageable.IsDead;
        if (wasDead && !dead) { ResetPresentation(); Trigger(respawnId); }
        wasDead = dead;
        bool combat = !dead && result == 0 && (match == null || match.CurrentPhase == FortressDuelPhase.Combat);
        SetBool(DeadId, dead); SetBool(CombatId, combat);
        if (Has(ResultId, AnimatorControllerParameterType.Int)) animator.SetInteger(ResultId, result);
        var body = animator.GetCurrentAnimatorStateInfo(0);
        SuppressProcedural = dead || result != 0 || body.IsName("Spawn");
        float dt = Time.deltaTime;
        Vector3 delta = movementRoot.position - previousPosition;
        Vector3 velocity = dt > 0f && delta.sqrMagnitude < 36f ? delta / dt : Vector3.zero;
        velocity.y = 0f;
        // Spawn acting never consumes an accepted shot or holds back player movement.
        if (combat && body.IsName("Spawn") && (pendingFire || velocity.sqrMagnitude > 0.25f))
        {
            animator.CrossFadeInFixedTime("Body.Locomotion", 0.03f, 0);
            SuppressProcedural = false;
        }
        SetFloat(speedId, dead ? 0f : Mathf.Clamp01(velocity.magnitude / speedForFullBlend), 0.08f);
        float turn = dt > 0f ? Vector3.SignedAngle(previousForward, movementRoot.forward, Vector3.up) / dt / 360f : 0f;
        SetFloat(TurnId, Mathf.Clamp(turn, -1f, 1f), 0.06f);
        previousPosition = movementRoot.position; previousForward = movementRoot.forward;
        bool reload = combat && (blaster != null ? blaster.CurrentAmmo < blaster.MaxAmmo : bot != null && bot.CurrentAmmo < bot.MaxAmmo);
        SetBool(ReloadId, reload);
        SetFloat(ReloadPhaseId, blaster != null ? blaster.ReloadProgress : bot != null ? bot.ReloadProgress : 1f);
        if (SuppressProcedural || !combat)
        {
            pendingFire = pendingHit = false;
            if (Has(fireId, AnimatorControllerParameterType.Trigger)) animator.ResetTrigger(fireId);
            if (Has(hitId, AnimatorControllerParameterType.Trigger)) animator.ResetTrigger(hitId);
            if (weaponLayer >= 0) animator.SetLayerWeight(weaponLayer, 0f);
            if (impactLayer >= 0) animator.SetLayerWeight(impactLayer, 0f);
            return;
        }
        if (pendingFire) Trigger(fireId);
        if (pendingHit) Trigger(hitId);
        if (weaponLayer >= 0)
        {
            bool active = pendingFire || reload || LayerInState(weaponLayer, FireState) || LayerInState(weaponLayer, ReloadState);
            animator.SetLayerWeight(weaponLayer, active ? 1f : Mathf.MoveTowards(animator.GetLayerWeight(weaponLayer), 0f, dt / 0.08f));
        }
        if (impactLayer >= 0) animator.SetLayerWeight(impactLayer, pendingHit || LayerInState(impactLayer, HitState) ? 1f : 0f);
        pendingFire = pendingHit = false;
    }
    private bool LayerInState(int layer, int state) => animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == state ||
        (animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).shortNameHash == state);
    public void NotifyFired() { if (damageable == null || !damageable.IsDead) pendingFire = true; }
    public void ShowResult(bool won) { result = won ? 1 : 2; }
    private void HandleFired(RobotBlaster source) => NotifyFired();
    private void HandleDamaged(Damageable target, int amount)
    {
        if (target.IsDead || Time.time - lastHit < 0.08f) return;
        pendingHit = true; lastHit = Time.time;
    }
    private void HandleDied(Damageable target)
    {
        pendingFire = pendingHit = false;
        if (!HasAuthoredAnimator) return;
        SetBool(DeadId, true); Trigger(deathId); SuppressProcedural = true;
    }
    private static int Hash(string value) => string.IsNullOrEmpty(value) ? 0 : Animator.StringToHash(value);
    private bool Has(int id, AnimatorControllerParameterType type) => id != 0 && parameters.TryGetValue(id, out var found) && found == type;
    private void Trigger(int id) { if (Has(id, AnimatorControllerParameterType.Trigger)) animator.SetTrigger(id); }
    private void SetBool(int id, bool value) { if (Has(id, AnimatorControllerParameterType.Bool)) animator.SetBool(id, value); }
    private void SetFloat(int id, float value, float damping = 0f)
    {
        if (!Has(id, AnimatorControllerParameterType.Float)) return;
        if (damping > 0f) animator.SetFloat(id, value, damping, Time.deltaTime);
        else animator.SetFloat(id, value);
    }
}
