using AIDrivenFW.Config;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class AIDriven_RequestFile
{
    private List<string> files = new List<string>();

    /// <summary>
    /// フォルダを再読み込みして、現在のファイルリストを更新します。
    /// </summary>
    public void Reload()
    {
        files.Clear();
        // Search both persistent data path and project data folder so files placed by the AISetup
        // (which stores into Application.dataPath) are also discovered by auto-detection.
        var searchPaths = new List<string>();
        searchPaths.Add(Path.Combine(Application.persistentDataPath, AIDrivenConfig.Instance.BaseFilePath));
        searchPaths.Add(Path.Combine(Application.dataPath, AIDrivenConfig.Instance.BaseFilePath));

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            foreach (var file in Directory.GetFiles(basePath, "*", SearchOption.AllDirectories))
            {
                files.Add(file);
            }
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
