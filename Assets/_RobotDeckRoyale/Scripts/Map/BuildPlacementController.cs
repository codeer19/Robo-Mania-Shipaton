using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class BuildPlacementController : MonoBehaviour
{
    /// <summary>
    /// The six build cards (see <see cref="BuildCards"/>). Sent over the network as
    /// ints, so the values are part of the online protocol.
    /// </summary>
    public enum BuildableType
    {
        Turret,
        PulseTower,
        HealingPad,
        // Value 3 was the Mine. Overdrive Pad takes its number, so saved
        // loadouts that held the Mine now hold the Overdrive Pad.
        OverdrivePad,
        RecoveryJammer,
        MissileInterceptor
    }

    /// <summary>
    /// The painted territory line (TerritoryMarkerBuilder draws it from these):
    /// |z| = Depth - Bow * cos(pi * x / (2 * HalfSpan)). Nothing may be placed on
    /// or past it; it bows toward the centre, 9 m out in the middle, 12.85 m at the sides.
    /// </summary>
    public const float TerritoryEdgeDepth = 12.85f, TerritoryEdgeBow = 3.85f, TerritoryHalfSpan = 22.9f;
    /// <summary>Clearance behind the line, covering the painted dash width.</summary>
    public const float TerritoryEdgeMargin = 0.3f;
    public static float TerritoryEdge(float x) =>
        TerritoryEdgeDepth - TerritoryEdgeBow * Mathf.Cos(Mathf.PI * Mathf.Clamp(x, -TerritoryHalfSpan, TerritoryHalfSpan) / (2f * TerritoryHalfSpan));

    /// <summary>Every player spends from the same budget each build phase.</summary>
    public const int BuildPointsPerRound = 3;
    public const int BuildItemCost = 1;

    public static string DisplayName(BuildableType type) => BuildCards.DisplayName(type);

    private class PlacedBuildable
    {
        public GameObject Object;
        public BuildableType Type;
    }

    [Header("References")]
    [SerializeField] private FortressDuelManager duelManager;
    [SerializeField] private BoxCollider localBuildArea;
    [SerializeField] private BoxCollider opponentBuildArea;

    [Header("Card Prefabs")]
    [SerializeField] private GameObject turretPrefab;
    [SerializeField] private GameObject pulseTowerPrefab;
    [SerializeField] private GameObject healingPadPrefab;
    [SerializeField, FormerlySerializedAs("minePrefab")] private GameObject overdrivePadPrefab;
    [SerializeField] private GameObject jammerPrefab;
    [SerializeField] private GameObject interceptorPrefab;

    [Header("Placement Rules")]
    [SerializeField] private float gridSize = 1f;
    [SerializeField] private float minimumSpacing = 3f;
    [SerializeField] private float placementY = 0f;
    [SerializeField] private LayerMask buildBlockerMask;
    [SerializeField, Tooltip("No structure may be placed this close (flat metres) to either robot spawn.")]
    private float spawnClearance = 3f;
    [SerializeField, Tooltip("Extra margin kept clear around both bases.")]
    private float baseClearance = 1.2f;

    [Header("Footprints (half extents)")]
    [SerializeField] private Vector3 turretCheckHalfExtents = new Vector3(1f, 0.8f, 1.5f);
    [SerializeField] private Vector3 pulseTowerCheckHalfExtents = new Vector3(1.1f, 0.9f, 1.1f);
    [SerializeField] private Vector3 healingPadCheckHalfExtents = new Vector3(1.25f, 0.3f, 1.25f);
    [SerializeField, FormerlySerializedAs("mineCheckHalfExtents")] private Vector3 overdriveCheckHalfExtents = new Vector3(1.3f, 0.3f, 1.3f);
    [SerializeField] private Vector3 jammerCheckHalfExtents = new Vector3(1f, 0.9f, 1f);
    [SerializeField] private Vector3 interceptorCheckHalfExtents = new Vector3(1.1f, 0.9f, 1.1f);

    [Header("Preview Colors")]
    [SerializeField] private Color validPlacementColor =
        new Color(0.25f, 1f, 0.35f, 1f);

    [SerializeField] private Color invalidPlacementColor =
        new Color(1f, 0.2f, 0.2f, 1f);

    private readonly List<PlacedBuildable> placedBuildables =
        new List<PlacedBuildable>();

    private Camera mainCamera;
    private GameObject previewObject;
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewBlock;
    private bool previewIsVisible;
    private BuildableType[] loadout = (BuildableType[])BuildCards.DefaultLoadout.Clone();
    private BuildableType selectedBuildable = BuildableType.Turret;
    private readonly Dictionary<BuildableType, Component> tuning = new Dictionary<BuildableType, Component>();

    // Touch state. A build touch is tracked from press to release so the
    // preview can be dragged into place before it is committed, which is the
    // only workable pattern on a phone: there is no hover, so a plain tap would
    // have to place blind.
    private int activeTouchId = -1;

    /// <summary>Raised whenever the selection or a placement count changes, so a HUD can refresh.</summary>
    public event Action BuildStateChanged;

    public BuildableType SelectedBuildable => selectedBuildable;

    /// <summary>The three cards this player brought (slot order = keys 1, 2, 3).</summary>
    public IReadOnlyList<BuildableType> Loadout => loadout;
    public bool InLoadout(BuildableType type) => Array.IndexOf(loadout, type) >= 0;

    private bool Online => MatchSessionContext.Type == MatchType.HumanOnline;
    private static bool OnlinePhaseIs(OnlineCombatPhase phase) =>
        NetworkedMatchState.Instance != null && NetworkedMatchState.Instance.Phase == phase;

    /// <summary>The opening build phase, which every online match now starts with.</summary>
    public bool IsMandatoryBuildPhase => Online && OnlinePhaseIs(OnlineCombatPhase.Build);
    public bool IsBuildPhase => Online
        ? IsMandatoryBuildPhase
        : duelManager != null && duelManager.CurrentPhase == FortressDuelPhase.Build;
    public bool CanToggleOnline => IsMandatoryBuildPhase;
    // Kept for existing bindings; combat can never reopen placement.
    public void ToggleOnlineBuild() { }

    public int Limit(BuildableType type) => BuildCards.IsDefined(type) ? BuildCards.MaxCopies : 0;

    public GameObject Prefab(BuildableType type) => type switch
    {
        BuildableType.Turret => turretPrefab,
        BuildableType.PulseTower => pulseTowerPrefab,
        BuildableType.HealingPad => healingPadPrefab,
        BuildableType.OverdrivePad => overdrivePadPrefab,
        BuildableType.RecoveryJammer => jammerPrefab,
        BuildableType.MissileInterceptor => interceptorPrefab,
        _ => null
    };

    // Tuning is authored once, on each card's prefab component; the online
    // authority simulates from exactly these values.
    private T Tuning<T>(BuildableType type) where T : Component
    {
        if (tuning.TryGetValue(type, out var cached) && cached != null) return (T)cached;
        var prefab = Prefab(type);
        var component = prefab != null ? prefab.GetComponent<T>() : null;
        tuning[type] = component;
        return component;
    }
    public AutoTurret Turret => Tuning<AutoTurret>(BuildableType.Turret);
    public PulseTower Pulse => Tuning<PulseTower>(BuildableType.PulseTower);
    public HealingPad Pad => Tuning<HealingPad>(BuildableType.HealingPad);
    public OverdrivePad Overdrive => Tuning<OverdrivePad>(BuildableType.OverdrivePad);
    public RecoveryJammer Jammer => Tuning<RecoveryJammer>(BuildableType.RecoveryJammer);
    public MissileInterceptor Interceptor => Tuning<MissileInterceptor>(BuildableType.MissileInterceptor);

    public Vector3 CheckHalfExtents(BuildableType type) => type switch
    {
        BuildableType.PulseTower => pulseTowerCheckHalfExtents,
        BuildableType.HealingPad => healingPadCheckHalfExtents,
        BuildableType.OverdrivePad => overdriveCheckHalfExtents,
        BuildableType.RecoveryJammer => jammerCheckHalfExtents,
        BuildableType.MissileInterceptor => interceptorCheckHalfExtents,
        _ => turretCheckHalfExtents
    };

    public Vector3 SnapOnline(Vector3 position) { position = SnapToGrid(position); position.y = placementY; return position; }

    /// <summary>Structures face the enemy side; the rotation a client sends is ignored.</summary>
    public Quaternion RotationOnline(BuildableType type, Quaternion rotation, TeamSide side) => FacingFor(side);
    public static Quaternion FacingFor(TeamSide side) => Quaternion.Euler(0f, side == TeamSide.SideA ? 0f : 180f, 0f);

    /// <summary>
    /// The one placement rule set, used by the local player, the bot and the
    /// online authority alike. It is decided by the authoritative TeamSide - never
    /// by the local blue/red presentation:
    ///  - the whole footprint lies inside that side's build zone and behind that
    ///    side's painted territory line (the middle of the arena is no-build);
    ///  - clear of both robot spawns and both bases;
    ///  - no scenery in the way, and spacing from other structures.
    /// </summary>
    public bool IsLegalPlacement(TeamSide side, BuildableType type, Vector3 position, Quaternion rotation, Func<Vector3, bool> tooCloseToStructure)
    {
        if (!BuildCards.IsDefined(type)) return false;
        var zone = side == TeamSide.SideA ? localBuildArea : opponentBuildArea;
        if (zone == null) return false;
        Vector3 extents = CheckHalfExtents(type);
        Bounds bounds = zone.bounds;
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = position + rotation * new Vector3(x * extents.x, 0f, z * extents.z);
                corner.y = bounds.center.y;
                if (!bounds.Contains(corner)) return false;
                // SideA owns -z, SideB +z. Corners suffice: the line is closest to
                // the centre in the middle, so an edge between two legal corners
                // never crosses it.
                float depth = side == TeamSide.SideA ? -corner.z : corner.z;
                if (depth < TerritoryEdge(corner.x) + TerritoryEdgeMargin) return false;
            }

        if (duelManager != null)
        {
            float footprint = Mathf.Max(extents.x, extents.z);
            for (int s = 0; s < 2; s++)
            {
                Transform spawn = duelManager.Spawn((TeamSide)s);
                if (spawn != null && FlatDistance(spawn.position, position) < spawnClearance + footprint * 0.5f) return false;
                var baseColliders = duelManager.HeistColliders((TeamSide)s);
                if (baseColliders == null) continue;
                foreach (var collider in baseColliders)
                {
                    if (collider == null) continue;
                    Bounds b = collider.bounds;
                    b.Expand(new Vector3(2f * (baseClearance + footprint), 100f, 2f * (baseClearance + footprint)));
                    if (b.Contains(new Vector3(position.x, b.center.y, position.z))) return false;
                }
            }
        }

        if (tooCloseToStructure != null && tooCloseToStructure(position)) return false;
        return !Physics.CheckBox(position + Vector3.up * .9f, extents, rotation, buildBlockerMask, QueryTriggerInteraction.Ignore);
    }

    public bool ValidateOnline(TeamSide side, BuildableType type, Vector3 position, Quaternion rotation, NetworkedMatchState state) =>
        IsLegalPlacement(side, type, position, rotation, p =>
        {
            for (int i = 0; i < NetworkedMatchState.StructureCapacity; i++)
            {
                var structure = state.Structures[i];
                if (structure.Health > 0 && FlatDistance(p, structure.Position) < minimumSpacing) return true;
            }
            return false;
        });

    public float MinimumSpacing => minimumSpacing;
    public float PlacementY => placementY;
    public float GridSize => gridSize;
    public BoxCollider BuildArea(TeamSide side) => side == TeamSide.SideA ? localBuildArea : opponentBuildArea;

    private int lastOnlineInventory = -1;

    private void Awake()
    {
        previewBlock = new MaterialPropertyBlock();
        mainCamera = Camera.main;

        if (duelManager == null)
        {
            duelManager = GetComponent<FortressDuelManager>();
        }

        // The loadout is fixed for the match: chosen in the menu, published on
        // the avatar online, and read once here.
        loadout = PlayerProfileService.BuildLoadout;
        selectedBuildable = loadout[0];
    }

    private void Start()
    {
        CreatePreviewObject();
    }

    private void Update()
    {
        if (Online && NetworkedMatchState.Instance != null)
        {
            int inventory = BuildPointsRemaining * 1000;
            for (int i = 0; i < loadout.Length; i++) inventory += GetRemaining(loadout[i]) * (int)Mathf.Pow(10, i);
            if (inventory != lastOnlineInventory) { lastOnlineInventory = inventory; BuildStateChanged?.Invoke(); }
        }
        int before = placedBuildables.Count;
        RemoveDestroyedBuildables();
        HandleBuildableSelection();

        if (before != placedBuildables.Count)
        {
            BuildStateChanged?.Invoke();
        }

        if (!IsBuildPhase || mainCamera == null)
        {
            activeTouchId = -1;
            SetPreviewVisible(false);
            return;
        }

        if (UsesTouch())
        {
            UpdateTouchPlacement();
            return;
        }

        UpdatePointerPlacement();
    }

    private static bool UsesTouch()
    {
        return Touchscreen.current != null &&
               (Application.isMobilePlatform || Mouse.current == null);
    }

    private void UpdatePointerPlacement()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        { SetPreviewVisible(false); return; }
        if (Mouse.current == null)
        {
            SetPreviewVisible(false);
            return;
        }

        if (!TryGetPlacementPosition(Mouse.current.position.ReadValue(),
                out Vector3 placementPosition))
        {
            SetPreviewVisible(false);
            return;
        }

        bool isValid = IsPlacementValid(placementPosition);
        UpdatePreview(placementPosition, isValid);

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            UndoLastPlacement();
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && isValid)
        {
            PlaceBuildable(placementPosition);
        }
    }

    /// <summary>
    /// Press-drag-release placement. Pressing the ground shows the preview,
    /// dragging positions it, and lifting commits it when the spot is legal.
    /// Touches that start on the build HUD are ignored so a button press never
    /// drops a structure behind the button.
    /// </summary>
    private void UpdateTouchPlacement()
    {
        bool stillDown = false;

        foreach (TouchControl touch in Touchscreen.current.touches)
        {
            int id = touch.touchId.ReadValue();
            Vector2 position = touch.position.ReadValue();

            if (activeTouchId == -1)
            {
                if (!touch.press.wasPressedThisFrame)
                    continue;

                if (IsPointerOverBuildUI(id))
                    continue;

                activeTouchId = id;
                stillDown = true;
                break;
            }

            if (id != activeTouchId)
                continue;

            if (touch.press.isPressed)
            {
                stillDown = true;

                if (TryGetPlacementPosition(position, out Vector3 dragged))
                {
                    UpdatePreview(dragged, IsPlacementValid(dragged));
                }
                else
                {
                    SetPreviewVisible(false);
                }
            }
            else
            {
                // Released: commit if the spot is legal.
                if (TryGetPlacementPosition(position, out Vector3 released) &&
                    IsPlacementValid(released))
                {
                    PlaceBuildable(released);
                }

                activeTouchId = -1;
                SetPreviewVisible(false);
            }

            break;
        }

        if (activeTouchId != -1 && !stillDown)
        {
            activeTouchId = -1;
            SetPreviewVisible(false);
        }

        if (activeTouchId == -1)
        {
            SetPreviewVisible(false);
        }
    }

    private static bool IsPointerOverBuildUI(int touchId)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(touchId);
    }

    // ---------------------------------------------------------------------
    // Public API for the build HUD
    // ---------------------------------------------------------------------

    public void SelectBuildableType(BuildableType type)
    {
        if (!IsBuildPhase || !InLoadout(type)) return;

        SelectBuildable(type);
        BuildStateChanged?.Invoke();
    }

    /// <summary>Selects the card in loadout slot 0, 1 or 2.</summary>
    public void SelectSlot(int slot)
    {
        if (slot < 0 || slot >= loadout.Length) return;
        SelectBuildableType(loadout[slot]);
    }

    public void UndoLast()
    {
        UndoLastPlacement();
        BuildStateChanged?.Invoke();
    }

    /// <summary>Placements left for one card: in the loadout, not yet placed (one use), and a point to spend.</summary>
    public int GetRemaining(BuildableType type)
    {
        if (!InLoadout(type)) return 0;
        if (Online) return NetworkedMatchState.Instance != null ? NetworkedMatchState.Instance.Remaining(MatchSessionContext.LocalSide, type) : Limit(type);
        return Mathf.Max(0, Mathf.Min(Limit(type) - CountPlacedBuildables(type), BuildPointsRemaining / BuildItemCost));
    }

    /// <summary>Build points left this round (3, 2, 1, 0).</summary>
    public int BuildPointsRemaining
    {
        get
        {
            if (Online) return NetworkedMatchState.Instance != null ? NetworkedMatchState.Instance.BuildPointsRemaining(MatchSessionContext.LocalSide) : BuildPointsPerRound;
            int spent = 0;
            foreach (PlacedBuildable buildable in placedBuildables)
                if (buildable.Object != null) spent += BuildItemCost;
            return Mathf.Max(0, BuildPointsPerRound - spent);
        }
    }

    // 1, 2, 3 are the equipped slots - never fixed card types.
    private void HandleBuildableSelection()
    {
        if (Keyboard.current == null || !IsBuildPhase)
        {
            return;
        }

        if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) SelectSlot(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) SelectSlot(1);
        if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame) SelectSlot(2);
    }

    private void SelectBuildable(BuildableType buildableType)
    {
        if (selectedBuildable == buildableType)
        {
            return;
        }

        selectedBuildable = buildableType;
        CreatePreviewObject();
        BuildStateChanged?.Invoke();
    }

    /// <summary>After a card runs out, move the selection to the next slot that still has placements.</summary>
    private void SelectNextAvailableIfExhausted()
    {
        if (GetRemaining(selectedBuildable) > 0 || BuildPointsRemaining <= 0) return;
        int start = Array.IndexOf(loadout, selectedBuildable);
        for (int step = 1; step <= loadout.Length; step++)
        {
            BuildableType next = loadout[(start + step) % loadout.Length];
            if (GetRemaining(next) > 0) { SelectBuildable(next); return; }
        }
    }

    private void CreatePreviewObject()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
        }

        GameObject selectedPrefab = Prefab(selectedBuildable);

        if (selectedPrefab == null)
        {
            return;
        }

        previewObject = Online ? OnlineStructureView.Create(selectedPrefab, null, new Vector3(0,-100,0), Quaternion.identity, -1, true).gameObject : Instantiate(
            selectedPrefab,
            new Vector3(0f, -100f, 0f),
            Quaternion.identity
        );

        previewObject.name = "Build Placement Preview";
        DisableForPreview(previewObject);

        previewRenderers =
            previewObject.GetComponentsInChildren<Renderer>();

        previewObject.SetActive(false);
        previewIsVisible = false;
    }

    /// <summary>The ghost never acts: no attacks, no heals, no health bar, no collision, no navmesh carve.</summary>
    internal static void DisableForPreview(GameObject ghost)
    {
        foreach (var collider in ghost.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var turret in ghost.GetComponentsInChildren<AutoTurret>(true)) turret.enabled = false;
        foreach (var structure in ghost.GetComponentsInChildren<BuildStructure>(true)) structure.SetPreview();
        foreach (var damageable in ghost.GetComponentsInChildren<Damageable>(true)) damageable.enabled = false;
        foreach (var target in ghost.GetComponentsInChildren<FortressTarget>(true)) target.enabled = false;
        foreach (var obstacle in ghost.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true)) obstacle.enabled = false;
        foreach (var canvas in ghost.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
    }

    private Quaternion GetPlacementRotation()
    {
        return Online ? FacingFor(MatchSessionContext.LocalSide) : FacingFor(TeamSide.SideA);
    }

    private bool TryGetPlacementPosition(Vector2 screenPosition, out Vector3 placementPosition)
    {
        placementPosition = Vector3.zero;

        // Clamped into the viewport before building the ray. A perspective
        // camera has no frustum outside its own rect and throws on a screen
        // point that falls outside it, which happens whenever the pointer
        // leaves the game view; an orthographic one tolerated it silently.
        Ray pointerRay = mainCamera.ScreenPointToRay(
            ClampToViewport(mainCamera, screenPosition)
        );

        // The drag plane has to sit at the height buildables are actually placed
        // at, or the pointer solves against y=0 while the piece lands on the
        // arena floor and the two disagree by the floor's height.
        Plane floorPlane = new Plane(Vector3.up, new Vector3(0f, placementY, 0f));

        if (!floorPlane.Raycast(pointerRay, out float rayDistance))
        {
            return false;
        }

        placementPosition = pointerRay.GetPoint(rayDistance);
        placementPosition = SnapToGrid(placementPosition);
        placementPosition.y = placementY;

        return true;
    }

    /// <summary>
    /// Keeps a screen point inside the camera's pixel rect so it can be turned
    /// into a ray. Clamping rather than rejecting means dragging past the edge
    /// of the window still places against the nearest valid point instead of
    /// the preview freezing.
    /// </summary>
    internal static Vector2 ClampToViewport(Camera camera, Vector2 screenPoint)
    {
        Rect rect = camera.pixelRect;

        return new Vector2(
            Mathf.Clamp(screenPoint.x, rect.xMin + 1f, rect.xMax - 1f),
            Mathf.Clamp(screenPoint.y, rect.yMin + 1f, rect.yMax - 1f));
    }

    private void UpdatePreview(Vector3 position, bool isValid)
    {
        if (previewObject == null)
        {
            return;
        }

        // Every build point spent: nothing more can be placed, and a red ghost
        // trailing the cursor reads as an enemy item on the player's own side.
        if (BuildPointsRemaining <= 0)
        {
            SetPreviewVisible(false);
            return;
        }

        SetPreviewVisible(true);

        previewObject.transform.position = position;
        // Seat the ghost the same way the placed piece will be, or a
        // centre-pivoted prefab previews buried and then lands on the surface.
        BuildableGrounding.SnapToGround(previewObject, placementY);
        previewObject.transform.rotation = GetPlacementRotation();

        SetPreviewColor(
            isValid ? validPlacementColor : invalidPlacementColor
        );
    }

    private void SetPreviewVisible(bool shouldShow)
    {
        if (previewObject == null || previewIsVisible == shouldShow)
        {
            return;
        }

        previewObject.SetActive(shouldShow);
        previewIsVisible = shouldShow;
    }

    private void SetPreviewColor(Color color)
    {
        if (previewRenderers == null)
        {
            return;
        }

        foreach (Renderer previewRenderer in previewRenderers)
        {
            if (previewRenderer == null)
            {
                continue;
            }

            previewBlock.SetColor("_BaseColor", color);
            previewBlock.SetColor("_Color", color);
            previewRenderer.SetPropertyBlock(previewBlock);
        }
    }

    private bool IsPlacementValid(Vector3 position)
    {
        if (GetRemaining(selectedBuildable) <= 0) return false;
        if (Online) return NetworkedMatchState.Instance != null &&
            ValidateOnline(MatchSessionContext.LocalSide, selectedBuildable, position, GetPlacementRotation(), NetworkedMatchState.Instance);
        return IsLegalPlacement(TeamSide.SideA, selectedBuildable, position, GetPlacementRotation(), IsTooCloseToAnotherBuildable);
    }

    private void PlaceBuildable(Vector3 position)
    {
        // Building closes with the build phase. The online authority rejects late
        // requests itself; offline this is the authority.
        if (!IsBuildPhase) return;
        if (Online) { NetworkedMatchState.Instance?.RequestPlacement(selectedBuildable, position, GetPlacementRotation()); return; }
        if (!IsPlacementValid(position)) return;
        GameObject selectedPrefab = Prefab(selectedBuildable);

        if (selectedPrefab == null)
        {
            return;
        }

        GameObject buildable = Instantiate(
            selectedPrefab,
            position,
            GetPlacementRotation()
        );

        buildable.name = "Placed" + selectedBuildable;
        var identity = buildable.GetComponent<FortressTarget>();
        if (identity != null) identity.SetTeam(FortressTeam.Blue);
        BuildableGrounding.SnapToGround(buildable, placementY);

        placedBuildables.Add(new PlacedBuildable
        {
            Object = buildable,
            Type = selectedBuildable
        });

        if (selectedBuildable == BuildableType.Turret)
        {
            ArenaCombatSetup setup = FindFirstObjectByType<ArenaCombatSetup>();
            if (setup != null)
                setup.RefreshNow();
        }

        // Only after the buildable actually exists. The preview ghost, an invalid
        // spot and a cancelled placement all return before reaching this point,
        // so a successful placement is the only thing that makes a sound.
        ReleaseAudio.PlayAt(ReleaseAudioCue.TurretDeploy, position);

        BuildStateChanged?.Invoke();
        SelectNextAvailableIfExhausted();
    }

    private int CountPlacedBuildables(BuildableType type)
    {
        int count = 0;

        foreach (PlacedBuildable buildable in placedBuildables)
        {
            if (buildable.Object != null && buildable.Type == type)
            {
                count++;
            }
        }

        return count;
    }

    private Vector3 SnapToGrid(Vector3 position)
    {
        position.x = Mathf.Round(position.x / gridSize) * gridSize;
        position.z = Mathf.Round(position.z / gridSize) * gridSize;

        return position;
    }

    private bool IsTooCloseToAnotherBuildable(Vector3 position)
    {
        foreach (PlacedBuildable buildable in placedBuildables)
        {
            if (buildable.Object == null)
            {
                continue;
            }

            if (FlatDistance(position, buildable.Object.transform.position) < minimumSpacing)
            {
                return true;
            }
        }

        return false;
    }

    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    private void UndoLastPlacement()
    {
        if (Online) { if (NetworkedMatchState.Instance != null) NetworkedMatchState.Instance.RequestUndo(); return; }
        RemoveDestroyedBuildables();

        if (placedBuildables.Count == 0)
        {
            return;
        }

        PlacedBuildable lastBuildable =
            placedBuildables[placedBuildables.Count - 1];

        placedBuildables.RemoveAt(placedBuildables.Count - 1);

        if (lastBuildable.Object != null)
        {
            Destroy(lastBuildable.Object);
        }
    }

    private void RemoveDestroyedBuildables()
    {
        for (int i = placedBuildables.Count - 1; i >= 0; i--)
        {
            if (placedBuildables[i].Object == null)
            {
                placedBuildables.RemoveAt(i);
            }
        }
    }
}
