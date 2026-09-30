using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyArenaImport
{
    [Serializable] public class PaletteEntry { public string name; public string hex; }
    [Serializable] public class PaletteData { public PaletteEntry[] colors; }

    // Scoped to this exported asset. Does not change any project settings.
    public sealed class SkyArenaModelImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != SkyArenaMaterials.ModelPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            // No tangents: the arena has no UVs and its 14 materials are flat colour
            // (no normal maps), so tangents were 16 unused bytes per vertex - about
            // 3.8 MB of the build's largest mesh.
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.preserveHierarchy = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }

    [InitializeOnLoad]
    public static class SkyArenaMaterials
    {
        public const string Root = "Assets/SkyArena";
        public const string ModelPath = Root + "/SkyArena.fbx";
        static SkyArenaMaterials()
        {
            // On first import into a project using another pipeline, adapt only
            // these packaged materials. User material colors are preserved.
            EditorApplication.delayCall += AdaptIfNeeded;
        }

        public static Shader ActiveShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return Shader.Find("SkyArena/Painted Two Sided");
            string type = pipeline.GetType().Name;
            if (type.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
            if (type.Contains("HDRender")) return Shader.Find("HDRP/Lit");
            throw new InvalidOperationException("SkyArena: unsupported render pipeline " + type);
        }

        static void AdaptIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += AdaptIfNeeded; return; }
            if (!File.Exists(Root + "/Materials/MAT_Floor_Cream.mat")) return;
            Shader shader = ActiveShader();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_Floor_Cream.mat");
            if (shader != null && mat != null && mat.shader != shader) MatchPipeline();
        }

        static void SetFloat(Material m, string name, float value)
        { if (m.HasProperty(name)) m.SetFloat(name, value); }

        static void Configure(Material m, Shader shader, Color color, bool light)
        {
            m.shader = shader;
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            SetFloat(m,"_Metallic",0); SetFloat(m,"_Smoothness",0.26f);
            SetFloat(m,"_Glossiness",0.26f); SetFloat(m,"_Cull",0);
            SetFloat(m,"_CullMode",0); SetFloat(m,"_CullModeForward",0);
            SetFloat(m,"_DoubleSidedEnable",1);
            if (shader.name == "HDRP/Lit")
            {
                m.EnableKeyword("_DOUBLESIDED_ON");
                if(m.HasProperty("_DoubleSidedConstants")) m.SetVector("_DoubleSidedConstants", new Vector4(-1,-1,-1,0));
            }
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.02f,0.02f,0.02f));
            Color emission = light ? color * 0.25f : Color.black;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", emission);
            if(light) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            m.doubleSidedGI = true;
        }

        [MenuItem("Tools/SkyArena/Match Materials to Active Pipeline")]
        public static void MatchPipeline()
        { ApplyPalette(false); }

        [MenuItem("Tools/SkyArena/Restore Current Palette Colors")]
        public static void RestoreReferenceColors()
        { ApplyPalette(true); }

        static void ApplyPalette(bool restoreColors)
        {
            var shader = ActiveShader();
            if (shader == null) throw new InvalidOperationException("Required SkyArena shader is unavailable.");
            var palette = JsonUtility.FromJson<PaletteData>(File.ReadAllText(Root + "/palette.json"));
            Directory.CreateDirectory(Root + "/Materials");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            foreach (var entry in palette.colors)
            {
                string path = Root + "/Materials/" + entry.name + ".mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = m == null;
                Color color;
                ColorUtility.TryParseHtmlString(entry.hex, out color);
                if (!isNew && !restoreColors) color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                if (isNew) m = new Material(shader) { name = entry.name };
                Configure(m, shader, color, entry.name == "MAT_Warm_Light");
                if(isNew) AssetDatabase.CreateAsset(m,path); else EditorUtility.SetDirty(m);
                importer?.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),entry.name),m);
            }
            AssetDatabase.SaveAssets();
            if (importer != null) importer.SaveAndReimport();
        }
    }
}
