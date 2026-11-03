using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public enum SetupFileKind { Folder, Model, Runtime, Other }

/// <summary>Asynchronous file data bound to the fixed rows authored in the Canvas scene.</summary>
public sealed class SetupFilePicker : MonoBehaviour
{
    [Serializable] public sealed class Row
    {
        public Button Button;
        public Image Icon;
        public Text Name, Kind;
    }
    public sealed class Entry { public string Path, Name; public SetupFileKind Kind; }
    sealed class ReadCounter { public int Count; }
    [Header("Scene bindings")]
    public Text Title, Description, Status, PageCaption, DriveCaption;
    public InputField Location, Search;
    public Button OpenButton, UpButton, HomeButton, DownloadsButton, DriveButton, PreviousButton, NextButton;
    public GameObject LoadingTrack;
    public RectTransform LoadingFill;
    public ScrollRect Scroll;
    public RectTransform Content;
    public Toggle ReduceMotion;
    public Row[] Rows;
    public Sprite FolderIcon, ModelIcon, RuntimeIcon;
    [Header("Read settings")]
    [Min(.05f)] public float TimeoutSeconds = 15;
    public float RowHeight = 68;
    public bool IsLoading { get; private set; }
    public string CurrentDirectory => directory;
    public bool IsModel => model;
    public Func<string, string, int, CancellationToken, Action<int>, Entry[]> ReaderOverride { get; set; }
    CancellationTokenSource request;
    ReadCounter counter;
    Entry[] entries = Array.Empty<Entry>();
    Action<string> selected;
    string directory;
    string[] drives;
    int generation, page, maximum = 100, drive;
    bool initialized, open, ja, model, hasMore;
    string L(string en, string jp) => ja ? jp : en;

    void Initialize()
    {
        if (initialized) return; initialized = true;
        OpenButton.onClick.AddListener(() => Navigate(Location.text));
        UpButton.onClick.AddListener(() => Navigate(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? directory));
        HomeButton.onClick.AddListener(() => Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        DownloadsButton.onClick.AddListener(() => Navigate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
        drives = Directory.GetLogicalDrives();
        DriveButton.onClick.AddListener(() => { if (drives.Length == 0) return; drive = (drive + 1) % drives.Length; Navigate(drives[drive]); });
        PreviousButton.onClick.AddListener(() => { if (!IsLoading) { page = Math.Max(0, page - 1); BindRows(); } });
        NextButton.onClick.AddListener(() =>
        {
            if (IsLoading) return;
            page++;
            if (page * Rows.Length >= entries.Length && hasMore) { maximum += 100; Refresh(); }
            else BindRows();
        });
        Search.onValueChanged.AddListener(_ => { if (open) { page = 0; maximum = 100; Refresh(true); } });
        for (int i = 0; i < Rows.Length; i++) { int slot = i; Rows[i].Button.onClick.AddListener(() => Choose(slot)); }
    }
    public void Open(bool chooseModel, string initial, bool japanese, Action<string> onSelected)
    {
        Initialize(); open = true; model = chooseModel; ja = japanese; selected = onSelected; page = 0; maximum = 100; UpdateCaptions();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), start = home;
        try
        {
            if (!string.IsNullOrWhiteSpace(initial)) start = initial.Trim().Trim('"');
            if (IdentifyName(Path.GetFileName(start)) != SetupFileKind.Other) start = Path.GetDirectoryName(Path.GetFullPath(start));
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException) { start = home; }
        Navigate(string.IsNullOrWhiteSpace(start) ? Directory.GetCurrentDirectory() : start);
    }
    public void SetLanguage(bool japanese) { ja = japanese; UpdateCaptions(); if (!IsLoading && open) BindRows(); }
    void UpdateCaptions()
    {
        Title.text = model ? L("Choose your AI model", "AIモデルを選ぶ") : L("Choose the runtime", "実行ソフトを選ぶ");
        Description.text = model ? L("Choose a GGUF file. Open folders with one click.", "GGUFファイルを選んでください。フォルダーはクリックで開きます。") : L("Choose llama-server. Keep DLLs and license notices with it.", "llama-serverを選んでください。DLL・ライセンス通知も保管します。");
    }
    void Navigate(string value)
    {
        if (!open) return;
        try
        {
            directory = Path.GetFullPath(value.Trim().Trim('"')); Location.SetTextWithoutNotify(directory); Search.SetTextWithoutNotify(""); page = 0; maximum = 100;
            drive = Array.FindIndex(drives, d => directory.StartsWith(d, StringComparison.OrdinalIgnoreCase)); DriveCaption.text = drive >= 0 ? drives[drive] : L("Drive", "ドライブ");
            Refresh();
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
        { Status.text = L("Check the folder path and try again.", "フォルダーの場所を確認してください。"); }
    }
    void Refresh(bool debounce = false)
    {
        if (!open) return;
        request?.Cancel(); request?.Dispose(); request = new CancellationTokenSource();
        hasMore = false;
        int current = Interlocked.Increment(ref generation); var readCounter = new ReadCounter(); counter = readCounter;
        foreach (var row in Rows) row.Button.gameObject.SetActive(false);
        IsLoading = true; LoadingTrack.SetActive(true); PreviousButton.interactable = NextButton.interactable = false; PageCaption.text = "";
        Status.text = L("Reading folder…", "読み込み中…");
        LoadAsync(directory, Search.text ?? "", maximum, current, readCounter, debounce, request.Token).Forget();
    }
    async UniTask LoadAsync(string path, string filter, int limit, int current, ReadCounter readCounter, bool debounce, CancellationToken cancelled)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancelled, timeout.Token); var ct = linked.Token;
        try
        {
            if (debounce) await UniTask.Delay(180, ignoreTimeScale: true, cancellationToken: ct);
            var reader = ReaderOverride ?? ReadDirectory;
            var found = await Task.Run(() => reader(path, filter, limit, ct, n => Volatile.Write(ref readCounter.Count, n)), ct).AsUniTask().AttachExternalCancellation(ct);
            await UniTask.SwitchToMainThread(); if (!open || current != generation) return;
            hasMore = found.Length > limit; entries = found.Take(limit).ToArray(); IsLoading = false; BindRows();
        }
        catch (OperationCanceledException)
        {
            await UniTask.SwitchToMainThread();
            if (open && current == generation && !cancelled.IsCancellationRequested) { entries = Array.Empty<Entry>(); Status.text = L("Reading is taking too long. Retry or choose another location.", "読み込みに時間がかかっています。再試行するか、別の場所を選んでください。"); }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException || e is ArgumentException || e is NotSupportedException)
        {
            await UniTask.SwitchToMainThread();
            if (open && current == generation) { entries = Array.Empty<Entry>(); Status.text = L("This folder can't be read. Choose another location.", "このフォルダーを読み取れません。別の場所を選んでください。"); }
        }
        finally
        {
            await UniTask.SwitchToMainThread();
            if (open && current == generation) { IsLoading = false; LoadingTrack.SetActive(false); PreviousButton.interactable = page > 0; NextButton.interactable = (page + 1) * Rows.Length < entries.Length || hasMore; }
        }
    }
    void BindRows()
    {
        page = Mathf.Clamp(page, 0, Math.Max(0, (entries.Length - 1) / Rows.Length)); int visible = 0;
        for (int i = 0; i < Rows.Length; i++)
        {
            int index = page * Rows.Length + i; var row = Rows[i]; row.Button.gameObject.SetActive(index < entries.Length); if (index >= entries.Length) continue;
            var entry = entries[index]; visible++;
            row.Name.text = entry.Name; row.Kind.text = entry.Kind == SetupFileKind.Folder ? L("Folder", "フォルダー") : entry.Kind == SetupFileKind.Model ? L("GGUF model", "GGUFモデル") : L("llama.cpp runtime", "llama.cpp実行ソフト");
            row.Icon.sprite = entry.Kind == SetupFileKind.Folder ? FolderIcon : entry.Kind == SetupFileKind.Model ? ModelIcon : RuntimeIcon;
            row.Button.interactable = entry.Kind == SetupFileKind.Folder || (model ? entry.Kind == SetupFileKind.Model : entry.Kind == SetupFileKind.Runtime);
        }
        Content.sizeDelta = new Vector2(Content.sizeDelta.x, visible * RowHeight); Scroll.verticalNormalizedPosition = 1;
        Status.text = entries.Length == 0 ? L("No matching files. Try another folder or name.", "該当ファイルがありません。場所や絞り込みを変えてください。") : L("Folders · GGUF models · llama-server files", "フォルダー ・ GGUFモデル ・ llama-serverファイル");
        PageCaption.text = (page + 1) + " / " + Math.Max(1, (entries.Length + Rows.Length - 1) / Rows.Length) + (hasMore ? " +" : "");
        PreviousButton.interactable = page > 0; NextButton.interactable = (page + 1) * Rows.Length < entries.Length || hasMore;
    }
    void Choose(int slot)
    {
        if (!open || IsLoading) return; int index = page * Rows.Length + slot; if (index >= entries.Length) return;
        var entry = entries[index]; if (entry.Kind == SetupFileKind.Folder) Navigate(entry.Path);
        else if (model ? entry.Kind == SetupFileKind.Model : entry.Kind == SetupFileKind.Runtime) selected?.Invoke(entry.Path);
    }
    void Update()
    {
        if (!open || !IsLoading) return;
        Status.text = L("Reading folder… ", "読み込み中… ") + Volatile.Read(ref counter.Count) + L(" entries checked", "項目を確認");
        float drift = ReduceMotion != null && ReduceMotion.isOn ? .35f : Mathf.PingPong(Time.unscaledTime * .65f, 1);
        LoadingFill.anchoredPosition = new Vector2(drift * (((RectTransform)LoadingTrack.transform).rect.width - LoadingFill.rect.width), 0);
    }
    public void Close()
    {
        open = false; Interlocked.Increment(ref generation); request?.Cancel(); request?.Dispose(); request = null; IsLoading = false;
        if (LoadingTrack != null) LoadingTrack.SetActive(false);
    }
    void OnDisable() => Close();
    void OnDestroy() => Close();
    static SetupFileKind IdentifyName(string name)
    {
        if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return SetupFileKind.Model;
        if (name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("llama-server", StringComparison.OrdinalIgnoreCase)) return SetupFileKind.Runtime;
        return SetupFileKind.Other;
    }
    public static Entry[] ReadDirectory(string path, string filter, int limit, CancellationToken ct, Action<int> progress)
    {
        var comparer = Comparer<Entry>.Create((a, b) =>
        {
            int folders = (a.Kind != SetupFileKind.Folder).CompareTo(b.Kind != SetupFileKind.Folder); if (folders != 0) return folders;
            int names = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); return names != 0 ? names : StringComparer.Ordinal.Compare(a.Path, b.Path);
        });
        var result = new SortedSet<Entry>(comparer); int count = 0;
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested(); if (++count % 64 == 0) progress(count);
            var kind = (info.Attributes & FileAttributes.Directory) != 0 ? SetupFileKind.Folder : IdentifyName(info.Name);
            if (kind == SetupFileKind.Other || info.Name.IndexOf(filter ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
            result.Add(new Entry { Path = info.FullName, Name = info.Name, Kind = kind }); if (result.Count > limit + 1) result.Remove(result.Max);
        }
        ct.ThrowIfCancellationRequested(); progress(count); return result.ToArray();
    }
}
