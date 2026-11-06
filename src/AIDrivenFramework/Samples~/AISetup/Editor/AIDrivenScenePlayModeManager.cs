using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AIDrivenSetupシーンを開いてPlayModeに遷移し、終了後に元のシーンに戻すエディタ拡張
/// </summary>
public class AIDrivenScenePlayModeManager
{
    private const string AISETUP_SCENE_PATH = "Assets/AIDrivenFW/AISetup/AIDrivenSetup.unity";

    private static string previousScenePath = "";
    private static bool shouldReturnToPreviousScene = false;

    [MenuItem("Tools/AIDrivenFW/AI Setup Wizard")]
    public static void PlayAIDrivenSetupScene()
    {
        // 現在のシーンが未保存の場合は保存を促す
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            bool save = EditorUtility.DisplayDialog(
                "Unsaved Changes",
                "Current scene has unsaved changes. Do you want to save before switching?",
                "Save",
                "Don't Save"
            );

            if (save)
            {
                EditorSceneManager.SaveOpenScenes();
            }
        }
        // 現在のシーンパスを保存
        Scene currentScene = EditorSceneManager.GetActiveScene();
        previousScenePath = currentScene.path;

        // シーンパスが空でない場合のみ戻る処理を有効にする
        if (!string.IsNullOrEmpty(previousScenePath))
        {
            shouldReturnToPreviousScene = true;
        }

        // PlayMode状態変更のコールバックを登録
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // AIDrivenSetupシーンを開く
        EditorSceneManager.OpenScene(AISETUP_SCENE_PATH);

        // PlayModeに遷移
        EditorApplication.isPlaying = true;

        Debug.Log($"Opening AIDrivenSetup scene and entering PlayMode. Will return to: {previousScenePath}");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // PlayModeから抜けた時の処理
        if (state == PlayModeStateChange.EnteredEditMode && shouldReturnToPreviousScene)
        {
            // 元のシーンに戻る
            if (!string.IsNullOrEmpty(previousScenePath))
            {
                EditorSceneManager.OpenScene(previousScenePath);
                Debug.Log($"Returned to previous scene: {previousScenePath}");
            }

            // 状態をリセット
            ClearSceneReturnState();
        }
    }

    /// <summary>
    /// 状態をリセットするメソッド
    /// </summary>
    public static void ClearSceneReturnState()
    {
        try
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
        catch
        {
            // コールバックが登録されていない場合は無視
        }
        shouldReturnToPreviousScene = false;
        previousScenePath = "";
        Debug.Log("Scene return state cleared.");
    }
}
