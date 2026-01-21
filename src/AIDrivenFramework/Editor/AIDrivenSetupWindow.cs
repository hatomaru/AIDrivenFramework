using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class AIDrivenSetupWindow : EditorWindow
{
    private const string AISETUP_SCENE_PATH = "Assets/AIDrivenFW/AISetup/AIDrivenSetup.unity";

    private const string TEMP_IMPORT_DIR = "Assets/AIDrivenFramework/TempPackages";

    // ===== UnityPackage paths =====
    private const string EXAMPLE_PACKAGE =
        "Packages/com.hatomaru.ai.framework/Editor/Packages/AIDriven_Example.unitypackage";

    private const string AISetup_PACKAGE =
        "Packages/com.hatomaru.ai.framework/Editor/Packages/AIDriven_AISetup.unitypackage";

    // ===== Toggle states =====
    bool exampleSamples = true;
    bool AISetup = false;
    bool installDefaultSettings = false;

    // ===== UI State =====
    bool setupCompleted = false;

    // ===== Import Queue State =====
    readonly Queue<string> importQueue = new Queue<string>();
    bool isImporting = false;
    bool shouldAddAISetupSceneToBuild = false;

    string currentImportedTempPath;

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

        importQueue.Clear();
        shouldAddAISetupSceneToBuild = AISetup;

        if (exampleSamples)
            importQueue.Enqueue(EXAMPLE_PACKAGE);

        if (AISetup)
            importQueue.Enqueue(AISetup_PACKAGE);

        if (importQueue.Count == 0)
            return;

        StartImportQueue();
    }

    void StartImportQueue()
    {
        if (isImporting)
            return;

        isImporting = true;
        EditorApplication.LockReloadAssemblies();
        AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
        AssetDatabase.importPackageFailed += OnImportPackageFailed;

        ImportNextFromQueue();
    }

    void ImportNextFromQueue()
    {
        if (importQueue.Count == 0)
        {
            EndImportQueue(success: true);
            return;
        }

        var path = importQueue.Dequeue();
        ImportUnityPackage(path);
    }

    void OnImportPackageCompleted(string packageName)
    {
        CleanupTempPackage();
        ImportNextFromQueue();
    }

    void OnImportPackageFailed(string packageName, string errorMessage)
    {
        CleanupTempPackage();
        Debug.LogError($"UnityPackage import failed: {packageName}\n{errorMessage}");
        EndImportQueue(success: false);
    }

    void EndImportQueue(bool success)
    {
        if (!isImporting)
            return;

        isImporting = false;
        CleanupTempPackage();
        AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
        AssetDatabase.importPackageFailed -= OnImportPackageFailed;
        EditorApplication.UnlockReloadAssemblies();

        if (success && shouldAddAISetupSceneToBuild)
            AddSceneToBuildSettingsIfNeeded(AISETUP_SCENE_PATH);

        setupCompleted = success;
        Repaint();
    }

    static void AddSceneToBuildSettingsIfNeeded(string sceneAssetPath)
    {
        if (string.IsNullOrWhiteSpace(sceneAssetPath))
            return;

        if (!File.Exists(sceneAssetPath))
        {
            Debug.LogError($"Scene not found: {sceneAssetPath}");
            return;
        }

        var scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (string.Equals(scenes[i].path, sceneAssetPath, System.StringComparison.Ordinal))
                return;
        }

        var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
        scenes.CopyTo(newScenes, 0);
        newScenes[newScenes.Length - 1] = new EditorBuildSettingsScene(sceneAssetPath, true);
        EditorBuildSettings.scenes = newScenes;
    }

    void ImportUnityPackage(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"UnityPackage not found: {path}");
            EndImportQueue(success: false);
            return;
        }

        // Importing directly from `Packages/` can involve Temp/PackageCache timing issues.
        // Copy to a stable location under `Assets/` before importing.
        try
        {
            if (!AssetDatabase.IsValidFolder(TEMP_IMPORT_DIR))
            {
                Directory.CreateDirectory(TEMP_IMPORT_DIR);
                AssetDatabase.Refresh();
            }

            var fileName = Path.GetFileName(path);
            currentImportedTempPath = Path.Combine(TEMP_IMPORT_DIR, fileName).Replace('\\', '/');
            File.Copy(path, currentImportedTempPath, true);
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to prepare UnityPackage for import: {path}\n{ex.Message}");
            EndImportQueue(success: false);
            return;
        }

        // true = show Import Window (safe / OSS friendly)
        AssetDatabase.ImportPackage(currentImportedTempPath, true);
    }

    void CleanupTempPackage()
    {
        if (string.IsNullOrEmpty(currentImportedTempPath))
            return;

        try
        {
            if (File.Exists(currentImportedTempPath))
                File.Delete(currentImportedTempPath);
        }
        catch
        {
            // best-effort cleanup
        }
        finally
        {
            currentImportedTempPath = null;
        }
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
