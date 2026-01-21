using UnityEngine;

using SFB;

public class FileSelectModule : MonoBehaviour
{
    AIDrivenManager aIDrivenManager;
    FileDownloadWatcher fileDownloadWatcher;

    public string selectedFilePath { get; private set; } = "";

    private void Awake()
    {
        aIDrivenManager = GetComponent<AIDrivenManager>();
        fileDownloadWatcher = GetComponent<FileDownloadWatcher>();
    }

    public void ResetState()
    {
        selectedFilePath = "";
    }

    public void OpenFileDialog()
    {
        selectedFilePath = "";
        var paths = StandaloneFileBrowser.OpenFilePanel(
            "ファイルを選択",
            Application.dataPath,
            fileDownloadWatcher.targetExtension.Substring(1),
            false
        );

        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            Debug.Log("選択されたパス: " + paths[0]);
            fileDownloadWatcher.FileName = paths[0];
            fileDownloadWatcher.isTrriggered = true;
        }
    }
}
