using AIDrivenFW;
using Cysharp.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AIDrivenManager : MonoBehaviour
{
    enum AIDrivenState
    {
        Prepare,
        Setup_Overview,
        Setup_SoftwareDownload,
        Setup_ModelDownload,
        Setup_LicenceConfirm,
        Setup_Complete,
    }

    AIDrivenState state = AIDrivenState.Prepare;
    bool isAcsepted = false;
    RequestFile requestFile;
    FileDownloadWatcher fileDownloadWatcher;
    GenAI ai = null;    // テスト用AIプロセス
    [SerializeField] TextMeshProUGUI labelText;
    [SerializeField] LoadingModule loadingModule;
    [SerializeField] PopupManager fileUsePopupManager;
    [SerializeField] CofirmUIManager cofirmUIManager;
    [SerializeField] FileGetUIManager fileGetUIManager;
    [SerializeField] NavigationBarUIManager navigationBarUIManager;
    [SerializeField] CardUIManager[] cardUIs;
    [SerializeField] FadeModule fadeModule;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        navigationBarUIManager.onBoardUI.SetActive(true);
        navigationBarUIManager.stepNavigationUI.SetActive(false);
        labelText.text = $"AIセットアップ <size=28>{Application.productName}";
        fileDownloadWatcher = GetComponent<FileDownloadWatcher>();
        requestFile = GetComponent<RequestFile>();
        state = AIDrivenState.Setup_Overview;
        MainLoop(destroyCancellationToken).Forget();
    }

    // Update is called once per frame
    void Update()
    {

    }

    async UniTask ShowCard(CancellationToken token)
    {
        switch (state)
        {
            case AIDrivenState.Setup_Overview:
                await cardUIs[0].Show();
                break;
            case AIDrivenState.Setup_SoftwareDownload:
                await cardUIs[1].Show();
                break;
            case AIDrivenState.Setup_ModelDownload:
                await cardUIs[1].Show();
                break;
            case AIDrivenState.Setup_LicenceConfirm:
                await cardUIs[2].Show();
                break;
        }
    }

    async UniTask HideCard(CancellationToken token)
    {
        switch (state)
        {
            case AIDrivenState.Setup_Overview:
                await cardUIs[0].Hide();
                break;
            case AIDrivenState.Setup_SoftwareDownload:
                await cardUIs[1].Hide();
                break;
            case AIDrivenState.Setup_ModelDownload:
                await cardUIs[1].Hide();
                break;
            case AIDrivenState.Setup_LicenceConfirm:
                await cardUIs[2].Hide();
                break;
        }
    }

    public async void OnBack()
    {
        await HideCard(destroyCancellationToken);
        state--;
        isAcsepted = true;
    }

    public async void OnEnter()
    {
        await HideCard(destroyCancellationToken);
        state++;
        isAcsepted = true;
    }

    /// <summary>
    /// メインループ
    /// </summary>
    async UniTask MainLoop(CancellationToken token)
    {
        while (true)
        {
            isAcsepted = false;
            // 描画する
            switch (state)
            {
                case AIDrivenState.Setup_Overview:
                    navigationBarUIManager.onBoardUI.SetActive(true);
                    navigationBarUIManager.stepNavigationUI.SetActive(false);
                    break;
                case AIDrivenState.Setup_SoftwareDownload:
                    navigationBarUIManager.onBoardUI.SetActive(false);
                    navigationBarUIManager.stepNavigationUI.SetActive(true);
                    navigationBarUIManager.ActivateStep(0);
                    fileGetUIManager.Init("AIソフト", AIDrivenConfig.softwareLink);
                    break;
                case AIDrivenState.Setup_ModelDownload:
                    navigationBarUIManager.ActivateStep(1);
                    fileGetUIManager.Init("モデル", AIDrivenConfig.modelink);
                    break;
                case AIDrivenState.Setup_LicenceConfirm:
                    navigationBarUIManager.ActivateStep(2);
                    break;
                case AIDrivenState.Setup_Complete:

                    navigationBarUIManager.onBoardUI.SetActive(false);
                    navigationBarUIManager.stepNavigationUI.SetActive(false);
                    await loadingModule.OnLoad("AIを準備中...");
                    //AIProcessManager.Prepare();
                    string response = await GenAI.Generate("こんにちは", ct: token);
                    Debug.Log("Response " + response);
                    if (string.IsNullOrEmpty(response))
                    {

                    }
                    Debug.Log("Response " + response);
                    loadingModule.OnComplete();
                    fadeModule.OnComplete();
                    await UniTask.Delay(200, cancellationToken: token);
                    await SceneManager.UnloadSceneAsync("AIDrivenSetup");
                    return;
            }
            await ShowCard(token);
            // 状態に応じた処理
            switch (state)
            {
                case AIDrivenState.Setup_SoftwareDownload:
                    await OnGetFile(destroyCancellationToken, ".zip");
                    isAcsepted = true;
                    break;
                case AIDrivenState.Setup_ModelDownload:
                    await OnGetFile(destroyCancellationToken, ".gguf");
                    isAcsepted = true;
                    break;
                default:
                    await UniTask.WaitUntil(() => isAcsepted, cancellationToken: token);
                    break;
            }
            await UniTask.WaitUntil(() => isAcsepted, cancellationToken: token);
            await UniTask.DelayFrame(1, cancellationToken: token);
        }
    }

    /// <summary>
    /// ファイルが正しいか確認
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="extension">対象拡張子</param>
    /// <returns>ファイルが正しいのか</returns>
    bool isSuccessedFile(string path, string extension)
    {
        // ファイルの確認処理
        if (!path.EndsWith(extension))
        {
            Debug.LogError("不正なファイル形式です。");
            return false;
        }
        return true;
    }

    /// <summary>
    /// ファイル取得要求時
    /// </summary>
    public async UniTask OnGetFile(CancellationToken token, string extension)
    {
        while (true)
        {
            fileDownloadWatcher.Init(extension);
            await UniTask.WaitUntil(() => fileDownloadWatcher.isTrriggered || isAcsepted, cancellationToken: token);
            if (isAcsepted)
            {
                return;
            }
            string path = fileDownloadWatcher.FileName;
            Debug.Log($"ファイル取得完了: {path}");
            loadingModule.OnLoad("ファイルを確認中...").Forget();
            bool isValid = isSuccessedFile(path, extension);
            if (!isValid)
            {
                loadingModule.OnComplete();
                continue;
            }
            cofirmUIManager.Init(path);
            // 確認ポップアップ表示
            fileUsePopupManager.Popup();
            //ファイルの処理_キャッシュ化(永続フォルダにAIDrivenFFramework)
            await UniTask.WaitUntil(() => !fileUsePopupManager.isPopuped, cancellationToken: token);
            if (!fileUsePopupManager.isAccsept)
            {
                loadingModule.OnComplete();
                continue;
            }
            await UniTask.Delay(500, cancellationToken: token); // 少し待機してから処理開始
            await OnFinalize(token, path);
            loadingModule.OnComplete();
            OnEnter();
            return;
        }
    }

    /// <summary>
    /// 取得したファイルを処理する
    /// </summary>
    /// <param name="token"></param>
    /// <param name="filePath"></param>
    public async UniTask OnFinalize(CancellationToken token, string filePath)
    {
        string fromPath = filePath;
        string toPath = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
        string tempExtractPath = Path.Combine(Application.persistentDataPath, AIDrivenConfig.tempFilePath);

        // ファイルごとに先を更新
        if (fromPath.Contains(".gguf"))
        {
            toPath = Path.Combine(toPath, AIDrivenConfig.modelSubPath);
        }

        loadingModule.OnLoad("ファイルを処理中...").Forget();
        // モデル解凍を想定したクリーンアップ
        if (Directory.Exists(toPath))
        {
            Directory.Delete(toPath, true);
        }
        // Zipを一時フォルダに展開
        if (fromPath.Contains(".zip"))
        {
            loadingModule.OnLoad("ファイルを展開中...").Forget();
            // 一時フォルダを作成
            if (!Directory.Exists(tempExtractPath))
            {
                Directory.CreateDirectory(tempExtractPath);
            }
            // Zip展開
            await UniTask.RunOnThreadPool(() =>
             {
                 ZipFile.ExtractToDirectory(fromPath, tempExtractPath, overwriteFiles: true);
                 // ソースパスに展開
                 foreach (var file in Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories))
                 {
                     // 自身のzipは解凍しない
                     if (file.Contains(Path.GetFileName(filePath)))
                     {
                         continue;
                     }
                     string fileName = Path.GetFileName(file);
                     string destPath = Path.Combine(toPath, fileName);
                     // 移動先ディレクトリを作成
                     if (!Directory.Exists(toPath))
                     {
                         Directory.CreateDirectory(toPath);
                     }
                     // 移動先ファイルを削除
                     if (File.Exists(destPath))
                     {
                         File.Delete(destPath);
                     }
                     File.Move(file, destPath);
                 }
             }, cancellationToken: token);
        }
        else
        {
            await UniTask.RunOnThreadPool(() =>
            {
                DirectoryCopy(fromPath, toPath, true);
            }, cancellationToken: token);
        }
        // キャッシュ化処理
        Debug.Log($"ファイルをキャッシュ化中: {toPath}");
        // 一時フォルダ削除
        if (Directory.Exists(tempExtractPath))
        {
            Directory.Delete(tempExtractPath, true);
        }
        return;
    }

    private static void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
    {
        // If source is a file, copy the single file into the destination directory
        if (File.Exists(sourceDir))
        {
            Directory.CreateDirectory(destDir);
            string destFilePath = Path.Combine(destDir, Path.GetFileName(sourceDir));
            File.Copy(sourceDir, destFilePath, true);
            return;
        }

        DirectoryInfo dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException("Source directory not found: " + sourceDir);

        DirectoryInfo[] dirs = dir.GetDirectories();
        Directory.CreateDirectory(destDir);

        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(destDir, file.Name);
            file.CopyTo(tempPath, true);
        }

        if (copySubDirs)
        {
            foreach (DirectoryInfo subdir in dirs)
            {
                string tempPath = Path.Combine(destDir, subdir.Name);
                DirectoryCopy(subdir.FullName, tempPath, true);
            }
        }
    }
}
