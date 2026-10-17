using TMPro;
using UnityEngine;

public class AIDriven_CofirmUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI promptText;
    [SerializeField] TextMeshProUGUI fileNameText;
    [SerializeField] AIDriven_LocalizationManager localizationManager;

    public void Init(string filePath)
    {
        if (filePath.Contains("\\"))
        {
            var segments = filePath.Split('\\');
            filePath = segments[segments.Length - 1];
        }
        if(filePath.Contains(".zip"))
        {
            promptText.text = localizationManager.Get("MODAL_ExtractFile");
        }
        else
        {
            promptText.text = localizationManager.Get("MODAL_UseFile");
        }
        fileNameText.text = $"{localizationManager.Get("MODAL_File")}:{filePath}";
    }
}
