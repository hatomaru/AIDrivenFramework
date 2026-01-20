using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class TMPAutoLinkify : MonoBehaviour
{
    TMP_Text tmpText;

    // example.com / www.xxx / https://xxx 対応
    private static readonly Regex urlRegex =
        new Regex(
            @"(https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9\-]+\.[a-zA-Z]{2,})",
            RegexOptions.Compiled
        );

    void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
        ConvertUrlsToLinks();
    }

    public void ConvertUrlsToLinks()
    {
        string original = tmpText.text;

        string converted = urlRegex.Replace(original, match =>
        {
            string rawUrl = match.Value;
            string openUrl = rawUrl.StartsWith("http")
                ? rawUrl
                : "https://" + rawUrl;

            return
                $"<link=\"{openUrl}\">" +
                $"<u>{rawUrl}</u>" +
                $"</link>";
        });

        tmpText.text = converted;
    }
}
