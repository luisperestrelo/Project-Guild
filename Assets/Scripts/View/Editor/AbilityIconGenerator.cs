using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProjectGuild.Data;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View
{
    /// <summary>
    /// Editor tool that renders Synty PolygonIcons 3D prefabs to 2D sprite PNGs
    /// and wires them to AbilityConfigAsset SOs.
    /// Uses a real camera render with the project's URP pipeline asset.
    /// Re-running regenerates all icons (clears old ones first).
    /// </summary>
    public static class AbilityIconGenerator
    {
        private const int IconSize = 256;
        private const string OutputFolder = "Assets/Art/AbilityIcons";
        private const string PrefabRoot = "Assets/Art/Synty/PolygonIcons/Prefabs/";
        private const string URPAssetPath = "Assets/Settings/PC_RPAsset.asset";

        private struct IconDef
        {
            public string PrefabName;
            public Color? Tint;
        }

        private static readonly Dictionary<string, IconDef> AbilityIconMap = new()
        {
            { "basic_attack",       new IconDef { PrefabName = "SM_Icon_Sword_01" } },
            { "taunt",              new IconDef { PrefabName = "SM_Icon_Shield_01" } },
            { "mass_taunt",         new IconDef { PrefabName = "SM_Icon_Starburst_01" } },
            { "bloodthirst",        new IconDef { PrefabName = "SM_Icon_Skull_01",
                                        Tint = new Color(0.9f, 0.15f, 0.1f) } },
            { "fireball",           new IconDef { PrefabName = "SM_Icon_Fire_01" } },
            { "fire_nova",          new IconDef { PrefabName = "SM_Icon_Star_01" } },
            { "culling_frost",      new IconDef { PrefabName = "SM_Icon_Food_Iceblock_01" } },
            { "heal",               new IconDef { PrefabName = "SM_Icon_Heart_01" } },
            { "circle_of_mending",  new IconDef { PrefabName = "SM_Icon_Highlight_Circle_01" } },
            { "greater_heal",       new IconDef { PrefabName = "SM_Icon_Cross_01" } },
        };

        [MenuItem("Tools/Project Guild/Generate Ability Icons")]
        public static void GenerateIcons()
        {
            EnsureFolder(OutputFolder);

            // Load URP pipeline asset so the camera renders with proper shaders
            var urpAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(URPAssetPath);
            var previousPipeline = GraphicsSettings.defaultRenderPipeline;

            if (urpAsset != null)
                GraphicsSettings.defaultRenderPipeline = urpAsset;

            string[] guids = AssetDatabase.FindAssets("t:AbilityConfigAsset");
            int rendered = 0;

            try
            {
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<AbilityConfigAsset>(assetPath);
                    if (asset == null) continue;

                    if (!AbilityIconMap.TryGetValue(asset.Id, out var iconDef))
                    {
                        Debug.LogWarning($"[AbilityIconGenerator] No icon mapping for '{asset.Id}'.");
                        continue;
                    }

                    string prefabPath = PrefabRoot + iconDef.PrefabName + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[AbilityIconGenerator] Prefab not found: {prefabPath}");
                        continue;
                    }

                    var pngBytes = RenderPrefabToIcon(prefab, iconDef.Tint);
                    if (pngBytes == null)
                    {
                        Debug.LogWarning($"[AbilityIconGenerator] Render failed for '{iconDef.PrefabName}'.");
                        continue;
                    }

                    string texturePath = $"{OutputFolder}/Icon_{asset.Id}.png";
                    string diskPath = Application.dataPath + texturePath.Substring("Assets".Length);

                    string dir = System.IO.Path.GetDirectoryName(diskPath);
                    if (!System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);

                    System.IO.File.WriteAllBytes(diskPath, pngBytes);

                    AssetDatabase.ImportAsset(texturePath);
                    ConfigureAsSprite(texturePath);

                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
                    if (sprite != null)
                    {
                        asset.Icon = sprite;
                        EditorUtility.SetDirty(asset);
                        rendered++;
                    }
                }
            }
            finally
            {
                // Restore original pipeline
                GraphicsSettings.defaultRenderPipeline = previousPipeline;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AbilityIconGenerator] Rendered {rendered} icon(s).");
        }

        private static byte[] RenderPrefabToIcon(GameObject prefab, Color? tint)
        {
            // Use a hidden layer to avoid interference with the scene
            // Instantiate everything in the real scene but far away
            var offset = new Vector3(0, -500, 0);

            var instance = Object.Instantiate(prefab);
            instance.transform.position = offset;
            instance.transform.rotation = Quaternion.identity;
            instance.hideFlags = HideFlags.HideAndDontSave;

            // Calculate bounds
            var bounds = CalculateBounds(instance);

            // Camera
            var camGo = new GameObject("_IconCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 200f;
            cam.cullingMask = ~0; // everything

            // Add URP camera data
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderType = CameraRenderType.Base;

            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            if (maxExtent < 0.001f) maxExtent = 0.5f;
            cam.orthographicSize = maxExtent * 1.35f;

            // Camera from front-top angle
            var camDir = new Vector3(0.0f, 0.3f, -1.0f).normalized;
            camGo.transform.position = bounds.center + camDir * (maxExtent * 5f);
            camGo.transform.LookAt(bounds.center);

            // Lights
            var keyLight = new GameObject("_KeyLight") { hideFlags = HideFlags.HideAndDontSave };
            var kl = keyLight.AddComponent<Light>();
            kl.type = LightType.Directional;
            kl.intensity = 1.5f;
            kl.color = Color.white;
            keyLight.transform.rotation = Quaternion.Euler(30f, -30f, 0f);

            var fillLight = new GameObject("_FillLight") { hideFlags = HideFlags.HideAndDontSave };
            var fl = fillLight.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.intensity = 0.6f;
            fl.color = new Color(0.85f, 0.88f, 1f);
            fillLight.transform.rotation = Quaternion.Euler(60f, 150f, 0f);

            // Render
            var rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            // Read pixels
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // Apply tint
            if (tint.HasValue)
                ApplyTint(tex, tint.Value);

            byte[] png = tex.EncodeToPNG();

            // Cleanup
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(keyLight);
            Object.DestroyImmediate(fillLight);

            return png;
        }

        private static void ApplyTint(Texture2D tex, Color tint)
        {
            var pixels = tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 0.01f) continue;
                float lum = pixels[i].r * 0.3f + pixels[i].g * 0.59f + pixels[i].b * 0.11f;
                pixels[i] = new Color(
                    Mathf.Clamp01(tint.r * lum * 2f),
                    Mathf.Clamp01(tint.g * lum * 2f),
                    Mathf.Clamp01(tint.b * lum * 2f),
                    pixels[i].a);
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one * 0.5f);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ConfigureAsSprite(string texturePath)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType == TextureImporterType.Sprite) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
