using AIDrivenFW;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AISetupHandler : MonoBehaviour
{
    void Awake()
    {
        bool isPrepare = FileManager.IsPrepared();
        UnityEngine.Debug.Log("Preparation Result: " + isPrepare);
        isPrepare = true;
        if (!isPrepare)
        {
            UnityEngine.Debug.LogError("GoTo Setup");
            SceneManager.LoadSceneAsync("AIDrivenSetup", LoadSceneMode.Additive);
        }
    }
}
