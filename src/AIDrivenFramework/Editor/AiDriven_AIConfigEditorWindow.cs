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