using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AIDrivenFW.API
{
    public static class AIDrivenInitializer
    {
        // 準備完了時に呼び出すイベント
        public static UnityAction<bool> onPreparationFinished;
        const string setupSceneName = "AIDrivenSetup";

        /// <summary>
        /// ローカルLLMの準備が行えているのかを確認し、必要に応じてAIDrivenSetupシーンをロードして準備を行う。
        /// </summary>
        /// <returns>セットアップが完了したか</returns>
        public async static UniTask<bool> Initialize(CancellationToken ct = default)
        {
            bool isPrepare = await FileManager.IsPrepared(ct);
            UnityEngine.Debug.Log("Preparation Result: " + isPrepare);
            if (!isPrepare)
            {
                if (!Application.CanStreamedLevelBeLoaded(setupSceneName))
                {
                    UnityEngine.Debug.LogError("AIDrivenSetup scene is not added to the build settings. Please add it to the build settings to proceed with the setup.");
                    return isPrepare;
                }
                UnityEngine.Debug.Log("Loading AIDrivenSetup scene for preparation...");
                await SceneManager.LoadSceneAsync(setupSceneName, LoadSceneMode.Additive);
                // Then wait until the additive setup scene is unloaded
                await UniTask.WaitUntil(() => !SceneManager.GetSceneByName(setupSceneName).isLoaded, cancellationToken: ct);
                isPrepare = true;
            }
            if (onPreparationFinished != null)
            {
                // Invoke subscribers safely: remove or skip subscribers whose target UnityEngine.Object has been destroyed
                var invocationList = onPreparationFinished.GetInvocationList();
                foreach (var d in invocationList)
                {
                    var action = d as UnityAction<bool>;
                    // If the delegate target is a UnityEngine.Object and has been destroyed, unsubscribe and skip
                    if (d.Target is UnityEngine.Object unityObj && unityObj == null)
                    {
                        try { onPreparationFinished -= action; } catch { }
                        continue;
                    }
                    try
                    {
                        action?.Invoke(isPrepare);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"Error invoking onPreparationFinished subscriber: {ex.Message}");
                    }
                }
            }
            return isPrepare;
        }
    }
}
