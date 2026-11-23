using AIDrivenFW.API;
using AIDrivenFW.Core;
using System.Threading;
using AIDrivenFW.Config;
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
        "Packages/AIDriven_Example.unitypackage";

    private const string AISetup_PACKAGE =
        "Packages/AIDriven_AISetup.unitypackage";

    // ===== Toggle states =====
    bool exampleSamples = true;
    bool AISetup = false;

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

    [SerializeField] GenAIConfig developerConfig;
    [SerializeField] DeveloperBackend developerBackend;
    CancellationTokenSource generationCancellation;
    string generationResult;
    MessageType generationResultType;

    enum DeveloperBackend
    {
        [InspectorName("llama.cpp Server (Recommended)")]
        LlamaServer,
        [InspectorName("llama.cpp CLI")]
        LlamaCli
    }

    [MenuItem("Tools/AIDrivenFW/Setup")]
    [MenuItem("Tools/AIDrivenFW/Optional Packages")]
    static void Open()
    {
        GetWindow<AIDrivenSetupWindow>("AIDriven Framework Setup");
    }

    void OnGUI()
    {
        GUILayout.Space(8);
        DrawDeveloperConfig();
        GUILayout.Space(12);
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

        if (exampleSamples && !AISetup && !HasSetupScene())
            EditorGUILayout.HelpBox("The Example Scene requires AISetup. AISetup will be imported first.", MessageType.Info);

        GUILayout.Space(12);

        GUI.enabled = !isImporting && (exampleSamples || AISetup);

        if (GUILayout.Button("Install Selected", GUILayout.Height(28)))
        {
            InstallSelectedPackages();
        }

        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        DrawResultMessage();
    }

    void DrawDeveloperConfig()
    {
        GUILayout.Label("Generation Configuration", EditorStyles.boldLabel);
        developerBackend = (DeveloperBackend)EditorGUILayout.EnumPopup("Backend", developerBackend);
        EditorGUILayout.HelpBox(
            "Install the llama.cpp runtime and a GGUF model. Use the AISetup scene to prepare them, " +
            "or select an existing configuration below.",
            MessageType.Info);

        developerConfig = (GenAIConfig)EditorGUILayout.ObjectField(
            "Configuration", developerConfig, typeof(GenAIConfig), false);
        if (developerConfig == null)
        {
            if (GUILayout.Button("Create Configuration"))
                CreateDeveloperConfig();
        }
        else if (GUILayout.Button("Open Configuration in Inspector"))
        {
            Selection.activeObject = developerConfig;
        }

        var dependencyProblem = GetDependencyProblem();
        if (dependencyProblem != null)
            EditorGUILayout.HelpBox(dependencyProblem, MessageType.Warning);
        else if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Enter Play Mode to run test generation.", MessageType.Info);

        if (generationCancellation != null)
        {
            if (GUILayout.Button("Cancel Test Generation"))
                generationCancellation.Cancel();
        }
        else
        {
            using (new EditorGUI.DisabledScope(dependencyProblem != null || !EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Run Test Generation"))
                    RunTestGeneration();
            }
        }

        if (!string.IsNullOrEmpty(generationResult))
            EditorGUILayout.HelpBox(generationResult, generationResultType);
    }

    string GetDependencyProblem()
    {
        try
        {
            var executable = developerBackend == DeveloperBackend.LlamaCli
                ? LlamaCliExecutor.FindExecutable()
                : !string.IsNullOrWhiteSpace(developerConfig?.aiSoftwarePath)
                    ? developerConfig.aiSoftwarePath
                    : LlamaHTTPExecutor.FindServerExecutable();
            if (!File.Exists(executable))
                return $"Install the selected llama.cpp executable. Expected: {executable}";

            ModelRepository.GetRequiredModelExecutablePath(developerConfig);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    async void RunTestGeneration()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
        generationCancellation = cancellation;
        generationResult = "Generating a test response…";
        generationResultType = MessageType.Info;
        GenAI ai = null;
        GenAIConfig config = null;

        try
        {
            if (developerConfig != null)
                config = Instantiate(developerConfig);
            IGenerateExecutor executor = developerBackend == DeveloperBackend.LlamaCli
                ? new LlamaCliExecutor()
                : new LlamaHTTPExecutor();
            ai = new GenAI(executor);
            var response = await ai.Generate("Reply with a short greeting.", config,
                ct: cancellation.Token, timeoutMs: 60000, retryAfterInitialization: false);
            generationResult = string.IsNullOrWhiteSpace(response) ? "The model returned an empty response." : response.Trim();
            generationResultType = string.IsNullOrWhiteSpace(response) ? MessageType.Warning : MessageType.Info;
        }
        catch (OperationCanceledException)
        {
            generationResult = "Test generation canceled.";
        }
        catch (Exception ex)
        {
            generationResult = ex.Message;
            generationResultType = MessageType.Error;
        }
        finally
        {
            try
            {
                ai?.KillProcess();
            }
            finally
            {
                if (config != null)
                    DestroyImmediate(config);
                generationCancellation = null;
                if (this != null)
                    Repaint();
            }
        }
    }

    void CreateDeveloperConfig()
    {
        var path = EditorUtility.SaveFilePanelInProject(
            "Create Generation Configuration", "GenAIConfig", "asset",
            "Choose a location for the new configuration.");
        if (string.IsNullOrEmpty(path))
            return;

        var config = CreateInstance<GenAIConfig>();
        AssetDatabase.CreateAsset(config, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        developerConfig = config;
        Selection.activeObject = config;
    }

    void InstallSelectedPackages()
    {
        if (isImporting)
            return;

        setupCompleted = false;

        importQueue.Clear();
        shouldAddAISetupSceneToBuild = AISetup || exampleSamples;

        if (AISetup || (exampleSamples && !HasSetupScene()))
            importQueue.Enqueue(AISetup_PACKAGE);

        if (exampleSamples)
            importQueue.Enqueue(EXAMPLE_PACKAGE);

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

        var package = importQueue.Dequeue();
        if (package == EXAMPLE_PACKAGE && !HasSetupScene())
        {
            Debug.LogError("Import the AIDriven Setup scene before importing the Example Scene.");
            EndImportQueue(success: false);
            return;
        }

        var path = ResolvePackagePath(package);
        currentExpectedPackageName = NormalizePackageName(path);
        if (string.IsNullOrEmpty(currentExpectedPackageName))
        {
            Debug.LogError($"Could not determine the UnityPackage name: {path}");
            EndImportQueue(success: false);
            return;
        }

        ImportUnityPackage(path);
    }

    static bool HasSetupScene()
    {
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(AISETUP_SCENE_PATH) != null;
    }

    string ResolvePackagePath(string relativePath)
    {
        var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var editorDirectory = Path.GetDirectoryName(scriptPath);
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(scriptPath);
        if (package == null)
            return Path.Combine(editorDirectory, relativePath);

        var packageRelativeDirectory = editorDirectory.Replace('\\', '/')
            .Substring(package.assetPath.Length).TrimStart('/');
        return Path.Combine(package.resolvedPath, packageRelativeDirectory, relativePath);
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
            {
                success = HasSetupScene();
                if (success)
                    AddSceneToBuildSettingsIfNeeded(AISETUP_SCENE_PATH);
                else
                    Debug.LogError("The AIDriven Setup scene was not imported.");
            }

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
        generationCancellation?.Cancel();
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
