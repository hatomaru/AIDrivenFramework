using TMPro;
using UnityEngine;

public class FileGetUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI guideText;
    [SerializeField] TextMeshProUGUI linkText;
    [SerializeField] TMPAutoLinkify tMPAutoLinkify;

    public void Init(string fileGenre,string link)
    {
        guideText.text = $"{fileGenre}は下記リンクから各自でダウンロードしてください。\r\nここでは、手順の案内のみを行います。";
        linkText.text = link;
        tMPAutoLinkify.ConvertUrlsToLinks();
    }
}
