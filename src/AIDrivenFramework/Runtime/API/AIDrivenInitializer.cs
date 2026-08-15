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
        /// <param name="ct">初期化処理を中止するキャンセルトークン。</param>
        /// <param name="defaultGenAI">準備済のGenAIクラス (オプション)</param>
        /// <returns>セットアップが完了したか</returns>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/>がキャンセルされた場合。</exception>
        public async static UniTask<bool> Initialize(CancellationToken ct = default,GenAI defaultGenAI = null)
        {
            ct.ThrowIfCancellationRequested();
            bool isPrepare = await FileManager.IsPrepared(ct,defaultGenAI);
            ct.ThrowIfCancellationRequested();
            UnityEngine.Debug.Log("Preparation Result: " + isPrepare);

            if (!isPrepare)
            {
                if (!Application.CanStreamedLevelBeLoaded(setupSceneName))
                {
                    UnityEngine.Debug.LogError("AIDrivenSetup scene is not added to the build settings. Please add it to the build settings to proceed with the setup.");
                    return isPrepare;
                }
                UnityEngine.Debug.Log("Loading AIDrivenSetup scene for preparation...");
                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(setupSceneName, LoadSceneMode.Additive);
                try
                {
                    await loadOperation.WithCancellation(ct);
                    // Then wait until the additive setup scene is unloaded
                    await UniTask.WaitUntil(() => !SceneManager.GetSceneByName(setupSceneName).isLoaded, cancellationToken: ct);
                    isPrepare = true;
                }
                catch (OperationCanceledException)
                {
                    CleanupCancelledSetupLoadAsync(loadOperation).Forget(ex =>
                        UnityEngine.Debug.LogError($"Failed to clean up the cancelled AIDrivenSetup scene load: {ex.Message}"));
                    throw;
                }
            }
            ct.ThrowIfCancellationRequested();
            if (onPreparationFinished != null)
            {
                // Invoke subscribers safely: remove or skip subscribers whose target UnityEngine.Object has been destroyed
                var invocationList = onPreparationFinished.GetInvocationList();
                foreach (var d in invocationList)
                {
                    ct.ThrowIfCancellationRequested();
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
            ct.ThrowIfCancellationRequested();
            return isPrepare;
        }

        private static async UniTask CleanupCancelledSetupLoadAsync(AsyncOperation loadOperation)
        {
            if (loadOperation != null && !loadOperation.isDone)
            {
                await loadOperation;
            }

            Scene setupScene = SceneManager.GetSceneByName(setupSceneName);
            if (!setupScene.isLoaded)
            {
                return;
            }

            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(setupScene);
            if (unloadOperation != null)
            {
                await unloadOperation;
            }
        }
    }
}
