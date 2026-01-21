using AIDrivenFW;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AIDriven_SmallCode : MonoBehaviour
{
    void Start()
    {
        TestCode().Forget();
    }

    async UniTask TestCode()
    {
        string response = await GenAI.Generate(
            "Hello",
            ct: destroyCancellationToken
        );

        Debug.Log("Response: " + response);
    }
}