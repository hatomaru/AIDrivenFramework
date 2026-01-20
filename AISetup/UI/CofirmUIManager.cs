using TMPro;
using UnityEngine;

public class CofirmUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI promptText;
    [SerializeField] TextMeshProUGUI fileNameText;

    public void Init(string filePath)
    {
        if (filePath.Contains("\\"))
        {
            var segments = filePath.Split('\\');
            filePath = segments[segments.Length - 1];
        }
        if(filePath.Contains(".zip"))
        {
            promptText.text = "このファイルを解凍して使用しますか？";
        }
        else
        {
            promptText.text = "このファイルを使用しますか？";
        }
        fileNameText.text = $"ファイル名:{filePath}";
    }
}
