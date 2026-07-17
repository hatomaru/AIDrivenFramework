using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

internal static class AIDrivenOptionalFontResources
{
    private const string FontPackageName = "AIDriven_Fonts";
    private const string FontPackagePath =
        "Packages/com.hatomaru.ai.framework/Editor/Packages/AIDriven_Fonts.unitypackage";
    private const string FontManifestPath =
        "Packages/com.hatomaru.ai.framework/Editor/Packages/AIDriven_Fonts.manifest.json";

    private const string AISetupPath = "Assets/AIDrivenFW/AISetup";
    private const string SamplePath = "Assets/AIDrivenFW/Sample";
    private const string OptionalContentRoot = "Assets/AIDrivenFW";
    private const string SharedRoot = "Assets/AIDrivenFW/Shared";
    private const string DestinationRoot = "Assets/AIDrivenFW/Shared/Fonts";
    private const string MediumFontGuid = "9587ffef7ed1a054494f0244943458cf";
    private const string LightFontGuid = "86c9d6a06c3775240b8b2ecfb1eca52b";
    private const string AutoRepairAttemptedKey =
        "AIDrivenFW.OptionalFontResources.AutoRepairAttempted";
    private const int ExpectedManifestEntryCount = 11;
    private const int ExpectedAssetEntryCount = 8;

    private static bool repairScheduled;
    private static bool waitingForEditorIdle;
    private static bool manualRepairQueued;
    private static bool fontImportInProgress;

    private enum PreflightStatus
    {
        ReadyToImport,
        AlreadyInstalled,
        Blocked
    }

    [Serializable]
    private sealed class FontPackageManifest
    {
        public FontPackageManifestEntry[] entries;
    }

    [Serializable]
    private sealed class FontPackageManifestEntry
    {
        public string path;
        public string guid;
        public string kind;
    }

    private sealed class PreflightResult
    {
        public PreflightStatus Status { get; }
        public string Message { get; }

        public PreflightResult(PreflightStatus status, string message)
        {
            Status = status;
            Message = message;
        }
    }

    [MenuItem("Tools/AIDrivenFW/Repair Optional Font Resources")]
    private static void RepairFromMenu()
    {
        QueueRepair(manual: true);
    }

    internal static void OnAssetsPostprocessed(string[] importedAssets, bool didDomainReload)
    {
        if (didDomainReload || ContainsOptionalContent(importedAssets))
            QueueRepair(manual: false);
    }

    private static void QueueRepair(bool manual)
    {
        // Import workers have isolated global state and must not schedule Editor work.
        if (AssetDatabase.IsAssetImportWorkerProcess())
            return;

        if (fontImportInProgress)
        {
            if (manual)
                Debug.Log("Optional font resources are already being imported.");
            return;
        }

        if (!manual && SessionState.GetBool(AutoRepairAttemptedKey, false))
            return;

        if (manual)
            manualRepairQueued = true;

        if (IsEditorBusy())
        {
            WaitForEditorIdle();
            return;
        }

        ScheduleRepairOnce();
    }

    private static void WaitForEditorIdle()
    {
        if (waitingForEditorIdle)
            return;

        waitingForEditorIdle = true;
        EditorApplication.update += OnEditorUpdateWhileWaiting;
    }

    private static void OnEditorUpdateWhileWaiting()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess())
        {
            StopWaitingForEditorIdle();
            return;
        }

        if (IsEditorBusy())
            return;

        StopWaitingForEditorIdle();
        ScheduleRepairOnce();
    }

    private static void StopWaitingForEditorIdle()
    {
        if (!waitingForEditorIdle)
            return;

        waitingForEditorIdle = false;
        EditorApplication.update -= OnEditorUpdateWhileWaiting;
    }

    private static void ScheduleRepairOnce()
    {
        if (repairScheduled)
            return;

        repairScheduled = true;
        EditorApplication.delayCall += ProcessScheduledRepair;
    }

    private static void ProcessScheduledRepair()
    {
        repairScheduled = false;

        if (IsEditorBusy())
        {
            WaitForEditorIdle();
            return;
        }

        var isManualRepair = manualRepairQueued;
        manualRepairQueued = false;

        if (!isManualRepair && SessionState.GetBool(AutoRepairAttemptedKey, false))
            return;

        TryImportFontResources(isManualRepair);
    }

    private static void TryImportFontResources(bool isManualRepair)
    {
        if (!isManualRepair && !HasOptionalContent())
            return;

        // Mark before preflight/import so post-process callbacks and failures cannot
        // create an automatic retry loop. Manual repair always remains retryable.
        SessionState.SetBool(AutoRepairAttemptedKey, true);

        if (!TryLoadManifest(out var manifest, out var manifestError))
        {
            ReportBlockedRepair(
                isManualRepair,
                "Optional font repair was stopped because its manifest is invalid.\n\n" +
                manifestError);
            return;
        }

        var preflight = RunPreflight(manifest);
        if (preflight.Status == PreflightStatus.AlreadyInstalled)
        {
            const string message =
                "All optional font resources already resolve to their expected paths and GUIDs.";
            Debug.Log(message);

            if (isManualRepair)
                EditorUtility.DisplayDialog("Optional Font Resources", message, "OK");

            return;
        }

        if (preflight.Status == PreflightStatus.Blocked)
        {
            ReportBlockedRepair(isManualRepair, preflight.Message);
            return;
        }

        var resolvedFontPackagePath = ResolvePackageAssetPath(FontPackagePath);
        if (!File.Exists(resolvedFontPackagePath))
        {
            ReportBlockedRepair(
                isManualRepair,
                $"Optional font resource package was not found: {FontPackagePath}\n" +
                $"Resolved path: {resolvedFontPackagePath}");
            return;
        }

        fontImportInProgress = true;
        SubscribeImportCallbacks();

        try
        {
            // Preflight proved every destination asset is absent or has its expected
            // folder GUID, so this non-interactive import cannot overwrite unknown data.
            AssetDatabase.ImportPackage(resolvedFontPackagePath, interactive: false);
        }
        catch (Exception exception)
        {
            FinishImport(
                success: false,
                $"Optional font resource import could not start.\n{exception}");
        }
    }

    private static bool TryLoadManifest(
        out FontPackageManifest manifest,
        out string errorMessage)
    {
        manifest = null;
        errorMessage = null;

        try
        {
            var resolvedManifestPath = ResolvePackageAssetPath(FontManifestPath);
            if (!File.Exists(resolvedManifestPath))
            {
                errorMessage =
                    $"Manifest was not found: {FontManifestPath}\n" +
                    $"Resolved path: {resolvedManifestPath}";
                return false;
            }

            var json = File.ReadAllText(resolvedManifestPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "The optional font manifest is empty.";
                return false;
            }

            manifest = JsonUtility.FromJson<FontPackageManifest>(json);
            if (manifest?.entries == null ||
                manifest.entries.Length != ExpectedManifestEntryCount)
            {
                errorMessage =
                    $"Expected {ExpectedManifestEntryCount} manifest entries, but found " +
                    $"{manifest?.entries?.Length ?? 0}.";
                return false;
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assetEntryCount = 0;

            foreach (var entry in manifest.entries)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.path) ||
                    string.IsNullOrWhiteSpace(entry.guid) ||
                    string.IsNullOrWhiteSpace(entry.kind))
                {
                    errorMessage = "Every manifest entry must contain path, guid, and kind.";
                    return false;
                }

                if (!IsUnityGuid(entry.guid))
                {
                    errorMessage = $"Manifest entry has an invalid GUID: {entry.guid}";
                    return false;
                }

                if (!string.Equals(entry.kind, "asset", StringComparison.Ordinal) &&
                    !string.Equals(entry.kind, "folder", StringComparison.Ordinal))
                {
                    errorMessage =
                        $"Manifest entry has an unsupported kind: {entry.path} ({entry.kind})";
                    return false;
                }

                var isAssetEntry = string.Equals(
                    entry.kind,
                    "asset",
                    StringComparison.Ordinal);
                var hasExpectedPath = isAssetEntry
                    ? IsPathAtOrBelow(entry.path, DestinationRoot) &&
                      !string.Equals(entry.path, DestinationRoot, StringComparison.Ordinal)
                    : string.Equals(entry.path, OptionalContentRoot, StringComparison.Ordinal) ||
                      string.Equals(entry.path, SharedRoot, StringComparison.Ordinal) ||
                      string.Equals(entry.path, DestinationRoot, StringComparison.Ordinal);

                if (!hasExpectedPath)
                {
                    errorMessage =
                        $"Manifest entry points outside the expected font destination: " +
                        $"{entry.path} ({entry.kind})";
                    return false;
                }

                if (!paths.Add(entry.path))
                {
                    errorMessage = $"Manifest contains a duplicate path: {entry.path}";
                    return false;
                }

                if (!guids.Add(entry.guid))
                {
                    errorMessage = $"Manifest contains a duplicate GUID: {entry.guid}";
                    return false;
                }

                if (isAssetEntry)
                    assetEntryCount++;
            }

            if (assetEntryCount != ExpectedAssetEntryCount)
            {
                errorMessage =
                    $"Expected {ExpectedAssetEntryCount} asset entries, but found " +
                    $"{assetEntryCount}.";
                return false;
            }

            if (!guids.Contains(MediumFontGuid) || !guids.Contains(LightFontGuid))
            {
                errorMessage = "The manifest does not contain both expected SDF font GUIDs.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage = $"Failed to read the optional font manifest.\n{exception}";
            manifest = null;
            return false;
        }
    }

    private static PreflightResult RunPreflight(FontPackageManifest manifest)
    {
        var manifestPaths = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var installedAssets = new List<string>();
        var missingAssets = new List<string>();

        foreach (var entry in manifest.entries)
        {
            manifestPaths.Add(entry.path);

            var pathResolvedFromGuid = AssetDatabase.GUIDToAssetPath(entry.guid);
            var destinationExists = AssetDatabase.AssetPathExists(entry.path);
            var guidAtDestination = destinationExists
                ? AssetDatabase.AssetPathToGUID(entry.path)
                : string.Empty;

            var guidResolvesToExpectedPath = string.Equals(
                pathResolvedFromGuid,
                entry.path,
                StringComparison.Ordinal);
            var destinationHasExpectedGuid = destinationExists && string.Equals(
                guidAtDestination,
                entry.guid,
                StringComparison.OrdinalIgnoreCase);

            if (guidResolvesToExpectedPath && destinationHasExpectedGuid)
            {
                if (string.Equals(entry.kind, "asset", StringComparison.Ordinal))
                    installedAssets.Add(DescribeEntry(entry));
                continue;
            }

            if (string.IsNullOrEmpty(pathResolvedFromGuid) && !destinationExists)
            {
                if (string.Equals(entry.kind, "asset", StringComparison.Ordinal))
                    missingAssets.Add(DescribeEntry(entry));
                continue;
            }

            var problemCountBeforeEntry = problems.Count;

            if (!string.IsNullOrEmpty(pathResolvedFromGuid) &&
                !string.Equals(pathResolvedFromGuid, entry.path, StringComparison.Ordinal))
            {
                problems.Add(
                    $"GUID {entry.guid} resolves to '{pathResolvedFromGuid}', " +
                    $"but the manifest expects '{entry.path}'.");
            }

            if (destinationExists &&
                !string.Equals(guidAtDestination, entry.guid, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Destination '{entry.path}' already exists with GUID " +
                    $"{guidAtDestination}; expected {entry.guid}.");
            }

            if (problems.Count == problemCountBeforeEntry)
            {
                problems.Add(
                    $"Asset database state is inconsistent for '{entry.path}' " +
                    $"(expected GUID {entry.guid}, resolved path " +
                    $"'{DisplayValue(pathResolvedFromGuid)}', destination GUID " +
                    $"'{DisplayValue(guidAtDestination)}').");
            }
        }

        if (problems.Count > 0)
            return BlockedResult(problems);

        if (installedAssets.Count == ExpectedAssetEntryCount)
            return new PreflightResult(PreflightStatus.AlreadyInstalled, null);

        AddUnexpectedDestinationAssets(manifestPaths, problems);
        if (problems.Count > 0)
            return BlockedResult(problems);

        if (installedAssets.Count == 0 && missingAssets.Count == ExpectedAssetEntryCount)
            return new PreflightResult(PreflightStatus.ReadyToImport, null);

        problems.Add(
            $"Only {installedAssets.Count} of {ExpectedAssetEntryCount} font assets are " +
            "already installed. Silent repair cannot safely merge a partial installation.");

        foreach (var installed in installedAssets)
            problems.Add($"Already installed: {installed}");

        foreach (var missing in missingAssets)
            problems.Add($"Missing: {missing}");

        return BlockedResult(problems);
    }

    private static void AddUnexpectedDestinationAssets(
        HashSet<string> manifestPaths,
        List<string> problems)
    {
        if (!AssetDatabase.IsValidFolder(DestinationRoot))
            return;

        var unexpectedPaths = new HashSet<string>(StringComparer.Ordinal);
        var discoveredGuids = AssetDatabase.FindAssets(
            string.Empty,
            new[] { DestinationRoot });

        foreach (var guid in discoveredGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || manifestPaths.Contains(path))
                continue;

            unexpectedPaths.Add(path);
        }

        var visitedFolders = new HashSet<string>(StringComparer.Ordinal)
        {
            DestinationRoot
        };
        var foldersToInspect = new Queue<string>();
        foldersToInspect.Enqueue(DestinationRoot);

        while (foldersToInspect.Count > 0)
        {
            var folder = foldersToInspect.Dequeue();
            foreach (var subFolder in AssetDatabase.GetSubFolders(folder))
            {
                if (!visitedFolders.Add(subFolder))
                    continue;

                foldersToInspect.Enqueue(subFolder);
                if (!manifestPaths.Contains(subFolder))
                    unexpectedPaths.Add(subFolder);
            }
        }

        var sortedUnexpectedPaths = new List<string>(unexpectedPaths);
        sortedUnexpectedPaths.Sort(StringComparer.Ordinal);

        foreach (var path in sortedUnexpectedPaths)
        {
            problems.Add(
                $"Unexpected asset or folder exists under the font destination: '{path}' " +
                $"(GUID {AssetDatabase.AssetPathToGUID(path)}).");
        }
    }

    private static PreflightResult BlockedResult(List<string> problems)
    {
        var message = new StringBuilder();
        message.AppendLine(
            "Optional font repair was blocked to prevent a silent overwrite.");
        message.AppendLine();

        foreach (var problem in problems)
            message.AppendLine($"- {problem}");

        message.AppendLine();
        message.AppendLine(
            $"Back up '{DestinationRoot}' and any asset paths listed above. " +
            "Then remove or relocate the conflicting/partial assets in Unity's Project " +
            "window so their associated .meta files are handled together, and run " +
            "Tools/AIDrivenFW/Repair Optional Font Resources again.");

        return new PreflightResult(PreflightStatus.Blocked, message.ToString());
    }

    private static void ReportBlockedRepair(bool isManualRepair, string message)
    {
        Debug.LogError(message);

        if (isManualRepair)
        {
            EditorUtility.DisplayDialog(
                "Optional Font Repair Blocked",
                message,
                "OK");
        }
    }

    private static string DescribeEntry(FontPackageManifestEntry entry)
    {
        return $"'{entry.path}' (GUID {entry.guid})";
    }

    private static string DisplayValue(string value)
    {
        return string.IsNullOrEmpty(value) ? "<none>" : value;
    }

    private static bool IsUnityGuid(string value)
    {
        if (value.Length != 32)
            return false;

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }

    private static bool HasOptionalContent()
    {
        return AssetDatabase.IsValidFolder(AISetupPath) ||
               AssetDatabase.IsValidFolder(SamplePath);
    }

    private static string ResolvePackageAssetPath(string assetPath)
    {
        var packageInfo = PackageInfo.FindForAssetPath(assetPath);
        if (packageInfo == null ||
            string.IsNullOrEmpty(packageInfo.assetPath) ||
            string.IsNullOrEmpty(packageInfo.resolvedPath))
        {
            return Path.GetFullPath(assetPath);
        }

        var packageAssetRoot = packageInfo.assetPath.TrimEnd('/', '\\');
        if (!assetPath.StartsWith(packageAssetRoot + "/", StringComparison.Ordinal))
            return Path.GetFullPath(assetPath);

        var relativePath = assetPath.Substring(packageAssetRoot.Length + 1);
        return Path.Combine(packageInfo.resolvedPath, relativePath);
    }

    private static bool ContainsOptionalContent(string[] importedAssets)
    {
        if (importedAssets == null)
            return false;

        foreach (var path in importedAssets)
        {
            if (IsPathAtOrBelow(path, AISetupPath) || IsPathAtOrBelow(path, SamplePath))
                return true;
        }

        return false;
    }

    private static bool IsPathAtOrBelow(string path, string root)
    {
        return string.Equals(path, root, StringComparison.Ordinal) ||
               (!string.IsNullOrEmpty(path) &&
                path.StartsWith(root + "/", StringComparison.Ordinal));
    }

    private static bool IsEditorBusy()
    {
        return EditorApplication.isCompiling ||
               EditorApplication.isUpdating ||
               EditorApplication.isPlayingOrWillChangePlaymode ||
               AIDrivenPackageImportTracker.IsOtherPackageImportInProgress;
    }

    private static void SubscribeImportCallbacks()
    {
        AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
        AssetDatabase.importPackageFailed += OnImportPackageFailed;
        AssetDatabase.importPackageCancelled += OnImportPackageCancelled;
    }

    private static void UnsubscribeImportCallbacks()
    {
        AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
        AssetDatabase.importPackageFailed -= OnImportPackageFailed;
        AssetDatabase.importPackageCancelled -= OnImportPackageCancelled;
    }

    private static void OnImportPackageCompleted(string packageName)
    {
        if (!IsFontPackageEvent(packageName))
            return;

        FinishImport(success: true, message: null);
    }

    private static void OnImportPackageFailed(string packageName, string errorMessage)
    {
        if (!IsFontPackageEvent(packageName))
            return;

        FinishImport(
            success: false,
            $"Optional font resource import failed: {packageName}\n{errorMessage}");
    }

    private static void OnImportPackageCancelled(string packageName)
    {
        if (!IsFontPackageEvent(packageName))
            return;

        FinishImport(
            success: false,
            $"Optional font resource import was cancelled: {packageName}");
    }

    private static bool IsFontPackageEvent(string packageName)
    {
        return fontImportInProgress && IsFontPackageName(packageName);
    }

    internal static bool IsFontPackageName(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return false;

        var normalizedPath = packageName.Trim().Replace('\\', '/').TrimEnd('/');
        var normalizedName = Path.GetFileName(normalizedPath);

        if (normalizedName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = normalizedName.Substring(
                0,
                normalizedName.Length - ".unitypackage".Length);
        }

        return string.Equals(
            normalizedName,
            FontPackageName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void FinishImport(bool success, string message)
    {
        if (!fontImportInProgress)
            return;

        fontImportInProgress = false;
        UnsubscribeImportCallbacks();

        if (!success)
        {
            Debug.LogError(message);
            return;
        }

        if (!TryLoadManifest(out var manifest, out var manifestError))
        {
            Debug.LogError(
                "Optional font resource import completed, but its manifest could not " +
                $"be validated.\n{manifestError}");
            return;
        }

        var preflight = RunPreflight(manifest);
        if (preflight.Status != PreflightStatus.AlreadyInstalled)
        {
            Debug.LogError(
                "Optional font resource import completed, but the imported paths and " +
                $"GUIDs did not pass verification.\n{preflight.Message}");
            return;
        }

        Debug.Log("AIDrivenFW optional font resources were imported successfully.");
    }
}

[InitializeOnLoad]
internal static class AIDrivenPackageImportTracker
{
    // A domain reload resets this count; Unity's compiling/updating flags keep the
    // repair waiter blocked until an import that spans that reload becomes idle.
    private static int otherPackageImportCount;

    internal static bool IsOtherPackageImportInProgress => otherPackageImportCount > 0;

    static AIDrivenPackageImportTracker()
    {
        // Event-only initialization is safe before the asset database finishes loading.
        // Subtract first so this remains duplicate-free within the current domain.
        AssetDatabase.importPackageStarted -= OnImportPackageStarted;
        AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
        AssetDatabase.importPackageFailed -= OnImportPackageFailed;
        AssetDatabase.importPackageCancelled -= OnImportPackageCancelled;

        AssetDatabase.importPackageStarted += OnImportPackageStarted;
        AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
        AssetDatabase.importPackageFailed += OnImportPackageFailed;
        AssetDatabase.importPackageCancelled += OnImportPackageCancelled;
    }

    private static void OnImportPackageStarted(string packageName)
    {
        if (!AIDrivenOptionalFontResources.IsFontPackageName(packageName))
            otherPackageImportCount++;
    }

    private static void OnImportPackageCompleted(string packageName)
    {
        FinishOtherPackageImport(packageName);
    }

    private static void OnImportPackageFailed(string packageName, string errorMessage)
    {
        FinishOtherPackageImport(packageName);
    }

    private static void OnImportPackageCancelled(string packageName)
    {
        FinishOtherPackageImport(packageName);
    }

    private static void FinishOtherPackageImport(string packageName)
    {
        if (AIDrivenOptionalFontResources.IsFontPackageName(packageName))
            return;

        // The repair waiter runs on the next Editor update, after every completion
        // subscriber (including the Optional Packages queue) has returned.
        if (otherPackageImportCount > 0)
            otherPackageImportCount--;
    }
}

internal sealed class AIDrivenOptionalFontResourcesPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths,
        bool didDomainReload)
    {
        AIDrivenOptionalFontResources.OnAssetsPostprocessed(importedAssets, didDomainReload);
    }
}
