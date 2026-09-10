using UnityEditor;
using UnityEngine;
using AIDrivenFW.Config;

public class AiDriven_AIConfigEditorWindow : EditorWindow
{
    AIDrivenConfig config;
    UnityEditor.SerializedObject serializedConfig;
    UnityEditor.Editor configEditor;
    Vector2 scrollPosition;

    [MenuItem("Tools/AIDrivenFW/Settings")]
    public static void Open()
    {
        GetWindow<AiDriven_AIConfigEditorWindow>("AI Config");
    }

    void OnEnable()
    {
        config = AIDrivenConfig.Instance;
        if (config != null)
        {
            serializedConfig = new UnityEditor.SerializedObject(config);
            configEditor = UnityEditor.Editor.CreateEditor(config);
        }
    }

    void OnDisable()
    {
        if (configEditor != null)
        {
            UnityEngine.Object.DestroyImmediate(configEditor);
            configEditor = null;
        }
    }

    void OnGUI()
    {
        // Allow selecting a different config asset
        var newConfig = (AIDrivenConfig)EditorGUILayout.ObjectField("Config", config, typeof(AIDrivenConfig), false);
        if (newConfig != config)
        {
            config = newConfig;
            if (config != null)
            {
                serializedConfig = new UnityEditor.SerializedObject(config);
                if (configEditor != null) Object.DestroyImmediate(configEditor);
                configEditor = UnityEditor.Editor.CreateEditor(config);
            }
            else
            {
                serializedConfig = null;
                if (configEditor != null) Object.DestroyImmediate(configEditor);
                configEditor = null;
            }
        }

        if (config == null)
        {
            EditorGUILayout.HelpBox("Config not found", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("AI Settings", EditorStyles.boldLabel);

        // Draw the default inspector for the config so all serialized fields can be edited
        if (configEditor != null)
        {
            serializedConfig.Update();
            configEditor.OnInspectorGUI();
            serializedConfig.ApplyModifiedProperties();
        }

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            SaveConfig();
        }

        if (GUILayout.Button("Reset to Defaults"))
        {
            if (EditorUtility.DisplayDialog("Reset Defaults", "Reset config to default values?", "Reset", "Cancel"))
            {
                config.ResetToDefaults();
                // Refresh serialized object and editor
                serializedConfig = new UnityEditor.SerializedObject(config);
                if (configEditor != null) Object.DestroyImmediate(configEditor);
                configEditor = UnityEditor.Editor.CreateEditor(config);
                serializedConfig.Update();
                serializedConfig.ApplyModifiedProperties();
                SaveConfig();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    void SaveConfig()
    {
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("AI Config saved.");
        // Show confirmation dialog in English after saving
        EditorUtility.DisplayDialog("Save Complete", "AI Config has been saved.", "OK");
    }
}

// Run after imports have settled, never from a ScriptableObject constructor or OnValidate.
[InitializeOnLoad]
internal static class AIDrivenRecommendedModelsUpgrade
{
    static AIDrivenRecommendedModelsUpgrade()
    {
        Schedule();
    }

    internal static void Schedule()
    {
        if (Application.isBatchMode) return;
        EditorApplication.update -= CheckWhenReady;
        EditorApplication.update += CheckWhenReady;
    }

    private static void CheckWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;

        EditorApplication.update -= CheckWhenReady;
        // Do not use Instance: checking for an upgrade must not create an asset.
        foreach (string guid in AssetDatabase.FindAssets("t:AIDrivenConfig", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<AIDrivenConfig>(path);
            if (config == null || !config.HasPendingRecommendedModelsUpdate) continue;
            // A read-only asset cannot persist the decision; wait for a later reload/import.
            if (!AssetDatabase.IsOpenForEdit(path)) continue;

            bool updateModels = EditorUtility.DisplayDialog(
                "AIDrivenFW: Update Recommended Models",
                $"{path}\n\n" +
                "Update the recommended models for Ollama and llama.cpp?\n\n" +
                "Light: Gemma 3 1B\nBalanced: Gemma 3 4B\nPowerful: Gemma 4 12B\n\n" +
                "This replaces both model lists, including any custom model names, URLs, and VRAM settings. " +
                "Other settings, such as file paths, will be preserved. No models will be downloaded.\n\n" +
                "This prompt will not appear again for this update, regardless of your choice.",
                "Update Model Lists", "Keep Current Lists");

            config.ReviewRecommendedModelsUpdate(updateModels);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
        }
    }
}

internal class AIDrivenConfigUpgradePostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (string path in importedAssets)
        {
            if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)) continue;
            AIDrivenRecommendedModelsUpgrade.Schedule();
            break;
        }
    }
}
