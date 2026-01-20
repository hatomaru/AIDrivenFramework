using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class FileDownloadWatcher : MonoBehaviour
{
    public bool isTrriggered = false;
    public string FileName = "";
    public string targetExtension = ".gguf";
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

    public void Init(string fileName)
    {
        FileName = "";
        targetExtension = fileName;
        isTrriggered = false;
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
        Debug.Log($"新しいファイル検出: {filePath}");

        // 拡張子チェック (.gguf など)
        var ext = Path.GetExtension(filePath);
        if (!string.Equals(ext, targetExtension, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"拡張子が一致しないため監視を中止: {filePath}");
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
            Debug.Log($"ファイルにアクセスできません: {ex.Message}");
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
                        Debug.Log($"DL完了: {filePath} (サイズ {size} bytes)");
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
                Debug.Log($"ファイル監視中にエラー: {ex.Message}");
            }

            await UniTask.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), cancellationToken: destroyCancellationToken);
        }
    }

    void OnDownloadComplete(string filePath)
    {
        // ダウンロード完了時の処理をここに追加
        Debug.Log($"処理開始: ダウンロード完了ファイルを処理します -> {filePath}");
        // 例: イベント発火、キューへ追加、モデル読み込みなど
        if(filePath.Contains(targetExtension))
        {
            OnComplete(filePath);
        }
    }

    void OnComplete(string filePath)
    {
        FileName = filePath;
        isTrriggered = true;
    }

    /// <summary>
    /// ダウンロードフォルダのパスを取得する
    /// </summary>
    /// <returns>ダウンロードフォルダのパス</returns>
    public static string GetDownloadFolderPath()
    {
#if UNITY_STANDALONE_WIN
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"
        );

#elif UNITY_STANDALONE_OSX
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Downloads"
        );

#else
        return string.Empty;
#endif
    }
}
