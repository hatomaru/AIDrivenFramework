using Cysharp.Threading.Tasks;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AIDrivenFW;
using System.Threading;

/// <summary>
/// AIDriven Framework Example Text Interaction
/// </summary>
public class AIDriven_ExampleText : MonoBehaviour
{
    StringBuilder outputLog = new StringBuilder();
    [SerializeField] GameObject canvas;
    [SerializeField] Button sendButton;
    [SerializeField] TextMeshProUGUI sendButtonText;
    [SerializeField] TMP_InputField inputField;
    [SerializeField] TextMeshProUGUI outputLogText;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        outputLogText.text = outputLog.ToString();
        sendButton.interactable = false;
    }

    public void OnChangeInput()
    {
        if(sendButtonText.text != "Send")
        {
            return;
        }
        sendButton.interactable = !string.IsNullOrEmpty(inputField.text);
    }

    public async void OnClickSend()
    {
        sendButtonText.text = "Wait..";
        sendButton.interactable = false;
        outputLog.AppendLine($"USER > {inputField.text}");
        outputLogText.text = outputLog.ToString();
        string result = await Generate(destroyCancellationToken);
        outputLog.AppendLine($"AI > {result}");
        outputLogText.text = outputLog.ToString();
        inputField.text = string.Empty;
        sendButton.interactable = !string.IsNullOrEmpty(inputField.text);
        sendButtonText.text = "Send";
    }

    public async UniTask<string> Generate(CancellationToken token)
    {
        return await GenAI.Generate(inputField.text, ct: token);
    }
}
