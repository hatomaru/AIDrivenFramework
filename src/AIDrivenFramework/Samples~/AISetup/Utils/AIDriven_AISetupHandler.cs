using AIDrivenFW.API;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Compatibility entry point for games using the previous optional helper.</summary>
public class AIDriven_AISetupHandler : MonoBehaviour
{
    public static UnityAction<bool> onPreparationFinished;
    public static async UniTask Initialize(GenAI defaultGenAI = null, GenAIConfig prepareGenAIConfig = null)
    {
        bool prepared = prepareGenAIConfig != null && await FileManager.IsPrepared(default, defaultGenAI, prepareGenAIConfig);
        if (!prepared) prepared = await AIDrivenInitializer.Initialize(default, defaultGenAI);
        onPreparationFinished?.Invoke(prepared);
    }
}
