using UnityEditor;
using UnityEngine;
using System.IO;

public class AIDrivenSetupWindow : EditorWindow
{
    // ===== UnityPackage paths =====
    private const string EXAMPLE_PACKAGE =
        "Packages/AIDrivenFrameWork/Editor/Setup/Packages/AIDriven_Samples.unitypackage";

    private const string AISetup_PACKAGE =
        "Packages/AIDrivenFrameWork/Editor/Setup/Packages/AIDriven_AISetup.unitypackage";

    // ===== Toggle states =====
    bool exampleSamples = true;
    bool AISetup = false;
    bool installDefaultSettings = false;

    // ===== UI State =====
    bool setupCompleted = false;

    [MenuItem("AIDrivenFramework/Setup")]
    static void Open()
    {
        GetWindow<AIDrivenSetupWindow>("AIDriven Framework Setup");
    }

    void OnGUI()
    {
        GUILayout.Space(8);
        GUILayout.Label("Optional Components", EditorStyles.boldLabel);
        GUILayout.Space(4);

        exampleSamples = EditorGUILayout.ToggleLeft(
            "Example Scene",
            exampleSamples
        );

        AISetup = EditorGUILayout.ToggleLeft(
            "AISetup",
            AISetup
        );

        GUILayout.Space(12);

        GUI.enabled = exampleSamples || AISetup || installDefaultSettings;

        if (GUILayout.Button("Install Selected", GUILayout.Height(28)))
        {
            InstallSelectedPackages();
        }

        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        DrawResultMessage();
    }

    void InstallSelectedPackages()
    {
        setupCompleted = false;

        if (exampleSamples)
            ImportUnityPackage(EXAMPLE_PACKAGE);

        if (AISetup)
            ImportUnityPackage(AISetup_PACKAGE);

        setupCompleted = true;
    }

    void ImportUnityPackage(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"UnityPackage not found: {path}");
            return;
        }

        // true = show Import Window (safe / OSS friendly)
        AssetDatabase.ImportPackage(path, true);
    }

    void DrawResultMessage()
    {
        if (!setupCompleted) return;

        GUILayout.Space(8);
        GUILayout.Label(
            "Setup Complete!",
            new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.green }
            }
        );
    }
}
