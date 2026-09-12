using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIDrivenFW.Config;
using UnityEditor;
using UnityEngine;

internal static class AIDriven_LlamaCppModelCleanup
{
    private const string MenuPath = "Tools/AIDrivenFW/Clean Up llama.cpp Models";

    [MenuItem(MenuPath)]
    private static void CleanUpModels()
    {
        string modelRoot = GetModelRootPath();
        List<string> modelPaths = GetModelPaths(modelRoot);
        if (modelPaths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "No llama.cpp Models Found",
                $"No GGUF models set up by AIDrivenFW were found.\n\nModel folder:\n{modelRoot}",
                "OK");
            return;
        }

        string targetPaths = string.Join("\n", modelPaths);
        string message =
            "The following llama.cpp model files set up by the game will be permanently deleted.\n\n" +
            targetPaths +
            "\n\nOllama-managed models and the llama.cpp runtime will not be deleted.";

        if (!EditorUtility.DisplayDialog("Clean Up llama.cpp Models", message, "Delete Models", "Cancel"))
        {
            return;
        }

        int deletedCount = 0;
        try
        {
            foreach (string modelPath in modelPaths)
            {
                File.Delete(modelPath);
                deletedCount++;
            }

            RemoveEmptyDirectories(modelRoot);
            Debug.Log($"[AIDrivenFW] Deleted {deletedCount} llama.cpp model file(s) from {modelRoot}.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[AIDrivenFW] Could not clean up llama.cpp models: {exception.Message}");
            EditorUtility.DisplayDialog(
                "Model Cleanup Failed",
                $"Some model files could not be deleted.\n\nModel folder:\n{modelRoot}\n\n{exception.Message}",
                "OK");
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateCleanUpModels()
    {
        return GetModelPaths(GetModelRootPath()).Count > 0;
    }

    private static string GetModelRootPath()
    {
        string root = Path.Combine(
            Application.persistentDataPath,
            AIDrivenConfig.Instance.BaseFilePath,
            AIDrivenConfig.Instance.ModelSubPath);
        return Path.GetFullPath(root);
    }

    private static List<string> GetModelPaths(string modelRoot)
    {
        if (!Directory.Exists(modelRoot))
        {
            return new List<string>();
        }

        if (IsReparsePoint(modelRoot))
        {
            return new List<string>();
        }

        string normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(modelRoot));
        var modelPaths = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(modelRoot);

        while (pendingDirectories.Count > 0)
        {
            string directory = pendingDirectories.Pop();
            foreach (string file in Directory.EnumerateFiles(directory, "*.gguf", SearchOption.TopDirectoryOnly))
            {
                string fullPath = Path.GetFullPath(file);
                if (!IsReparsePoint(file) && fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    modelPaths.Add(fullPath);
                }
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsReparsePoint(childDirectory))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }
        }

        return modelPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void RemoveEmptyDirectories(string modelRoot)
    {
        if (!Directory.Exists(modelRoot))
        {
            return;
        }

        foreach (string directory in Directory.GetDirectories(modelRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !IsReparsePoint(path))
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(modelRoot).Any())
        {
            Directory.Delete(modelRoot);
        }
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
