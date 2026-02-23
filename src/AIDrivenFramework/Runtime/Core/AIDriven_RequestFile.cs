using AIFW.Config;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class AIDriven_RequestFile : MonoBehaviour
{
    private List<string> files = new List<string>();

    /// <summary>
    /// フォルダを再読み込みして、現在のファイルリストを更新します。
    /// </summary>
    public void Reload()
    {
        files.Clear();
        string path = Path.Combine(Application.persistentDataPath,AIDrivenConfig.baseFilePath);
        if (!Directory.Exists(path)) return;

        // Get files in directory and all subdirectories
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            string f = file;
            files.Add(f);
        }
    }

    /// <summary>
    /// 拡張子・ファイル名でファイルが存在するか確認しファイル名を返す
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <returns>ファイルが存在するか</returns>
    public string Contains(string fileName)
    {
        foreach (var file in files)
        {
            if (file.Contains(fileName))
            {
                return file;
            }
        }
        return "null";
    }
}
