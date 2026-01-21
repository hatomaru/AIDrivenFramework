using UnityEngine;

public static class QuitHookBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        var go = new GameObject("AIProcessManager");
        go.hideFlags = HideFlags.HideAndDontSave;

        Object.DontDestroyOnLoad(go);
        go.AddComponent<QuitHookBehaviour>();
        go.AddComponent<AISetupHandler>();
    }
}
