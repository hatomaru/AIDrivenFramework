using System.IO;
using TMPro;
using UnityEngine;

public class AIDriven_FileRowInstance : MonoBehaviour
{
    string filePath;
    AIDriven_FileSelectModule fileSelectModule;
    [SerializeField] private TextMeshProUGUI fileNameText;
    [SerializeField] private TextMeshProUGUI fileSizeText;
    [SerializeField] private TextMeshProUGUI fileUpdateDateText;

    private void Awake()
    {
        fileSelectModule = GameObject.Find("Manager").GetComponent<AIDriven_FileSelectModule>();
    }

    public void Init(string path)
    {
        var info = new FileInfo(path);
        filePath = path;
        fileNameText.text = Path.GetFileName(path);
        if (info.Length / 1024 / 1024 >= 1000)
        {
            fileSizeText.text = $"{info.Length / 1024 / 1024 / 1024f:F1} GB";
        }
        else
        {
            fileSizeText.text = $"{info.Length / 1024 / 1024} MB";
        }
        fileUpdateDateText.text = info.LastWriteTime.ToString("yyyy.MM.dd");
    }

    public void OnClickSelect()
    {
        fileSelectModule.SelectedFile(filePath);
    }
}
