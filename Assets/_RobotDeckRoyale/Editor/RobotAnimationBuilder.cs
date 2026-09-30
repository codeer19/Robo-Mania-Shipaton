using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Builds reviewable assets from the Blender rig without modifying the source model.</summary>
public static class RobotAnimationBuilder
{
    public const string Folder = "Assets/_RobotDeckRoyale/Art/Animation/Robot";
    public const string PrefabPath = "Assets/_RobotDeckRoyale/Prefabs/Player/PF_Robot_AnimatedVisual.prefab";
    private static readonly string[] Names = { "ReferencePose", "Idle", "Move", "Turn_L", "Turn_R", "Fire", "Reload", "TakeHit", "Death", "Spawn", "Victory", "Defeat" };

    [MenuItem("Robo Mania/Animation/Build Robot Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        AssetDatabase.Refresh();
        string modelPath = Folder + "/robot1_rigged.fbx";
        ConfigureImport(modelPath, null);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().First();
        var clips = new Dictionary<string, AnimationClip>();
        foreach (string name in Names)
        {
            string path = Folder + "/robot1@" + name + ".fbx";
            ConfigureImport(path, avatar);
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = name; clip.legacy = false;
            // Export baking includes constant keys on all bones. Wheels and eyes belong to code.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.path.Contains("/Wheel_") || binding.path.Contains("/Eye_") ||
                    binding.propertyName.Contains("Scale") || binding.path == "" || binding.path == "RobotRig")
                    AnimationUtility.SetEditorCurve(clip, binding, null);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = name == "Idle" || name == "Move" || name == "Reload";
            settings.loopBlend = settings.loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            string clipPath = Folder + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing == null) { AssetDatabase.CreateAsset(clip, clipPath); existing = clip; }
            else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); EditorUtility.SetDirty(existing); }
            clips.Add(name, existing);
        }
        AnimationUtility.SetAdditiveReferencePose(clips["TakeHit"], clips["ReferencePose"], 0f);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/Robot.controller");
        if (controller == null) controller = CreateController(clips);
        var wrapper = new GameObject("Visual");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            model.name = "Model"; model.transform.SetParent(wrapper.transform, false);
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar; animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var rig = model.AddComponent<RobotRigBindings>();
            rig.Animator = animator;
            var transforms = model.GetComponentsInChildren<Transform>(true);
            Func<string, Transform> bone = name => transforms.Single(t => t.name == name);
            rig.Chassis = bone("Chassis"); rig.Head = bone("Head"); rig.GunArm = bone("Arm_R");
            rig.LeftWheel = bone("Wheel_L"); rig.RightWheel = bone("Wheel_R");
            rig.LeftEye = bone("Eye_L"); rig.RightEye = bone("Eye_R");
            rig.ScreenPanel = bone("ChestScreen");
            rig.FaceSocket = new GameObject("FaceSocket").transform;
            rig.FaceSocket.SetParent(rig.Chassis, false);
            rig.FaceSocket.position = new Vector3(-0.026f, 2.116f, 0.70f);
            rig.FaceSocket.rotation = Quaternion.identity;
            rig.MuzzleVisual = new GameObject("MuzzleVisual").transform;
            rig.MuzzleVisual.SetParent(rig.GunArm, false);
            rig.MuzzleVisual.localPosition = new Vector3(0f, -0.3f, 0.2f);
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.quality = SkinQuality.Bone1;
                renderer.updateWhenOffscreen = false;
                Bounds sourceBounds = renderer.sharedMesh.bounds;
                Matrix4x4 conversion = renderer.rootBone.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds bounds = new Bounds(conversion.MultiplyPoint3x4(sourceBounds.center), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                    bounds.Encapsulate(conversion.MultiplyPoint3x4(sourceBounds.center + Vector3.Scale(sourceBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                bounds.Expand(1.0f);
                renderer.localBounds = bounds;
            }
            RemapMaterials(model);
            wrapper.AddComponent<RobotMotionVisual>();
            wrapper.AddComponent<RobotExpressionVisual>();
            wrapper.AddComponent<RobotFaceScreen>();
            var hooks = wrapper.AddComponent<RobotAnimationHooks>();
            var serialized = new SerializedObject(hooks);
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(wrapper); }
        AssetDatabase.SaveAssets();
        Debug.Log("Robot rig, 12 editable clips, masks, controller and visual prefab built.");
    }

    private static void ConfigureImport(string path, Avatar avatar)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.globalScale = 1f; importer.useFileUnits = true; importer.preserveHierarchy = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = avatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.motionNodeName = "Root";
        importer.optimizeGameObjects = false;
        importer.skinWeights = ModelImporterSkinWeights.Custom; importer.maxBonesPerVertex = 1;
        importer.isReadable = false; importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();
    }

    private static void RemapMaterials(GameObject model)
    {
        var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_RobotDeckRoyale/Prefabs/Player/PF_Robot_Player.prefab");
        var materials = original.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
            .Where(m => m != null).GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            var slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && materials.TryGetValue(slots[i].name, out var material)) slots[i] = material;
            renderer.sharedMaterials = slots;
        }
    }

    private static AnimatorController CreateController(Dictionary<string, AnimationClip> clips)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Robot.controller");
        foreach (string p in new[] {"Fire", "Hit", "Death", "Respawn"}) c.AddParameter(p, AnimatorControllerParameterType.Trigger);
        foreach (string p in new[] {"MoveSpeed", "TurnRate", "ReloadPhase"}) c.AddParameter(p, AnimatorControllerParameterType.Float);
        foreach (string p in new[] {"Dead", "CombatActive", "Reloading"}) c.AddParameter(p, AnimatorControllerParameterType.Bool);
        c.AddParameter("Result", AnimatorControllerParameterType.Int);
        var layers = c.layers; layers[0].name = "Body"; c.layers = layers;
        var body = c.layers[0].stateMachine;
        var locomotion = State(body, "Locomotion", null);
        var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "MoveSpeed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, c); tree.AddChild(clips["Idle"], 0f); tree.AddChild(clips["Move"], 1f);
        locomotion.motion = tree;
        var spawn = State(body, "Spawn", clips["Spawn"]); body.defaultState = spawn;
        Transition(spawn, locomotion, .08f, true);
        var death = State(body, "Death", clips["Death"]);
        var kill = body.AddAnyStateTransition(death); Setup(kill,.02f); kill.AddCondition(AnimatorConditionMode.If,0,"Dead"); kill.canTransitionToSelf=false;
        var respawn = Transition(death,spawn,.05f); respawn.AddCondition(AnimatorConditionMode.IfNot,0,"Dead"); respawn.AddCondition(AnimatorConditionMode.If,0,"Respawn");
        for (int side=0; side<2; side++)
        {
            var turn = State(body,side==0?"Turn_L":"Turn_R",clips[side==0?"Turn_L":"Turn_R"]);
            var enter=Transition(locomotion,turn,.05f);
            enter.AddCondition(AnimatorConditionMode.Less,.1f,"MoveSpeed");
            enter.AddCondition(side==0?AnimatorConditionMode.Greater:AnimatorConditionMode.Less,side==0?.25f:-.25f,"TurnRate");
            enter.AddCondition(AnimatorConditionMode.If,0,"CombatActive");
            Transition(turn,locomotion,.08f,true);
            var moving=Transition(turn,locomotion,.08f); moving.AddCondition(AnimatorConditionMode.Greater,.15f,"MoveSpeed");
        }
        for (int result=1; result<=2; result++)
        {
            string name=result==1?"Victory":"Defeat";
            var state=State(body,name,clips[name]); var tr=body.AddAnyStateTransition(state); Setup(tr,.12f);
            tr.AddCondition(AnimatorConditionMode.Equals,result,"Result"); tr.AddCondition(AnimatorConditionMode.IfNot,0,"Dead"); tr.canTransitionToSelf=false;
        }
        c.AddLayer("Weapon"); c.AddLayer("Impact");
        layers=c.layers;
        layers[1].avatarMask=Mask("WeaponMask",true); layers[1].defaultWeight=0f;
        layers[2].avatarMask=Mask("ImpactMask",false); layers[2].defaultWeight=0f; layers[2].blendingMode=AnimatorLayerBlendingMode.Additive;
        c.layers=layers;
        var weapon=c.layers[1].stateMachine;
        var empty=State(weapon,"Empty",null); weapon.defaultState=empty;
        var fire=State(weapon,"Fire",clips["Fire"]); var reload=State(weapon,"Reload",clips["Reload"]);
        reload.timeParameterActive=true; reload.timeParameter="ReloadPhase";
        var shot=weapon.AddAnyStateTransition(fire); Setup(shot,0f); shot.canTransitionToSelf=true;
        shot.AddCondition(AnimatorConditionMode.If,0,"Fire"); shot.AddCondition(AnimatorConditionMode.If,0,"CombatActive"); shot.AddCondition(AnimatorConditionMode.IfNot,0,"Dead");
        var charge=Transition(empty,reload,.08f); charge.AddCondition(AnimatorConditionMode.If,0,"Reloading");
        var firedReload=Transition(fire,reload,.03f,true); firedReload.AddCondition(AnimatorConditionMode.If,0,"Reloading");
        var firedEmpty=Transition(fire,empty,.03f,true); firedEmpty.AddCondition(AnimatorConditionMode.IfNot,0,"Reloading");
        var full=Transition(reload,empty,.08f); full.AddCondition(AnimatorConditionMode.IfNot,0,"Reloading");
        var impact=c.layers[2].stateMachine;
        var zero=State(impact,"ZeroDelta",clips["ReferencePose"]); impact.defaultState=zero;
        AnimationUtility.SetAdditiveReferencePose(clips["ReferencePose"],clips["ReferencePose"],0f);
        var hit=State(impact,"TakeHit",clips["TakeHit"]);
        var flinch=impact.AddAnyStateTransition(hit); Setup(flinch,.01f); flinch.canTransitionToSelf=true;
        flinch.AddCondition(AnimatorConditionMode.If,0,"Hit"); flinch.AddCondition(AnimatorConditionMode.IfNot,0,"Dead");
        Transition(hit,zero,.04f,true);
        return c;
    }
    private static AvatarMask Mask(string name, bool weapon)
    {
        var mask=new AvatarMask {name=name};
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/robot1_rigged.fbx");
        var transforms=model.GetComponentsInChildren<Transform>(); mask.transformCount=transforms.Length;
        for (int i=0;i<transforms.Length;i++)
        {
            string path=AnimationUtility.CalculateTransformPath(transforms[i],model.transform);
            mask.SetTransformPath(i,path);
            mask.SetTransformActive(i,weapon ? path.Contains("/Shoulder_") : transforms[i].name=="Chassis" || transforms[i].name=="Head");
        }
        AssetDatabase.CreateAsset(mask,Folder+"/"+name+".mask"); return mask;
    }
    private static AnimatorState State(AnimatorStateMachine machine,string name,Motion motion)
    {
        var state=machine.AddState(name); state.motion=motion; state.writeDefaultValues=false; return state;
    }
    private static void Setup(AnimatorStateTransition tr,float duration,bool exit=false)
    {
        tr.hasFixedDuration=true; tr.duration=duration; tr.hasExitTime=exit; tr.exitTime=1f;
        tr.interruptionSource=TransitionInterruptionSource.SourceThenDestination;
    }
    private static AnimatorStateTransition Transition(AnimatorState from,AnimatorState to,float duration,bool exit=false)
    { var tr=from.AddTransition(to); Setup(tr,duration,exit); return tr; }
}
