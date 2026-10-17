using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

enum SetupFileKind { Folder, Model, Runtime, Other }

/// <summary>In-game file browser. Identification is by file type/name, not executable or model validation.</summary>
sealed class SetupFilePicker
{
    readonly VisualElement root;
    readonly bool ja, model;
    readonly Texture2D accent;
    readonly Action<string> selected;
    readonly TextField location, search;
    readonly Label message;
    readonly ScrollView list;
    string directory;
    public bool IsModel => model;
    public string CurrentDirectory => directory;
    int limit = 100;
    string L(string en, string jp) => ja ? jp : en;

    public SetupFilePicker(VisualElement root, bool ja, bool model, string initial, Texture2D accent, Action<string> selected)
    {
        this.root = root; this.ja = ja; this.model = model; this.accent = accent; this.selected = selected;
        root.AddToClassList("file-browser");
        var title = new Label(model ? L("Choose your AI model", "AIモデルを選ぶ") : L("Choose the runtime", "実行ソフトを選ぶ")); title.AddToClassList("title"); root.Add(title);
        var hint = new Label(model ? L("Choose a GGUF file. Folders open with one click.", "GGUFファイルを選んでください。フォルダーはクリックで開きます。") : L("Choose llama-server. Keep its DLLs and license notices together.", "llama-serverを選んでください。DLL・ライセンス通知も一緒に保管します。")); hint.AddToClassList("description"); root.Add(hint);
        var shortcuts = new VisualElement(); shortcuts.AddToClassList("browser-shortcuts"); root.Add(shortcuts);
        AddButton(shortcuts, L("Up", "上の階層"), () => Navigate(Directory.GetParent(directory)?.FullName ?? directory), "browser-up");
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
        search.RegisterValueChangedCallback(_ => { limit = 100; Refresh(); });
        message = new Label(); message.AddToClassList("note"); root.Add(message);
        list = new ScrollView(ScrollViewMode.Vertical) { name = "browser-files" }; root.Add(list);
        string start = Directory.Exists(initial) ? initial : File.Exists(initial) ? Path.GetDirectoryName(Path.GetFullPath(initial)) : home;
        Navigate(Directory.Exists(start) ? start : Directory.GetCurrentDirectory());
    }
    static void AddButton(VisualElement parent, string title, Action action, string name)
    { parent.Add(new Button(action) { name = name, text = title }); }

    public static SetupFileKind Identify(string path)
    {
        if (Directory.Exists(path)) return SetupFileKind.Folder;
        string name = Path.GetFileName(path);
        if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return SetupFileKind.Model;
        if (name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("llama-server", StringComparison.OrdinalIgnoreCase)) return SetupFileKind.Runtime;
        return SetupFileKind.Other;
    }
    void Navigate(string value)
    {
        try
        {
            string next = Path.GetFullPath(value.Trim().Trim('"'));
            if (!Directory.Exists(next)) throw new DirectoryNotFoundException();
            directory = next; location.SetValueWithoutNotify(directory); search.SetValueWithoutNotify(""); limit = 100; Refresh();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
        { message.text = L("This folder can't be opened. Choose another folder.", "このフォルダーを開けません。別の場所を選んでください。"); }
    }
    void Refresh()
    {
        list.Clear();
        try
        {
            var entries = new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .Select(e => new { File = e, Kind = Identify(e.FullName) })
                .Where(e => e.Kind != SetupFileKind.Other && e.File.Name.IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(e => e.Kind != SetupFileKind.Folder).ThenBy(e => e.File.Name, StringComparer.OrdinalIgnoreCase).Take(limit + 1).ToArray();
            message.text = L("Folders · GGUF models · llama-server files", "フォルダー ・ GGUFモデル ・ llama-serverファイル");
            if (entries.Length == 0) message.text = L("No matching files here. Try another folder or name.", "該当ファイルがありません。場所や絞り込みを変えてください。");
            foreach (var entry in entries.Take(limit))
            {
                string path = entry.File.FullName; var kind = entry.Kind;
                bool allowed = kind == SetupFileKind.Folder || (model ? kind == SetupFileKind.Model : kind == SetupFileKind.Runtime);
                var row = new Button(() => { if (kind == SetupFileKind.Folder) Navigate(path); else if (File.Exists(path)) selected(path); else Refresh(); })
                    { name = "file-entry", userData = path, tooltip = path };
                row.AddToClassList("browser-row"); row.Add(new SetupFileIcon(kind, accent));
                var labels = new VisualElement(); labels.AddToClassList("browser-labels"); row.Add(labels);
                var name = new Label(entry.File.Name); name.AddToClassList("browser-name"); labels.Add(name);
                var type = new Label(kind == SetupFileKind.Folder ? L("Folder", "フォルダー") : kind == SetupFileKind.Model ? L("GGUF model", "GGUFモデル") : L("llama.cpp runtime", "llama.cpp実行ソフト"));
                type.AddToClassList("browser-type"); labels.Add(type);
                row.SetEnabled(allowed); list.Add(row);
            }
            if (entries.Length > limit) AddButton(list, L("Show more", "さらに表示"), () => { limit += 100; Refresh(); }, "browser-more");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        { message.text = L("This folder can't be read. Choose another folder.", "このフォルダーを読み取れません。別の場所を選んでください。"); }
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
            p.BeginPath();p.MoveTo(new Vector2(10,11));p.LineTo(new Vector2(30,11));p.LineTo(new Vector2(30,29));p.LineTo(new Vector2(10,29));p.ClosePath();p.Stroke();
            p.BeginPath();p.MoveTo(new Vector2(17,16));p.LineTo(new Vector2(25,20));p.LineTo(new Vector2(17,24));p.ClosePath();p.Fill();
        }
    }
}
