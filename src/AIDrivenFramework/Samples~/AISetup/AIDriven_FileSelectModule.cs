using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

public class AIDriven_FileSelectModule : MonoBehaviour
{
    [SerializeField] CanvasGroup fileSelectGroup;
    [SerializeField] Transform contentTransform;
    [SerializeField] GameObject fileRowPrefab;
    [SerializeField] TMP_InputField pathInputField;
    List<GameObject> fileRows = new List<GameObject>();
    AIDriven_AIDrivenManager aIDrivenManager;
    AIDriven_FileDownloadWatcher fileDownloadWatcher;

    public string selectedFilePath { get; private set; } = "";

    private void Awake()
    {
        aIDrivenManager = GetComponent<AIDriven_AIDrivenManager>();
        fileDownloadWatcher = GetComponent<AIDriven_FileDownloadWatcher>();
        fileSelectGroup.alpha = 0f;
        fileSelectGroup.blocksRaycasts = false;
    }

    public void ResetState()
    {
        selectedFilePath = "";
    }

    public void OpenFileDialog()
    {
        if(aIDrivenManager.isOllama)
        {
            fileDownloadWatcher.FileName = "ollama";
            fileDownloadWatcher.isFileSelected = true;
            return;
        }
        selectedFilePath = "";
        fileSelectGroup.alpha = 1f;
        fileSelectGroup.blocksRaycasts = true;
        InitFileSelecter(LoadLastPath());
    }

    public void SelectedFile(string filePath)
    {
        CloseFileDialog();
        Debug.Log($"Selected Path:  filePath");
        fileDownloadWatcher.FileName = filePath;
        fileDownloadWatcher.isFileSelected = true;
    }

    public void InitFileSelecter(string path)
    {
        Debug.Log($"Initializing file selector with path: {path}");
        pathInputField.text = path;
        for (int i = fileRows.Count - 1; i >= 0; i--)
        {
            Destroy(fileRows[i]);
        }
        fileRows.Clear();
        var files = Directory.GetFiles(path)
            .Where(file => fileDownloadWatcher.targetExtension.Match(Path.GetFileName(file)))
            .OrderByDescending(file => File.GetLastWriteTime(file));

        foreach (var file in files)
        {
            var row = Instantiate(fileRowPrefab, contentTransform);
            row.GetComponent<AIDriven_FileRowInstance>().Init(file);
            fileRows.Add(row);
        }
        PlayerPrefs.SetString("FileBrowser_LastPath", path);
        PlayerPrefs.Save();
    }

    public void CloseFileDialog()
    {
        selectedFilePath = "";
        fileSelectGroup.alpha = 0f;
        fileSelectGroup.blocksRaycasts = false;
    }

    public string LoadLastPath()
    {
        if (PlayerPrefs.HasKey("FileBrowser_LastPath"))
        {
            string path = PlayerPrefs.GetString("FileBrowser_LastPath");

            if (Directory.Exists(path))
                return path;
        }

        return AIDriven_FileDownloadWatcher.GetDownloadFolderPath();
    }
}
