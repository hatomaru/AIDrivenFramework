using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class AIDriven_FileDownloadWatcher : MonoBehaviour
{
    public bool isFileSelected = false;
    public string FileName = "";
    public AIDrivenFW_FileFilter targetExtension;
    public string watchFolderPath;
    private HashSet<string> knownFiles = new HashSet<string>();

    // --- new configuration fields ---
    [Tooltip("Required minimum file size (bytes) to consider download complete")]
    public long minCompleteSizeBytes = 50 * 1024 * 1024; // default 50 MB

    [Tooltip("Number of seconds the file size must remain stable after reaching the minimum size")]
    public float stableSeconds = 2f;

    [Tooltip("Polling interval in seconds when checking file size")]
    public float checkIntervalSeconds = 1f;

    void Start()
    {
        watchFolderPath = GetDownloadFolderPath();

        InvokeRepeating(nameof(CheckFolder), 1f, 1f); // 1秒ごと
    }

    public void Init(AIDrivenFW_FileFilter fileName)
    {
        FileName = "";
        targetExtension = fileName;
        isFileSelected = false;
        knownFiles.Clear();
        foreach (var file in Directory.GetFiles(watchFolderPath))
        {
            knownFiles.Add(file);
        }
    }

    void CheckFolder()
    {
        var files = Directory.GetFiles(watchFolderPath);

        foreach (var file in files)
        {
            if (!knownFiles.Contains(file))
            {
                knownFiles.Add(file);
                // fire-and-forget the async watcher
                _ = OnNewFileDetected(file);
            }
        }
    }

    async UniTask OnNewFileDetected(string filePath)
    {
        // 拡張子チェック (.gguf など)
        var ext = Path.GetExtension(filePath);
        if(targetExtension == null)
        {
            return;
        }
        if (!targetExtension.Match(filePath))
        {
            Debug.Log($"Skip because the file extension does not match: {filePath}");
            return;
        }

        // 初期サイズ取得
        var fi = new FileInfo(filePath);
        long lastSize = 0;
        try
        {
            lastSize = fi.Exists ? fi.Length : 0;
        }
        catch (Exception ex)
        {
            Debug.Log($"Cannot access the file: {ex.Message}");
        }

        float stableAccum = 0f;

        // サイズが一定以上になったら「DL完了」とみなす
        while (true)
        {
            try
            {
                fi.Refresh();
                long size = fi.Exists ? fi.Length : 0;

                if (size >= minCompleteSizeBytes)
                {
                    if (size == lastSize)
                    {
                        stableAccum += checkIntervalSeconds;
                    }
                    else
                    {
                        stableAccum = 0f;
                        lastSize = size;
                    }

                    if (stableAccum >= stableSeconds)
                    {
                        Debug.Log($"Download complete: {filePath} (Size {size} bytes)");
                        OnDownloadComplete(filePath);
                        return;
                    }
                }
                else
                {
                    // サイズ未達の場合リセットして待機
                    lastSize = size;
                    stableAccum = 0f;
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"File monitoring in progress Error: {ex.Message}");
            }

            await UniTask.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), cancellationToken: destroyCancellationToken);
        }
    }

    void OnDownloadComplete(string filePath)
    {
        // 例: イベント発火、キューへ追加、モデル読み込みなど
        if (!targetExtension.Match(filePath))
        {
            return;
        }
        // ダウンロード完了時の処理をここに追加
        Debug.Log($"Processing Start: Processing completed download files -> {filePath}");
        OnComplete(filePath);
    }

    void OnComplete(string filePath)
    {
        FileName = filePath;
        isFileSelected = true;
    }

    /// <summary>
    /// ダウンロードフォルダのパスを取得する
    /// </summary>
    /// <returns>ダウンロードフォルダのパス</returns>
    public static string GetDownloadFolderPath()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"
        );

#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Downloads"
        );

#else
        return Path.Combine(Application.dataPath, "Downloads");
#endif
    }
}
