using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum MenuBotPose { Idle, Victory, Defeat, Draw }

/// <summary>One live, isolated Spark stage for home and all match outcomes.</summary>
public sealed class RobotPreviewPresenter : MonoBehaviour
{
    private const int PreviewLayer = 31;
    private GameObject world, robot;
    private Camera previewCamera;
    private RenderTexture texture;
    private RawImage target;
    private Animator animator;
    private MenuBotPose pose;
    private Vector3 basePosition;
    private readonly Dictionary<Transform, Quaternion> rotations = new Dictionary<Transform, Quaternion>();
    private Transform body, leftArm, rightArm, eye;
    private Vector3 eyeScale;
    private Transform restEye;
    private int eyeVerticalAxis;
    private bool authoredPose;
    private bool showBackdrop = true;
    private int stageIndex;
    private SparkSkinVisual skinVisual;
    private string previewSkinId;
    private bool shopPresentation;
    private Transform leftWheel, rightWheel;
    public string DisplayedSkin => skinVisual != null ? skinVisual.AppliedSkinId : string.Empty;
    public MenuBotPose Pose => pose;
    private bool homePresentation;
    private readonly Dictionary<Transform, Bounds> railBounds = new Dictionary<Transform, Bounds>();
    private Material outlineMaterial;
    private float outlineScreenPixels;
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    /// <summary>
    /// Lobby cards: the robot renders on a transparent stage and the RawImage
    /// draws a clean dark outline around his whole silhouette, so he separates
    /// from the card behind him. Only for stages without the arena backdrop.
    /// </summary>
    public void EnableSilhouetteOutline(float screenPixels)
    {
        if (previewCamera == null || showBackdrop) return;
        var template = Resources.Load<Material>("Frontend/M_UISilhouetteOutline");
        if (template == null) { Debug.LogWarning("[MENU STAGE] Outline material missing; lobby robot keeps its sky."); return; }
        var sky = previewCamera.GetComponent<Skybox>();
        if (sky != null) Destroy(sky);
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        // An HDR intermediate target has no alpha channel; the outline needs it.
        previewCamera.allowHDR = false;
        outlineMaterial = new Material(template) { name = "LobbyRobotOutline" };
        outlineScreenPixels = screenPixels;
        target.material = outlineMaterial;
        outlineWidthDirty = true;
        cardFraming = true;
        ApplyFraming(pose);
        ResizeTexture();         // re-sizes to the card, not the screen
    }

    private bool cardFraming;

    private bool outlineWidthDirty;

    public void ConfigureHomePresentation()
    {
        homePresentation = true;
        if (arenaRoot != null)
        {
            croppedNames.Clear(); railBounds.Clear();
            CropBackdrop(arenaRoot);
            ApplyCloudBackdrop(true);
        }
        HideHomeRing();
    }

    private void HideHomeRing()
    {
        if (!homePresentation || robot == null) return;
        foreach (var ring in robot.GetComponentsInChildren<TeamGroundRing>(true)) ring.enabled = false;
        foreach (var renderer in robot.GetComponentsInChildren<Renderer>(true))
            if (renderer is LineRenderer || renderer.name.Contains("GroundRing") || renderer.name.Contains("SelectionRing")) renderer.enabled = false;
    }

    private Bounds BackdropBounds(Renderer piece)
    {
        if (!homePresentation || !piece.name.StartsWith("Rail")) return piece.bounds;
        // Imported rails contain separate blue/yellow meshes and support posts.
        // Cropping individual meshes left floating fragments. Keep each assembly whole.
        Transform assembly = piece.transform;
        while (assembly.parent != null && !assembly.name.EndsWith("_Instance")) assembly = assembly.parent;
        if (!railBounds.TryGetValue(assembly,out var bounds))
        {
            bounds = piece.bounds;
            foreach (var part in assembly.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(part.bounds);
            railBounds[assembly] = bounds;
        }
        return bounds;
    }

    /// <summary>
    /// Stages are parked far apart so two presenters can be on screen at once.
    ///
    /// Every stage shares the preview layer, so if they sat at the same coordinate
    /// each camera would render both robots. The separation is larger than the
    /// camera's far plane, which keeps them mutually invisible.
    /// </summary>
    private const float StageSpacing = 600f;
    private static int stageCount;

    public bool Initialize(GameObject fallbackPrefab, RawImage image) =>
        Initialize(fallbackPrefab, image, true);

    /// <param name="includeBackdrop">
    /// False for small panels such as the lobby, where the full arena would add
    /// hundreds of renderers behind a thumbnail-sized robot for no visual gain.
    /// </param>
    public bool Initialize(GameObject fallbackPrefab, RawImage image, bool includeBackdrop)
    {
        showBackdrop = includeBackdrop;
        DisposePreview();
        FrontendAssets assets = FrontendAssets.Load();
        GameObject source = assets != null && assets.RobotVisual != null ? assets.RobotVisual : fallbackPrefab;
        if (source == null || image == null)
        {
            Debug.LogError(
                $"[MENU STAGE] Cannot build the live stage. assets={(assets == null ? "MISSING" : "ok")} " +
                $"robotVisual={(assets != null && assets.RobotVisual != null ? "ok" : "MISSING")} " +
                $"fallbackPrefab={(fallbackPrefab == null ? "MISSING" : "ok")} " +
                $"rawImage={(image == null ? "MISSING" : "ok")}");
            return false;
        }
        target = image;
        target.raycastTarget = false;
        world = new GameObject("MenuBotStage");
        stageIndex = stageCount++;
        world.transform.position = new Vector3(2400 + stageIndex * StageSpacing, 2400, 2400);
        world.SetActive(false);
        robot = Instantiate(source, world.transform);
        robot.name = "EquippedSpark";
        foreach (MonoBehaviour b in robot.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        foreach (Collider c in robot.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (Rigidbody r in robot.GetComponentsInChildren<Rigidbody>(true)) { r.isKinematic = true; r.detectCollisions = false; }
        animator = robot.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        robot.transform.localPosition = Vector3.zero;
        robot.transform.localRotation = Quaternion.Euler(0, -8, 0);
        basePosition = robot.transform.localPosition;
        CachePoseParts();
        if (showBackdrop && assets != null && assets.Arena != null)
        {
            GameObject arena = Instantiate(assets.Arena, world.transform);
            arena.name = "SkyArenaMenuBackdrop";
            // The arena prefab is authored at Spark's original 2.34m scale, but the
            // gameplay robot is imported at 1.58x. Left at scale 1 the railings sit
            // barely knee-high on him and the whole map crowds the frame, which is
            // what made the backdrop read as clutter instead of a location.
            arena.transform.localScale = Vector3.one * ArenaMatchScale;
            arena.transform.localPosition = new Vector3(0f, -.06f, -6f);
            foreach (Renderer surface in arena.GetComponentsInChildren<Renderer>(true))
            {
                if (!surface.name.Contains("Arena_Outer_Platform")) continue;
                // Align this presentation-only floor to Spark's wheel contact plane.
                surface.transform.position += Vector3.up * (world.transform.position.y - surface.bounds.max.y);
                var materials = surface.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name.Contains("Floor_Cream"))
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_BaseColor", new Color(1f, .94f, .81f));
                        block.SetFloat("_Smoothness", .38f);
                        surface.SetPropertyBlock(block, i);
                    }
            }
            foreach (Collider c in arena.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (MonoBehaviour b in arena.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            CropBackdrop(arena.transform);
            arenaRoot = arena.transform;
        }
        if (showBackdrop) BuildClouds();
        SetLayer(world);
        var cameraObject = new GameObject("MenuBotCamera", typeof(Camera));
        cameraObject.transform.SetParent(world.transform, false);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.cullingMask = 1 << PreviewLayer;
        // UI preview color and depth attachments must use the same sample count.
        // Android accepted Create() for the old 2x target but supplied a 1x
        // attachment, making every render pass fail and leaving the stage blank.
        previewCamera.allowMSAA = false;
        // Perspective, not orthographic. The old orthographic rig is what made
        // every frontend screen read flat and prototype-like: no convergence, no
        // depth between Spark and the arena behind him.
        previewCamera.orthographic = false;
        // Spark's eye is a flat disc sitting about 0.03 units off the face screen,
        // and depth resolution goes as z^2/near: at near 0.1 the smallest depth
        // difference this stage could resolve at Spark's distance was around the
        // same 0.03, so the eye and the screen behind it swapped places per pixel
        // per frame and the eye rendered as a shifting broken ring.
        //
        // The stage has to stay out at world 2400 because the scene's own camera
        // has an all-layers mask and a 400 unit far plane, so the fix is here:
        // nothing on this stage is nearer than about 9.6 units (the closest
        // framing stands off 11.0 and Spark is ~1.5 across), so a 4 unit near
        // plane is clear of the geometry and buys ~40x the depth precision.
        previewCamera.nearClipPlane = 4f;
        previewCamera.farClipPlane = 220f;
        BuildSky();
        BuildLighting();
        ResizeTexture();
        world.SetActive(true);
        ApplyEquipped();
        SetPose(MenuBotPose.Idle);
        LogStageDiagnostics(assets, source);
        PlayerProfileService.ProfileChanged += ApplyEquipped;
        return true;
    }

    /// <summary>
    /// Camera framing per screen. Spark stands with his feet at the stage origin
    /// and is 3.70 units tall, so these are solved from the fraction of screen
    /// height he should occupy rather than eyeballed.
    /// </summary>
    private readonly struct Framing
    {
        public readonly Vector3 Position;
        public readonly Vector3 LookAt;
        public readonly float FieldOfView;

        public Framing(Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            Position = position;
            LookAt = lookAt;
            FieldOfView = fieldOfView;
        }
    }

    // ~34 degrees vertical is roughly a 50mm feel: natural convergence without
    // the fisheye stretch a wide lens puts on a round character this close.
    private const float PresentationFov = 34f;

    private static Framing GetFraming(MenuBotPose pose)
    {
        // Home leaves headroom for the surrounding UI; results push in and drop
        // the camera below Spark's centre for a slight hero angle.
        // Home pushes in from the old 15.2 stand-off. At that distance Spark read
        // as one more element on a busy screen rather than its subject, with a
        // wide band of empty sand between his wheels and the buttons. Closer, and
        // aimed slightly lower, he fills the gap the rail used to leave while the
        // top bar and the bottom deck keep their clearance.
        return pose == MenuBotPose.Idle
            ? new Framing(new Vector3(0f, 2.30f, 11.9f), new Vector3(0f, 1.86f, 0f), PresentationFov)
            : new Framing(new Vector3(0f, 1.55f, 11.0f), new Vector3(0f, 2.05f, 0f), PresentationFov);
    }

    private void ApplyFraming(MenuBotPose requested)
    {
        if (previewCamera == null) return;
        Framing framing = GetFraming(requested);
        if (shopPresentation) framing = new Framing(new Vector3(0, 2.45f, 15.2f), new Vector3(0, .35f, 0), PresentationFov);
        // Card stages: Spark fills ~75% of the card height, wheels near the bottom.
        if (cardFraming) framing = new Framing(new Vector3(0f, 1.95f, 8.1f), new Vector3(0f, 1.85f, 0f), PresentationFov);
        previewCamera.fieldOfView = framing.FieldOfView;
        previewCamera.transform.localPosition = framing.Position;
        previewCamera.transform.localRotation =
            Quaternion.LookRotation((framing.LookAt - framing.Position).normalized, Vector3.up);
    }

    public void SetPose(MenuBotPose requested)
    {
        pose = requested;
        ApplyFraming(requested);
        // Home sits Spark on his platform in open sky. Results keep more of the
        // arena so the shot still reads as the place the match was just fought.
        ApplyCloudBackdrop(requested == MenuBotPose.Idle);
        authoredPose = false;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        int state = Animator.StringToHash(requested.ToString());
        if (animator.HasState(0, state))
        {
            animator.CrossFadeInFixedTime(state, .18f);
            authoredPose = true;
        }
    }

    // Gameplay imports Spark at 1.58x his authored size and scales SkyArena to
    // 2.32x to match. The frontend stage has to use the same ratio or Spark and
    // the arena disagree about how big the world is.
    private const float ArenaMatchScale = 2.32f;

    /// <summary>
    /// Keeps a recognisable slice of SkyArena behind Spark and drops the rest.
    /// Showing the whole 95x107 map put outer rock, distant facilities and stray
    /// barrels in the foreground, competing with the character who is meant to be
    /// the hero of the shot. Also saves rendering ~1600 offscreen pieces.
    /// </summary>
    private void CropBackdrop(Transform arena)
    {
        Vector3 origin = world.transform.position;
        int hidden = 0;
        foreach (Renderer piece in arena.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 local = BackdropBounds(piece).center - origin;

            // Anything beside or in front of Spark is foreground clutter; anything
            // far behind him is haze he will never read as a place.
            bool besideOrInFront = Mathf.Abs(local.x) > 26f || local.z > 6f;
            if (piece.name.Contains("Center_Mark")) { piece.enabled = false; croppedNames.Add(piece); continue; }
            bool tooFarBehind = local.z < -46f;
            bool farBelow = local.y < -9f;

            if (besideOrInFront || tooFarBehind || farBelow)
            {
                piece.enabled = false;
                // Remembered so the home/result backdrop toggle can never
                // re-enable something the distance crop already threw away.
                croppedNames.Add(piece);
                hidden++;
            }
        }
        croppedPieces = hidden;
    }

    private int croppedPieces;
    public int CroppedBackdropPieces => croppedPieces;
    private Transform arenaRoot;
    private Transform cloudRoot;

    /// <summary>
    /// Home swaps the arena's buildings, trees and turbine for open sky, leaving
    /// only the platform Spark stands on and its rail edge. SkyArena is a floating
    /// island, so clouds read as the same world without the hangar clutter
    /// competing with the character.
    /// </summary>
    private void ApplyCloudBackdrop(bool cloudsOnly)
    {
        if (cloudRoot != null) cloudRoot.gameObject.SetActive(true);
        if (arenaRoot == null) return;

        Vector3 origin = world.transform.position;
        foreach (Renderer piece in arenaRoot.GetComponentsInChildren<Renderer>(true))
        {
            // Never re-enable anything the distance crop already discarded.
            if (croppedNames.Contains(piece)) continue;

            if (!cloudsOnly)
            {
                piece.enabled = true;
                continue;
            }

            Bounds bounds = BackdropBounds(piece);
            Vector3 local = bounds.center - origin;
            float top = bounds.max.y - origin.y;
            // Keep the deck under his wheels and the low rail ring around it.
            bool onPlatform = Mathf.Abs(local.x) < 27f && local.z > -37f && local.z < 25f;
            piece.enabled = onPlatform && top < 3.2f;
        }
    }

    private readonly HashSet<Renderer> croppedNames = new HashSet<Renderer>();

    /// <summary>
    /// Cloud decks below the island plus scattered puffs at mixed depths. Reuses
    /// the arena's own cloud material so home and the match share one sky.
    /// </summary>
    private void BuildClouds()
    {
        Material deck = Resources.Load<Material>("Frontend/M_FrontendCloudDeck");
        Material puff = Resources.Load<Material>("Frontend/M_FrontendCloudPuff");
        if (deck == null && puff == null) return;

        cloudRoot = new GameObject("MenuCloudscape").transform;
        cloudRoot.SetParent(world.transform, false);

        // The camera sits within ~2 degrees of horizontal, so at the rail ring only
        // y > ~6 is open sky. Decks parked below the island are occluded by the
        // island itself, which is why the first pass read as a smudge on the
        // horizon. Near deck skims just under the platform lip so it shows past
        // the edges; the rest sit low purely for depth.
        if (deck != null)
        {
            AddCloudQuad("CloudDeck_Near", deck, new Vector3(0f, -5f, -46f), new Vector3(170f, 170f, 1f), Quaternion.Euler(90f, 0f, 0f));
            AddCloudQuad("CloudDeck_Far", deck, new Vector3(0f, -22f, -95f), new Vector3(320f, 320f, 1f), Quaternion.Euler(90f, 0f, 0f));
        }

        if (puff == null) return;
        var random = new System.Random(90721);
        for (int i = 0; i < 18; i++)
        {
            float angle = (i / 18f) * Mathf.PI * 2f + (float)random.NextDouble() * .3f;
            float spread = 26f + (float)random.NextDouble() * 62f;
            // Sit them in the visible sky band above the railing.
            float height = 4f + (float)random.NextDouble() * 19f;
            float size = 18f + (float)random.NextDouble() * 24f;
            float depth = -48f - (float)random.NextDouble() * 62f;
            Vector3 at = new Vector3(Mathf.Sin(angle) * spread, height, depth);
            AddCloudQuad($"CloudPuff_{i:00}", puff, at, new Vector3(size, size * .58f, 1f), Quaternion.identity);
        }
    }

    private void AddCloudQuad(string cloudName, Material material, Vector3 at, Vector3 scale, Quaternion rotation)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = cloudName;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(cloudRoot, false);
        quad.transform.localPosition = at;
        quad.transform.localRotation = rotation;
        quad.transform.localScale = scale;
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    /// <summary>
    /// Three-point rig, all culled to the preview layer so it cannot leak into
    /// the arena scene the result screen runs inside. The single key light the
    /// stage had before is why Spark read flat against the backdrop - there was
    /// nothing separating his silhouette from the arena.
    /// </summary>
    private void BuildLighting()
    {
        // The key has to clearly dominate. Skybox ambient is already lifting the
        // shadow side, so three near-equal lights from three sides cancelled all
        // the shading and left everything looking unlit.
        AddLight("MenuKeyLight", new Vector3(-5.5f, 7f, 8f),
            new Color(1f, .96f, .88f), 1.15f, LightShadows.Soft);
        // Cool rim from behind-upper-right: this is what lifts him off the arena.
        AddLight("MenuRimLight", new Vector3(5f, 5.5f, -8f),
            new Color(.70f, .87f, 1f), .85f, LightShadows.None);
        // Barely-there bounce. Any stronger and it fills the key's shadow side
        // straight back in.
        AddLight("MenuFillLight", new Vector3(4f, .8f, 6f),
            new Color(.78f, .87f, 1f), .18f, LightShadows.None);
    }

    private void AddLight(string lightName, Vector3 fromPosition, Color colour,
        float intensity, LightShadows shadows)
    {
        var holder = new GameObject(lightName, typeof(Light));
        holder.transform.SetParent(world.transform, false);
        holder.transform.localPosition = fromPosition;
        // Aim at Spark's centre of mass rather than his feet.
        holder.transform.localRotation =
            Quaternion.LookRotation((new Vector3(0f, 1.9f, 0f) - fromPosition).normalized, Vector3.up);
        Light light = holder.GetComponent<Light>();
        light.type = LightType.Directional;
        light.color = colour;
        light.intensity = intensity;
        light.shadows = shadows;
        light.shadowStrength = .55f;
        light.cullingMask = 1 << PreviewLayer;
    }

    /// <summary>
    /// Reuses the arena's own gradient sky so the frontend and the match read as
    /// the same world. Falls back to a flat colour if the material is missing.
    /// </summary>
    private void BuildSky()
    {
        Material sky = Resources.Load<Material>("Frontend/M_PresentationSky");
        if (sky == null)
        {
            Shader gradient = Shader.Find("SkyArena/Sky Gradient");
            if (gradient != null)
            {
                sky = new Material(gradient) { name = "M_PresentationSky_Runtime" };
                sky.SetColor("_ZenithColor", new Color(.16f, .53f, .88f));
                sky.SetColor("_HorizonColor", new Color(.76f, .93f, 1f));
                sky.SetColor("_NadirColor", new Color(.45f, .78f, .96f));
                sky.SetFloat("_HorizonFalloff", 1.6f);
                sky.SetFloat("_Exposure", 1f);
            }
        }

        if (sky == null)
        {
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(.32f, .64f, .94f);
            return;
        }

        Skybox skybox = previewCamera.gameObject.AddComponent<Skybox>();
        skybox.material = sky;
        previewCamera.clearFlags = CameraClearFlags.Skybox;
    }

    private void ApplyEquipped()
    {
        if (robot != null)
        {
            if (string.IsNullOrEmpty(previewSkinId)) RobotCosmeticApplier.ApplyEquipped(robot);
            else RobotCosmeticApplier.ApplySkin(robot, previewSkinId);
            skinVisual = robot.GetComponentInChildren<SparkSkinVisual>(true);
            SetLayer(robot);
            HideHomeRing();

            // The skin rebuild may have replaced the bones this preview poses.
            CachePoseParts();
        }
    }

    /// <summary>Temporary inspection selection. Does not write or equip the saved profile.</summary>
    public void PreviewSkin(string skinId)
    {
        previewSkinId = skinId;
        ApplyEquipped();
    }

    public void SetShopPresentation(bool value)
    {
        shopPresentation = value;
        ApplyFraming(pose);
        if (!value) PreviewSkin(null);
    }

    private void LateUpdate()
    {
        if (robot == null) return;
        ResizeTexture();
        float t = Time.unscaledTime;
        float breath = Mathf.Sin(t * 1.8f);
        robot.transform.localPosition = basePosition + Vector3.up * (pose == MenuBotPose.Victory ? Mathf.Abs(Mathf.Sin(t * 3)) * .09f : breath * .015f);
        robot.transform.localRotation = Quaternion.Euler(0, -8 + Mathf.Sin(t * .65f) * 4, 0);
        if (authoredPose) return;
        // Supplied Spark has no result clips. These preview-only bone offsets keep its controller intact.
        Rotate(body, pose == MenuBotPose.Defeat ? new Vector3(10, 0, -3) :
            pose == MenuBotPose.Draw ? new Vector3(0, 0, 5 + breath * 2) : new Vector3(breath, 0, 0));
        // Arm lift runs on Y (with a little X), not Z. Rotating these bones about
        // Z moves the wrist by ~0.01 units, which is why the previous victory pose
        // never actually raised anything. Measured: (-30,-70,0) lifts the right
        // wrist +0.87 while keeping it out from the body. Left mirrors on Y.
        float cheer = breath * 8f;
        Rotate(leftArm, pose == MenuBotPose.Victory ? new Vector3(-30, 70 + cheer, 0) :
            pose == MenuBotPose.Defeat ? new Vector3(15, -45, 0) :
            pose == MenuBotPose.Draw ? new Vector3(-15, 38, 0) : new Vector3(0, breath * 2, 0));
        Rotate(rightArm, pose == MenuBotPose.Victory ? new Vector3(-30, -70 - cheer, 0) :
            pose == MenuBotPose.Defeat ? new Vector3(15, 45, 0) :
            pose == MenuBotPose.Draw ? new Vector3(-15, -38, 0) : new Vector3(0, -breath * 2, 0));
        Rotate(leftWheel, new Vector3(0, 0, Mathf.Sin(t * .65f) * 2));
        Rotate(rightWheel, new Vector3(0, 0, Mathf.Sin(t * .65f) * 2));
        if (eye != null)
        {
            float blink = Mathf.Repeat(t, 4.2f) < .12f ? .1f : 1;
            float mood = pose == MenuBotPose.Defeat ? .5f : pose == MenuBotPose.Victory ? .75f :
                pose == MenuBotPose.Draw ? .8f : 1;
            var expressionScale = eyeScale;
            expressionScale[eyeVerticalAxis] *= blink * mood;
            eye.localScale = expressionScale;

            // The eye bone must never be rotated. Spark's eye is a flat disc lying
            // in the face screen, and the screen it sits on is domed, so tipping the
            // disc even a little pushes one side of it behind that dome where it is
            // occluded - the result on screen is the ring rendering as a broken
            // crescent. Measured: a 12 degree tilt took the baked eye mesh from
            // 0.006 to 0.056 units thick, half of it ending up inside the screen.
            //
            // Squashing is safe and is what drives every other expression here: the
            // squash axis lies in the disc's own plane, so it can only ever move
            // vertices within the face, never through it. Draw therefore reads as a
            // narrowed eye rather than a tilted one.
            Rotate(eye, Vector3.zero);
        }
    }

    /// <summary>
    /// Finds the bones the preview poses by hand and records their rest rotation.
    ///
    /// Re-run after every skin change: applying a skin rebuilds the robot's
    /// hierarchy, so the cached bones can be replaced by new ones. Keeping the old
    /// references meant the pose code asked for a rest rotation that had never
    /// been recorded, and the menu threw once per frame.
    /// </summary>
    private void CachePoseParts()
    {
        if (robot == null) return;

        body = leftArm = rightArm = eye = leftWheel = rightWheel = null;
        rotations.Clear();

        foreach (Transform t in robot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Body") body = t;
            if (t.name == "Arm_L") leftArm = t;
            if (t.name == "Arm_R") rightArm = t;
            if (t.name == "Eye") eye = t;
            if (t.name == "Wheel_L") leftWheel = t;
            if (t.name == "Wheel_R") rightWheel = t;
        }

        foreach (Transform t in new[] { body, leftArm, rightArm, eye, leftWheel, rightWheel })
            if (t != null) rotations[t] = t.localRotation;

        if (eye != null && restEye != eye)
        {
            restEye = eye;
            eyeScale = eye.localScale;
            // Spark's imported eye bone is Z-up. Do not squash its depth axis,
            // or recapture a transient blink as the rest pose on a skin swap.
            Vector3 up = eye.InverseTransformDirection(robot.transform.up);
            up = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
            eyeVerticalAxis = up.x >= up.y && up.x >= up.z ? 0 : up.y >= up.z ? 1 : 2;
        }
    }

    private void Rotate(Transform part, Vector3 angles)
    {
        if (part == null) return;

        // Tolerates a bone whose rest pose was never recorded - adopt its current
        // rotation rather than throwing. A missing entry must not break the menu.
        if (!rotations.TryGetValue(part, out Quaternion rest))
        {
            rest = part.localRotation;
            rotations[part] = rest;
        }

        part.localRotation = rest * Quaternion.Euler(angles);
    }

    private void ResizeTexture()
    {
        if (target == null || previewCamera == null) return;
        float aspect = Mathf.Max(.5f, target.rectTransform.rect.width / Mathf.Max(1, target.rectTransform.rect.height));
        int height = Mathf.Clamp(Screen.height, 720, 1080), width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 360, 2560);
        float shownHeight = 0f;
        if (outlineMaterial != null)
        {
            // A card-sized stage: rendered at ~1.5x its on-screen size, so the
            // outline width in texels maps to a constant width in screen pixels.
            var canvas = target.canvas;
            shownHeight = target.rectTransform.rect.height * (canvas != null ? canvas.scaleFactor : 1f);
            height = Mathf.Clamp(Mathf.RoundToInt(shownHeight * 1.5f), 256, 1080);
            width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 128, 2048);
        }
        if (texture != null && texture.width == width && texture.height == height)
        {
            if (outlineWidthDirty && shownHeight > 1f) ApplyOutlineWidth(shownHeight);
            return;
        }
        if (texture != null) { previewCamera.targetTexture = null; texture.Release(); Destroy(texture); }

        texture = CreateStageTexture(width, height);
        if (texture == null)
        {
            // Nothing was allocated, so leaving the camera pointed at a dead target
            // would render the stage to nowhere and show the RawImage as black -
            // which is exactly the "UI only, no 3D" symptom.
            Debug.LogError("[MENU STAGE] Could not allocate the preview RenderTexture; live stage unavailable.");
            return;
        }

        previewCamera.targetTexture = texture;
        target.texture = texture;
        if (outlineMaterial != null && shownHeight > 1f) ApplyOutlineWidth(shownHeight);
    }

    private void ApplyOutlineWidth(float shownHeight)
    {
        if (outlineMaterial == null || texture == null) return;
        outlineMaterial.SetFloat(OutlineWidthId, outlineScreenPixels * texture.height / shownHeight);
        outlineWidthDirty = false;
    }

    /// <summary>
    /// Matches the preview camera's single-sample color/depth configuration.
    /// </summary>
    private static RenderTexture CreateStageTexture(int width, int height)
    {
        var plain = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "LiveSparkStage",
            antiAliasing = 1
        };

        if (plain.Create()) return plain;
        Destroy(plain);
        return null;
    }

    /// <summary>
    /// One-shot report of everything the stage depends on. Written once per stage
    /// so it can be pulled off a device with logcat, because this presentation is
    /// built entirely at runtime and leaves nothing in the scene to inspect.
    /// </summary>
    private void LogStageDiagnostics(FrontendAssets assets, GameObject source)
    {
        Debug.Log(
            $"[MENU STAGE] graphics={SystemInfo.graphicsDeviceType} " +
            $"msaaSupport={SystemInfo.supportsMultisampledTextures} " +
            $"screen={Screen.width}x{Screen.height} " +
            $"assets={(assets == null ? "MISSING" : "ok")} " +
            $"robotVisual={(assets != null && assets.RobotVisual != null ? "ok" : "MISSING")} " +
            $"arena={(assets != null && assets.Arena != null ? "ok" : "MISSING")} " +
            $"source={(source == null ? "MISSING" : source.name)}");

        Debug.Log(
            $"[MENU STAGE] camera={(previewCamera == null ? "MISSING" : "ok")} " +
            $"targetTexture={(texture == null ? "MISSING" : $"{texture.width}x{texture.height} created={texture.IsCreated()} msaa={texture.antiAliasing}")} " +
            $"clearFlags={(previewCamera == null ? "-" : previewCamera.clearFlags.ToString())} " +
            $"cullingMask={(previewCamera == null ? 0 : previewCamera.cullingMask)} " +
            $"rawImageRect={(target == null ? "MISSING" : target.rectTransform.rect.size.ToString())}");

        int renderersOnLayer = 0;
        if (world != null)
        {
            foreach (Renderer piece in world.GetComponentsInChildren<Renderer>(true))
            {
                if (piece.enabled && piece.gameObject.layer == PreviewLayer) renderersOnLayer++;
            }
        }

        Debug.Log($"[MENU STAGE] visible renderers on preview layer = {renderersOnLayer}");
    }

    private static void SetLayer(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
    }

    private void OnEnable() { if (world != null) world.SetActive(true); }
    private void OnDisable() { if (world != null) world.SetActive(false); }
    private void OnDestroy() => DisposePreview();
    private void DisposePreview()
    {
        PlayerProfileService.ProfileChanged -= ApplyEquipped;
        if (target != null) target.texture = null;
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (texture != null) { texture.Release(); Destroy(texture); }
        if (outlineMaterial != null) { Destroy(outlineMaterial); outlineMaterial = null; }
        if (world != null) { world.SetActive(false); Destroy(world); }
        texture = null; world = null; robot = null; rotations.Clear();
    }
}
