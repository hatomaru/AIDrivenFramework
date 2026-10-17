using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

enum SetupFileKind { Folder, Model, Runtime, Other }

/// <summary>In-game file browser. Identification is by file type/name, not executable or model validation.</summary>
sealed class SetupFilePicker : IDisposable
{
    readonly VisualElement root;
    readonly bool ja, model;
    readonly Texture2D accent;
    readonly Action<string> selected;
    readonly TextField location, search;
    readonly Label message;
    readonly ScrollView list;
    readonly ProgressBar loading;
    readonly IVisualElementScheduledItem ticker;
    readonly Func<string, string, int, CancellationToken, Action<int>, Entry[]> reader;
    readonly TimeSpan readTimeout;
    CancellationTokenSource request;
    int generation, scanned;
    bool disposed;
    public bool IsLoading { get; private set; }
    public sealed class Entry
    {
        public string Path, Name;
        public SetupFileKind Kind;
    }
    string directory;
    public bool IsModel => model;
    public string CurrentDirectory => directory;
    int limit = 100;
    string L(string en, string jp) => ja ? jp : en;

    public SetupFilePicker(VisualElement root, bool ja, bool model, string initial, Texture2D accent, Action<string> selected, Func<string, string, int, CancellationToken, Action<int>, Entry[]> reader = null, TimeSpan? readTimeout = null)
    {
        this.root = root; this.ja = ja; this.model = model; this.accent = accent; this.selected = selected; this.reader = reader ?? ReadDirectory; this.readTimeout = readTimeout ?? TimeSpan.FromSeconds(15);
        root.AddToClassList("file-browser");
        var title = new Label(model ? L("Choose your AI model", "AIモデルを選ぶ") : L("Choose the runtime", "実行ソフトを選ぶ")); title.AddToClassList("title"); root.Add(title);
        var hint = new Label(model ? L("Choose a GGUF file. Folders open with one click.", "GGUFファイルを選んでください。フォルダーはクリックで開きます。") : L("Choose llama-server. Keep its DLLs and license notices together.", "llama-serverを選んでください。DLL・ライセンス通知も一緒に保管します。")); hint.AddToClassList("description"); root.Add(hint);
        var shortcuts = new VisualElement(); shortcuts.AddToClassList("browser-shortcuts"); root.Add(shortcuts);
        AddButton(shortcuts, L("Up", "上の階層"), () => Navigate(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? directory), "browser-up");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddButton(shortcuts, L("Home", "ホーム"), () => Navigate(home), "browser-home");
        AddButton(shortcuts, L("Downloads", "ダウンロード"), () => Navigate(Path.Combine(home, "Downloads")), "browser-downloads");
        var drives = new DropdownField { name = "browser-drive", choices = Directory.GetLogicalDrives().ToList() };
        if (drives.choices.Count > 0) drives.SetValueWithoutNotify(drives.choices[0]);
        drives.RegisterValueChangedCallback(e => Navigate(e.newValue)); shortcuts.Add(drives);
        var address = new VisualElement(); address.AddToClassList("browser-address"); root.Add(address);
        location = new TextField { name = "browser-location", tooltip = L("Folder path", "フォルダーの場所") }; address.Add(location);
        location.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Navigate(location.value); e.StopPropagation(); } });
        AddButton(address, L("Open", "開く"), () => Navigate(location.value), "browser-open");
        search = new TextField { name = "browser-search", label = L("Find a file", "ファイル名で絞り込む") }; root.Add(search);
        search.RegisterValueChangedCallback(_ => { limit = 100; Refresh(true); });
        message = new Label { name = "browser-status" }; message.AddToClassList("note"); root.Add(message);
        loading = new ProgressBar { name = "browser-loading", lowValue = 0, highValue = 100, value = 30 };
        loading.AddToClassList("browser-loading"); loading.style.display = DisplayStyle.None; root.Add(loading);
        ticker = root.schedule.Execute(() =>
        {
            if (!IsLoading || disposed) return;
            message.text = L("Reading folder… ", "読み込み中… ") + Volatile.Read(ref scanned) + L(" entries checked", "項目を確認");
            var fill = loading.Q(className: "unity-progress-bar__progress");
            float drift = PlayerPrefs.GetInt("AIDrivenFW.Setup.ReduceMotion", 0) == 1 ? .35f : Mathf.PingPong(Time.realtimeSinceStartup * .65f, 1);
            fill.style.translate = new Translate(drift * Mathf.Max(0, loading.resolvedStyle.width * .7f), 0);
        }).Every(80);
        list = new ScrollView(ScrollViewMode.Vertical) { name = "browser-files" }; root.Add(list);
        // Choose an initial location lexically: even existence probes can stall on network paths.
        string start = home;
        try
        {
            if (!string.IsNullOrWhiteSpace(initial)) start = initial.Trim().Trim('"');
            if (IdentifyName(Path.GetFileName(start)) != SetupFileKind.Other) start = Path.GetDirectoryName(Path.GetFullPath(start));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException) { start = home; }
        Navigate(string.IsNullOrWhiteSpace(start) ? Directory.GetCurrentDirectory() : start);
    }
    static void AddButton(VisualElement parent, string title, Action action, string name)
    { parent.Add(new Button(action) { name = name, text = title }); }

    static SetupFileKind IdentifyName(string name)
    {
        if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return SetupFileKind.Model;
        if (name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("llama-server", StringComparison.OrdinalIgnoreCase)) return SetupFileKind.Runtime;
        return SetupFileKind.Other;
    }
    // Runs only on a worker. Keep at most limit+1 sorted entries even in very large folders.
    public static Entry[] ReadDirectory(string path, string filter, int limit, CancellationToken ct, Action<int> progress)
    {
        var comparer = Comparer<Entry>.Create((a, b) =>
        {
            int folders = (a.Kind != SetupFileKind.Folder).CompareTo(b.Kind != SetupFileKind.Folder);
            if (folders != 0) return folders;
            int names = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            return names != 0 ? names : StringComparer.Ordinal.Compare(a.Path, b.Path);
        });
        var entries = new SortedSet<Entry>(comparer); int count = 0;
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (++count % 64 == 0) progress(count);
            var kind = (info.Attributes & FileAttributes.Directory) != 0 ? SetupFileKind.Folder : IdentifyName(info.Name);
            if (kind == SetupFileKind.Other || info.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            entries.Add(new Entry { Path = info.FullName, Name = info.Name, Kind = kind });
            if (entries.Count > limit + 1) entries.Remove(entries.Max);
        }
        ct.ThrowIfCancellationRequested(); progress(count); return entries.ToArray();
    }
    void Navigate(string value)
    {
        if (disposed) return;
        try
        {
            directory = Path.GetFullPath(value.Trim().Trim('"'));
            location.SetValueWithoutNotify(directory); search.SetValueWithoutNotify(""); limit = 100; Refresh();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
        { message.text = L("Check the folder path and try again.", "フォルダーの場所を確認してください。"); }
    }
    void Refresh(bool debounce = false)
    {
        if (disposed) return;
        request?.Cancel(); request?.Dispose(); request = new CancellationTokenSource();
        int current = ++generation; string path = directory, filter = search.value ?? ""; int maximum = limit;
        list.Clear(); scanned = 0; IsLoading = true; loading.style.display = DisplayStyle.Flex;
        message.text = L("Reading folder…", "読み込み中…");
        LoadAsync(path, filter, maximum, current, debounce, request.Token).Forget();
    }
    async UniTask LoadAsync(string path, string filter, int maximum, int current, bool debounce, CancellationToken cancelled)
    {
        using var timeout = new CancellationTokenSource(readTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancelled, timeout.Token);
        var ct = linked.Token;
        try
        {
            if (debounce) await UniTask.Delay(180, ignoreTimeScale: true, cancellationToken: ct);
            var entries = await Task.Run(() => reader(path, filter, maximum, ct, n => { if (current == Volatile.Read(ref generation)) Volatile.Write(ref scanned, n); }), ct)
                .AsUniTask().AttachExternalCancellation(ct);
            await UniTask.SwitchToMainThread();
            if (disposed || current != generation) return;
            int displayed = 0;
            foreach (var entry in entries.Take(maximum))
            {
                ct.ThrowIfCancellationRequested(); if (disposed || current != generation) return;
                var kind = entry.Kind;
                bool allowed = kind == SetupFileKind.Folder || (model ? kind == SetupFileKind.Model : kind == SetupFileKind.Runtime);
                // No filesystem probes on click: the setup validates selected files before execution.
                var row = new Button(() => { if (kind == SetupFileKind.Folder) Navigate(entry.Path); else selected(entry.Path); })
                    { name = "file-entry", userData = entry.Path, tooltip = entry.Path };
                row.AddToClassList("browser-row"); row.Add(new SetupFileIcon(kind, accent));
                var labels = new VisualElement(); labels.AddToClassList("browser-labels"); row.Add(labels);
                var name = new Label(entry.Name); name.AddToClassList("browser-name"); labels.Add(name);
                var type = new Label(kind == SetupFileKind.Folder ? L("Folder", "フォルダー") : kind == SetupFileKind.Model ? L("GGUF model", "GGUFモデル") : L("llama.cpp runtime", "llama.cpp実行ソフト"));
                type.AddToClassList("browser-type"); labels.Add(type); row.SetEnabled(allowed); list.Add(row);
                if (++displayed % 12 == 0) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (entries.Length > maximum) AddButton(list, L("Show more", "さらに表示"), () => { limit += 100; Refresh(); }, "browser-more");
            message.text = entries.Length == 0 ? L("No matching files here. Try another folder or name.", "該当ファイルがありません。場所や絞り込みを変えてください。")
                : L("Folders · GGUF models · llama-server files", "フォルダー ・ GGUFモデル ・ llama-serverファイル");
        }
        catch (OperationCanceledException)
        {
            await UniTask.SwitchToMainThread();
            if (!disposed && current == generation && !cancelled.IsCancellationRequested)
            { list.Clear(); message.text = L("This folder is taking too long. Try again or choose another location.", "読み込みに時間がかかっています。再試行するか、別の場所を選んでください。"); }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ArgumentException || ex is NotSupportedException)
        {
            await UniTask.SwitchToMainThread();
            if (!disposed && current == generation) { list.Clear(); message.text = L("This folder can't be read. Choose another folder.", "このフォルダーを読み取れません。別の場所を選んでください。"); }
        }
        finally
        {
            await UniTask.SwitchToMainThread();
            if (!disposed && current == generation) { IsLoading = false; loading.style.display = DisplayStyle.None; }
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; ++generation; request?.Cancel(); request?.Dispose(); ticker.Pause();
        IsLoading = false;
    }

}

/// <summary>Code-drawn vector glyphs keep file icons crisp at every UI scale.</summary>
sealed class SetupFileIcon : VisualElement
{
    readonly SetupFileKind kind;
    public SetupFileIcon(SetupFileKind kind, Texture2D accent)
    {
        this.kind = kind; pickingMode = PickingMode.Ignore; AddToClassList("file-kind-icon");
        AddToClassList("file-kind-" + kind.ToString().ToLowerInvariant()); style.backgroundImage = new StyleBackground(accent);
        generateVisualContent += Paint;
    }
    void Paint(MeshGenerationContext context)
    {
        var p = context.painter2D; p.strokeColor = Color.white; p.fillColor = Color.white; p.lineWidth = 1.7f;
        p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
        if (kind == SetupFileKind.Folder)
        {
            p.BeginPath(); p.MoveTo(new Vector2(9,13));p.LineTo(new Vector2(17,13));p.LineTo(new Vector2(20,16));p.LineTo(new Vector2(31,16));p.LineTo(new Vector2(31,28));p.LineTo(new Vector2(9,28));p.ClosePath();p.Stroke();
        }
        else if (kind == SetupFileKind.Model)
        {
            for (int y=0;y<3;y++) { p.BeginPath();p.MoveTo(new Vector2(10,17+y*5));p.LineTo(new Vector2(20,22+y*5));p.LineTo(new Vector2(30,17+y*5));p.Stroke(); }
            p.BeginPath();p.MoveTo(new Vector2(10,17));p.LineTo(new Vector2(20,12));p.LineTo(new Vector2(30,17));p.Stroke();
        }
        else
        {
            // A processor chip represents the inference runtime without suggesting video playback.
            p.BeginPath();p.MoveTo(new Vector2(13,13));p.LineTo(new Vector2(27,13));p.LineTo(new Vector2(27,27));p.LineTo(new Vector2(13,27));p.ClosePath();p.Stroke();
            p.BeginPath();p.MoveTo(new Vector2(17,17));p.LineTo(new Vector2(23,17));p.LineTo(new Vector2(23,23));p.LineTo(new Vector2(17,23));p.ClosePath();p.Stroke();
            for (int i = 0; i < 3; i++)
            {
                float pin = 16 + i * 4;
                p.BeginPath();p.MoveTo(new Vector2(pin,9));p.LineTo(new Vector2(pin,13));p.Stroke();
                p.BeginPath();p.MoveTo(new Vector2(pin,27));p.LineTo(new Vector2(pin,31));p.Stroke();
                p.BeginPath();p.MoveTo(new Vector2(9,pin));p.LineTo(new Vector2(13,pin));p.Stroke();
                p.BeginPath();p.MoveTo(new Vector2(27,pin));p.LineTo(new Vector2(31,pin));p.Stroke();
            }
        }
    }
}
