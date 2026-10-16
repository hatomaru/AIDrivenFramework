using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// AISetupのローカライズデータを格納するScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "LocalizationData", menuName = "AIDrivenFW/LocalizationData")]
public class AIDriven_LocalizationData : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string key;
        public string japanese;
        public string english;
    }

    public List<Entry> entries = new List<Entry>();
}
