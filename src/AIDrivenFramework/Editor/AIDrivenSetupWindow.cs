using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

public class AIDrivenSetupWindow : EditorWindow
{
    private const string AISETUP_SCENE_PATH = "Assets/AIDrivenFW/AISetup/AIDrivenSetup.unity";

    private const string TEMP_IMPORT_ROOT = "Assets/AIDrivenFramework/TempPackages";

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
    bool isEndingImport = false;
    bool shouldAddAISetupSceneToBuild = false;
    bool importCallbacksRegistered = false;
    bool reloadAssembliesLocked = false;

    string currentImportSessionDir;
    string currentImportedTempPath;
    string currentExpectedPackageName;

    [MenuItem("Tools/AIDrivenFW/Optional Packages")]
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

        GUI.enabled = !isImporting && (exampleSamples || AISetup || installDefaultSettings);

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
        if (isImporting)
            return;

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
        try
        {
            EditorApplication.LockReloadAssemblies();
            reloadAssembliesLocked = true;

            CreateImportSession();

            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            AssetDatabase.importPackageFailed += OnImportPackageFailed;
            AssetDatabase.importPackageCancelled += OnImportPackageCancelled;
            importCallbacksRegistered = true;

            ImportNextFromQueue();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start the optional package import: {ex.Message}");
            EndImportQueue(success: false);
        }
    }

    void ImportNextFromQueue()
    {
        if (importQueue.Count == 0)
        {
            EndImportQueue(success: true);
            return;
        }

        var path = importQueue.Dequeue();
        currentExpectedPackageName = NormalizePackageName(path);
        if (string.IsNullOrEmpty(currentExpectedPackageName))
        {
            Debug.LogError($"Could not determine the UnityPackage name: {path}");
            EndImportQueue(success: false);
            return;
        }

        ImportUnityPackage(path);
    }

    void OnImportPackageCompleted(string packageName)
    {
        if (!TryConsumeExpectedPackageCallback(packageName))
            return;

        CleanupTempPackage();
        ImportNextFromQueue();
    }

    void OnImportPackageFailed(string packageName, string errorMessage)
    {
        if (!TryConsumeExpectedPackageCallback(packageName))
            return;

        CleanupTempPackage();
        Debug.LogError($"UnityPackage import failed: {packageName}\n{errorMessage}");
        EndImportQueue(success: false);
    }

    void OnImportPackageCancelled(string packageName)
    {
        if (!TryConsumeExpectedPackageCallback(packageName))
            return;

        Debug.LogWarning($"UnityPackage import cancelled: {packageName}");
        EndImportQueue(success: false);
    }

    bool TryConsumeExpectedPackageCallback(string packageName)
    {
        if (string.IsNullOrEmpty(currentExpectedPackageName) ||
            !PackageNamesMatch(currentExpectedPackageName, packageName))
        {
            return false;
        }

        currentExpectedPackageName = null;
        return true;
    }

    static bool PackageNamesMatch(string expectedPackageName, string callbackPackageName)
    {
        var normalizedExpectedName = NormalizePackageName(expectedPackageName);
        var normalizedCallbackName = NormalizePackageName(callbackPackageName);

        return !string.IsNullOrEmpty(normalizedExpectedName) &&
               string.Equals(
                   normalizedExpectedName,
                   normalizedCallbackName,
                   StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizePackageName(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return string.Empty;

        var normalizedPath = packageName.Trim().Replace('\\', '/').TrimEnd('/');
        var fileName = Path.GetFileName(normalizedPath);
        const string unityPackageExtension = ".unitypackage";

        if (fileName.EndsWith(unityPackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName.Substring(
                0,
                fileName.Length - unityPackageExtension.Length);
        }

        return fileName;
    }

    void EndImportQueue(bool success)
    {
        if (isEndingImport)
            return;

        if (!isImporting && !importCallbacksRegistered &&
            !reloadAssembliesLocked && string.IsNullOrEmpty(currentImportSessionDir) &&
            string.IsNullOrEmpty(currentExpectedPackageName))
            return;

        isEndingImport = true;
        isImporting = false;
        try
        {
            if (success && shouldAddAISetupSceneToBuild)
                AddSceneToBuildSettingsIfNeeded(AISETUP_SCENE_PATH);

            setupCompleted = success;
        }
        catch (Exception ex)
        {
            setupCompleted = false;
            Debug.LogError($"Failed to finish the optional package setup: {ex.Message}");
        }
        finally
        {
            try
            {
                ReleaseImportResources();
            }
            finally
            {
                isEndingImport = false;
                Repaint();
            }
        }
    }

    void OnDisable()
    {
        if (isImporting || importCallbacksRegistered || reloadAssembliesLocked ||
            !string.IsNullOrEmpty(currentImportSessionDir) ||
            !string.IsNullOrEmpty(currentExpectedPackageName))
        {
            EndImportQueue(success: false);
        }
    }

    void CreateImportSession()
    {
        string sessionDir;
        do
        {
            sessionDir = $"{TEMP_IMPORT_ROOT}/{Guid.NewGuid():N}";
        }
        while (Directory.Exists(sessionDir) || AssetDatabase.IsValidFolder(sessionDir));

        Directory.CreateDirectory(sessionDir);
        currentImportSessionDir = sessionDir;
        AssetDatabase.Refresh();
    }

    void ReleaseImportResources()
    {
        currentExpectedPackageName = null;

        try
        {
            CleanupTempPackage();
        }
        finally
        {
            try
            {
                if (importCallbacksRegistered)
                {
                    AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
                    AssetDatabase.importPackageFailed -= OnImportPackageFailed;
                    AssetDatabase.importPackageCancelled -= OnImportPackageCancelled;
                    importCallbacksRegistered = false;
                }
            }
            finally
            {
                try
                {
                    CleanupImportSession();
                    importQueue.Clear();
                }
                finally
                {
                    if (reloadAssembliesLocked)
                    {
                        try
                        {
                            EditorApplication.UnlockReloadAssemblies();
                        }
                        finally
                        {
                            reloadAssembliesLocked = false;
                        }
                    }
                }
            }
        }
    }

    void CleanupImportSession()
    {
        var sessionDir = currentImportSessionDir;
        currentImportSessionDir = null;

        if (string.IsNullOrEmpty(sessionDir))
            return;

        if (!IsValidImportSessionDirectory(sessionDir))
        {
            Debug.LogWarning($"Skipped cleanup for an unexpected import session path: {sessionDir}");
            return;
        }

        try
        {
            if (AssetDatabase.IsValidFolder(sessionDir) && AssetDatabase.DeleteAsset(sessionDir))
                return;

            var absoluteSessionDir = GetAbsoluteProjectPath(sessionDir);
            if (Directory.Exists(absoluteSessionDir))
                Directory.Delete(absoluteSessionDir, true);

            var metaPath = absoluteSessionDir + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to remove import session '{sessionDir}': {ex.Message}");
        }
    }

    static bool IsValidImportSessionDirectory(string sessionDir)
    {
        var normalizedRoot = TEMP_IMPORT_ROOT.Replace('\\', '/').TrimEnd('/');
        var normalizedSession = sessionDir.Replace('\\', '/').TrimEnd('/');
        var prefix = normalizedRoot + "/";

        if (!normalizedSession.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var sessionName = normalizedSession.Substring(prefix.Length);
        return sessionName.IndexOf('/') < 0 &&
               Guid.TryParseExact(sessionName, "N", out _);
    }

    static string GetAbsoluteProjectPath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
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
            if (string.IsNullOrEmpty(currentImportSessionDir) ||
                !IsValidImportSessionDirectory(currentImportSessionDir))
                throw new InvalidOperationException("The import session is not initialized.");

            var fileName = Path.GetFileName(path);
            currentImportedTempPath = Path.Combine(currentImportSessionDir, fileName).Replace('\\', '/');
            File.Copy(path, currentImportedTempPath, false);
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to prepare UnityPackage for import: {path}\n{ex.Message}");
            EndImportQueue(success: false);
            return;
        }

        try
        {
            // true = show Import Window (safe / OSS friendly)
            AssetDatabase.ImportPackage(currentImportedTempPath, true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import UnityPackage: {path}\n{ex.Message}");
            EndImportQueue(success: false);
        }
    }

    void CleanupTempPackage()
    {
        if (string.IsNullOrEmpty(currentImportedTempPath))
            return;

        try
        {
            if (!IsPathInsideCurrentImportSession(currentImportedTempPath))
            {
                Debug.LogWarning($"Skipped cleanup for an unexpected temporary package path: {currentImportedTempPath}");
                return;
            }

            if (AssetDatabase.DeleteAsset(currentImportedTempPath))
                return;

            var absoluteTempPath = GetAbsoluteProjectPath(currentImportedTempPath);
            if (File.Exists(absoluteTempPath))
                File.Delete(absoluteTempPath);

            var metaPath = absoluteTempPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to remove temporary package '{currentImportedTempPath}': {ex.Message}");
        }
        finally
        {
            currentImportedTempPath = null;
        }
    }

    bool IsPathInsideCurrentImportSession(string assetPath)
    {
        if (string.IsNullOrEmpty(currentImportSessionDir) ||
            !IsValidImportSessionDirectory(currentImportSessionDir))
            return false;

        var sessionPath = GetAbsoluteProjectPath(currentImportSessionDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidatePath = GetAbsoluteProjectPath(assetPath);
        var candidateParent = Path.GetDirectoryName(candidatePath);

        return string.Equals(candidateParent, sessionPath, StringComparison.OrdinalIgnoreCase);
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
