using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using AIDrivenFW.API;
using AIDrivenFW.Config;
using AIDrivenFW.Setup;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>One purpose per slide; acquisition starts only after explicit consent.</summary>
public sealed class PlayerSetupScreen : MonoBehaviour
{
    enum Slide { Welcome, Runtime, Models, Custom, Browse, Consent, Progress, Result }
    PanelSettings panel;
    Font font;
    SetupAtmosphere atmosphere;
    readonly List<VisualElement> icons = new List<VisualElement>();
    readonly List<VisualElement> sparks = new List<VisualElement>();
    readonly List<VisualElement> celebration = new List<VisualElement>();
    Label resultSymbol;
    VisualElement root, content, current, cards;
    readonly Dictionary<Slide, VisualElement> slides = new Dictionary<Slide, VisualElement>();
    readonly Dictionary<string, string[]> copy = new Dictionary<string, string[]>();
    Label chosen, download, runtime, source, error, statusTitle, statusCopy, percent, bytes, remaining, stageCaption, resultTitle, resultCopy, backendStatus;
    VisualElement runtimeChoice, modelChoice, llamaHelp;
    ProgressBar progressBar, resultBar;
    Button primary, secondary;
    Toggle consent, reduced;
    DropdownField installed, backend, ggufCatalog;
    Label modelTermsNote;
    SetupFilePicker fileBrowser;
    TextField tag, size, runtimePath, modelPath;
    PlayerSetupPlan selection;
    InstalledModel[] installedModels = Array.Empty<InstalledModel>();
    PlayerSetupServices services;
    PlayerSetupFlow flow;
    SetupCompletion completion;
    CancellationTokenSource operation;
    readonly Queue<SetupProgress> pending = new Queue<SetupProgress>();
    readonly object progressLock = new object();
    SetupProgress latest;
    Slide slide;
    bool ja, busy, complete, closing, destroyed, runtimeChosen;
    float changedAt;
    public VisualElement Root => root;
    public PlayerSetupFlow Flow => flow;
    string L(string en, string jp) => ja ? jp : en;

    void Awake()
    {
        ja = PlayerPrefs.GetString("AIDrivenFW.Setup.Language", Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en") == "ja";
        selection = PlayerSetupPlan.Choices().FirstOrDefault() ?? new PlayerSetupPlan { Model = "", ModelSource = "", ModelTerms = "", ModelBytes = 0 };
        var journal = SetupJournal.Load();
        if (journal?.Revision == ModelInfo.CurrentConsentRevision && journal.Plan != null)
        {
            try { journal.Plan.Validate(); selection = journal.Plan; } catch (ArgumentException) { }
        }
        else
        {
            var saved = ModelInfo.LoadFromFile();
            var known = PlayerSetupPlan.Choices().FirstOrDefault(p => p.Model.Equals(saved?.Name, StringComparison.OrdinalIgnoreCase));
            if (known != null && saved.Mode == "Ollama") selection = known;
            else if (!string.IsNullOrWhiteSpace(saved?.Name)) selection = new PlayerSetupPlan
            {
                Model = saved.Name, Mode = saved.Mode, RuntimePath = saved.RuntimePath, ModelPath = saved.ModelPath,
                ModelTerms = saved.ModelTermsUrl, RuntimeTerms = saved.RuntimeTermsUrl,
                ModelSource = PlayerSetupPlan.OllamaModelPage(saved.Name), ModelBytes = 0, ReviewModelTermsAtSource = saved.LocalModelTermsReviewed
            };
        }
        if (selection.Mode == "Llama" && File.Exists(selection.ModelPath))
        { try { selection.ModelBytes = new FileInfo(selection.ModelPath).Length; } catch (IOException) { } }
        services = new PlayerSetupServices(); flow = new PlayerSetupFlow(services);
        flow.Changed += p => { lock (progressLock) pending.Enqueue(p); };
        Build(); RefreshLanguage(); Go(Slide.Welcome); LoadInstalledAsync().Forget();
    }

    void Build()
    {
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("PlayerSetup/Theme");
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.scale = 1; panel.sortingOrder = 10000;
        var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = panel;
        root = document.rootVisualElement; root.name = "player-setup"; root.AddToClassList("screen");
        root.styleSheets.Add(Resources.Load<StyleSheet>("PlayerSetup/Screen"));
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "Hiragino Sans", "Noto Sans CJK JP", "Noto Sans", "Arial" }, 20);
        if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        var languages = Box(root, "languages");
        Btn(languages, "日本語", () => SetLanguage(true), "language-ja"); Btn(languages, "English", () => SetLanguage(false), "language-en");
        var card = Box(root, "card");
        atmosphere = new SetupAtmosphere(root, card);
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "slide-scroll" }; scroll.AddToClassList("slide-scroll"); card.Add(scroll);
        scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        content = scroll.contentContainer;

        var welcome = MakeSlide(Slide.Welcome);
        Emblem(welcome);
        T(welcome, "title", "A little setup.\nThen play.", "このゲームに、\nあなたのAIを。", "title");
        T(welcome, "intro", "A one-time setup for AI on this PC.\nNo AI account or subscription needed.", "このPCで動くAIを、初回だけ準備します。\nAI用の登録や月額契約は不要です。", "description");
        chosen = Text(welcome, "", "chosen");
        Btn(welcome, "", () => Go(Slide.Runtime), "change-model").AddToClassList("link");

        var engines = MakeSlide(Slide.Runtime);
        T(engines, "runtime-title", "How would you like to start?", "AIの使い方を選ぶ", "title");
        T(engines, "runtime-copy", "Both work on this PC. Choose the way that suits you.\nOnly your selection will be prepared and run.", "このPCで使う方法を選んでください。\n選んだ方だけを準備・実行します。", "description");
        var automatic = Btn(engines, "", () =>
        {
            runtimeChosen = true;
            if (selection.Mode != "Ollama") Select(PlayerSetupPlan.Choices().FirstOrDefault() ?? new PlayerSetupPlan { Model = "", ModelBytes = 0, ModelTerms = "" });
            backend.value = "Ollama"; Go(Slide.Models);
        }, "runtime-ollama"); automatic.AddToClassList("runtime-card");
        T(automatic, "ollama-title", "Easy automatic setup", "かんたん自動準備", "model-tier");
        T(automatic, "ollama-copy", "Ollama · recommended\nChoose a model; we'll handle preparation.", "Ollama・おすすめ\nモデルを選ぶだけで準備できます。", "note");
        var files = Btn(engines, "", () => { runtimeChosen = true; backend.value = "llama.cpp / GGUF"; Go(Slide.Custom); }, "runtime-llama"); files.AddToClassList("runtime-card");
        T(files, "llama-title", "Use my model files", "手持ちのモデルを使う", "model-tier");
        T(files, "llama-copy", "llama.cpp / GGUF\nChoose your runtime and model files.", "llama.cpp / GGUF\n実行ソフトとモデルのファイルを選びます。", "note");

        var models = MakeSlide(Slide.Models);
        T(models, "models-title", "Choose your AI", "AIモデルを選ぶ", "title");
        T(models, "models-copy", "Start small. You can choose a different model.", "まずは軽いモデルから。自由に選べます。", "description");
        cards = Box(models, "model-cards");
        T(models, "memory-note", "Larger models use more memory and may respond slower.", "大きいモデルほどメモリを使い、応答が遅くなる場合があります。", "note");
        Btn(models, "", () => { backend.value = "Ollama"; Go(Slide.Custom); }, "custom-model").AddToClassList("link");

        var custom = MakeSlide(Slide.Custom);
        T(custom, "custom-title", "Use your own model", "別のモデルを使う", "title");
        T(custom, "custom-copy", "Choose a local text model and review its license.", "ローカルの文章生成モデルと利用条件を指定します。", "description");
        installed = new DropdownField { name = "installed-models", choices = new List<string> { "…" }, index = 0 }; custom.Add(installed);
        backend = new DropdownField { name = "backend", choices = new List<string> { "Ollama", "llama.cpp / GGUF" }, index = selection.Mode == "Llama" ? 1 : 0 }; custom.Add(backend);
        backendStatus = Text(custom, "", "note");
        llamaHelp = Box(custom, "llama-help");
        Btn(llamaHelp, "", () => OpenTerms("https://github.com/ggml-org/llama.cpp/releases"), "get-llama").AddToClassList("link");
        Btn(llamaHelp, "", () => { var entry = GgufCatalog().FirstOrDefault(); if (entry != null) OpenTerms(entry.DownloadUrl); }, "get-gguf").AddToClassList("link");
        tag = Field(custom, "model-tag", selection.Model);
        size = Field(custom, "model-size", Math.Ceiling(selection.ModelBytes / 1000000d).ToString(CultureInfo.InvariantCulture));
        runtimeChoice = Box(custom, "file-choice"); runtimeChoice.Add(new SetupFileIcon(SetupFileKind.Runtime, atmosphere.Accent)); runtimePath = Field(runtimeChoice, "runtime-path", selection.RuntimePath);
        Btn(runtimeChoice, "", () => ChooseFile(false), "browse-runtime");
        modelChoice = Box(custom, "file-choice"); modelChoice.Add(new SetupFileIcon(SetupFileKind.Model, atmosphere.Accent)); modelPath = Field(modelChoice, "gguf-path", selection.ModelPath);
        Btn(modelChoice, "", () => ChooseFile(true), "browse-model");
        ggufCatalog = new DropdownField { name = "gguf-catalog" }; custom.Add(ggufCatalog);
        modelTermsNote = Text(custom, "", "note"); modelTermsNote.name = "model-terms-note";
        ggufCatalog.RegisterValueChangedCallback(_ => { consent.value = false; UpdateModelTermsNote(); });
        T(custom, "file-note", "For GGUF, keep the selected files in their current locations.", "GGUFの場合、指定したファイルは現在の場所に保管してください。", "note");
        error = Text(custom, "", "error");
        backend.RegisterValueChangedCallback(_ =>
        {
            consent.value = false;
            if (backend.index == 1 && !File.Exists(runtimePath.value)) runtimePath.SetValueWithoutNotify(FindLlama());
            CustomVisibility();
        });
        foreach (var field in new[] { tag, size, runtimePath, modelPath }) field.RegisterValueChangedCallback(_ => consent.value = false);
        tag.RegisterValueChangedCallback(_ => UpdateModelTermsNote());
        modelPath.RegisterValueChangedCallback(_ => { ResetGgufCatalog(); UpdateFileSize(); UpdateModelTermsNote(); });
        runtimePath.RegisterValueChangedCallback(_ => CustomVisibility());
        installed.RegisterValueChangedCallback(e =>
        {
            var model = installedModels.FirstOrDefault(m => m.name == e.newValue); if (model == null) return;
            backend.SetValueWithoutNotify("Ollama"); tag.SetValueWithoutNotify(model.name);
            size.SetValueWithoutNotify(Math.Ceiling(model.size / 1000000d).ToString(CultureInfo.InvariantCulture));
            UpdateModelTermsNote();
            consent.value = false; CustomVisibility();
        });

        MakeSlide(Slide.Browse);

        var review = MakeSlide(Slide.Consent);
        T(review, "review-title", "Ready to get ready?", "準備を始めましょう。", "title");
        T(review, "review-copy", "Check your selection before we begin.", "取得する内容と利用条件を確認してください。", "description");
        var details = Box(review, "details");
        Text(details, "", "selection-name").name = "selection-name";
        download = Text(details, "", "detail"); runtime = Text(details, "", "detail"); source = Text(details, "", "note");
        T(review, "local-note", "AI prompts are processed on this PC.\nSetup downloads use the internet.", "AIへの入力は、このPC内で処理します。\n初回の取得にはインターネットを使います。", "description");
        var links = Box(review, "terms-links"); Btn(links, "", () => OpenTerms(selection.ModelTerms), "model-license").AddToClassList("link"); Btn(links, "", () => OpenTerms(selection.RuntimeTerms), "runtime-license").AddToClassList("link");
        consent = new Toggle { name = "consent" }; consent.AddToClassList("consent"); review.Add(consent); consent.RegisterValueChangedCallback(_ => UpdateActions());

        var progress = MakeSlide(Slide.Progress);
        Emblem(progress);
        statusTitle = Text(progress, "", "title"); statusTitle.name = "status-title";
        statusCopy = Text(progress, "", "description");
        stageCaption = Text(progress, "", "stage-caption"); stageCaption.name = "current-stage";
        progressBar = new ProgressBar { name = "overall-progress", lowValue = 0, highValue = 100, value = 0 }; progress.Add(progressBar);
        percent = Text(progress, "0%", "percent"); bytes = Text(progress, "", "download-stats"); bytes.name = "download-completed";
        remaining = Text(progress, "", "download-stats"); remaining.name = "download-remaining";
        T(progress, "progress-note", "Progress reflects completed steps and download data.\nThe first response can take a little longer.", "完了した工程と取得量からの目安です。\n最初の応答確認には時間がかかる場合があります。", "note");

        var result = MakeSlide(Slide.Result);
        var seal = Box(result, "result-seal");
        resultSymbol = Text(seal, "", "result-symbol"); resultSymbol.name = "result-symbol";
        resultSymbol.style.backgroundImage = new StyleBackground(atmosphere.Accent);
        for (int i = 0; i < 8; i++) { var spark = Box(seal, "celebration-spark"); spark.pickingMode = PickingMode.Ignore; celebration.Add(spark); }
        resultTitle = Text(result, "", "title"); resultCopy = Text(result, "", "description");
        resultBar = new ProgressBar { name = "completed-progress", lowValue = 0, highValue = 100, value = 100 }; result.Add(resultBar);
        Text(result, "100%", "percent").name = "completed-percent";
        Btn(result, "", () => Go(Slide.Models), "result-model").AddToClassList("link");
        var actions = Box(card, "actions"); secondary = Btn(actions, "", Secondary, "secondary"); primary = Btn(actions, "", Primary, "primary"); primary.AddToClassList("primary");
        var footer = Box(root, "footer"); reduced = new Toggle { name = "reduce-motion", value = PlayerPrefs.GetInt("AIDrivenFW.Setup.ReduceMotion", 0) == 1 }; footer.Add(reduced);
        reduced.RegisterValueChangedCallback(e => { PlayerPrefs.SetInt("AIDrivenFW.Setup.ReduceMotion", e.newValue ? 1 : 0); PlayerPrefs.Save(); });
        CustomVisibility();
        foreach (var button in root.Query<Button>().ToList())
        {
            button.RegisterCallback<PointerDownEvent>(_ => { if (!reduced.value && button.enabledInHierarchy) button.style.scale = new Scale(new Vector3(.97f, .97f, 1)); });
            button.RegisterCallback<PointerUpEvent>(_ => button.style.scale = new Scale(Vector3.one));
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.scale = new Scale(Vector3.one));
        }
        progressBar.Q(className: "unity-progress-bar__progress").style.backgroundImage = new StyleBackground(atmosphere.Accent);
        resultBar.Q(className: "unity-progress-bar__progress").style.backgroundImage = new StyleBackground(atmosphere.Accent);
    }

    void Emblem(VisualElement parent)
    {
        var orbit = Box(parent, "icon-orbit"); orbit.pickingMode = PickingMode.Ignore;
        var icon = atmosphere.CreateIcon(); orbit.Add(icon); icons.Add(icon);
        for (int i = 0; i < 3; i++) sparks.Add(Box(orbit, "spark"));
    }

    VisualElement MakeSlide(Slide value)
    { var element = Box(content, "slide"); element.name = "slide-" + value; slides[value] = element; return element; }
    void Go(Slide value)
    {
        if (slide == Slide.Browse && value != Slide.Browse) fileBrowser?.Dispose();
        slide = value; current = slides[value];
        foreach (var pair in slides) Visible(pair.Value, pair.Key == value);
        root.Q<ScrollView>("slide-scroll").scrollOffset = Vector2.zero;
        changedAt = Time.unscaledTime; UpdateActions();
        // Reused controls can be reached naturally with the keyboard after each transition.
        if (value == Slide.Consent) consent.Focus(); else if (value == Slide.Runtime)
            (root.Q<Button>("runtime-ollama").enabledSelf ? root.Q<Button>("runtime-ollama") : root.Q<Button>("runtime-llama")).Focus(); else primary.Focus();
    }
    void SetLanguage(bool value)
    { ja = value; PlayerPrefs.SetString("AIDrivenFW.Setup.Language", ja ? "ja" : "en"); PlayerPrefs.Save(); RefreshLanguage(); if (slide == Slide.Browse && fileBrowser != null) ChooseFile(fileBrowser.IsModel, fileBrowser.CurrentDirectory); }
    void RefreshLanguage()
    {
        foreach (var pair in copy) root.Q<Label>(pair.Key).text = pair.Value[ja ? 1 : 0];
        root.Q<Button>("language-ja").EnableInClassList("active-language", ja); root.Q<Button>("language-en").EnableInClassList("active-language", !ja);
        root.Q<Button>("change-model").text = L("Change model or runtime", "モデル・実行方法を変更"); root.Q<Button>("custom-model").text = L("Use another Ollama model", "別のOllamaモデルを使う");
        root.Q<Button>("get-llama").text = L("Get llama.cpp ↗", "実行ソフトを入手 ↗"); root.Q<Button>("get-gguf").text = L("Model distribution page ↗", "モデルの配布ページ ↗");
        root.Q<Button>("runtime-ollama").SetEnabled(AIDrivenConfig.Instance?.IsAllaiveOllama ?? true);
        root.Q<Button>("browse-runtime").text = root.Q<Button>("browse-model").text = L("Choose file", "ファイルを選ぶ");
        root.Q<Button>("result-model").text = L("Choose another model", "モデルを選び直す");
        root.Q<Button>("model-license").text = L("Model license ↗", "モデルの利用条件 ↗"); root.Q<Button>("runtime-license").text = L("Runtime license ↗", "実行ソフトの利用条件 ↗");
        installed.label = L("Installed on this PC", "導入済みモデル"); backend.label = L("Runtime", "実行方法");
        tag.label = L("Ollama model tag", "Ollamaモデル名"); size.label = L("Estimated download (MB)", "取得容量の目安（MB）");
        runtimePath.label = L("1. llama-server file", "1. llama-serverファイル"); modelPath.label = L("2. GGUF model file", "2. GGUFモデルファイル");
        consent.text = L("I've reviewed both licenses and agree to download and run this selection.", "両方の利用条件を確認し、取得と実行に同意します。");
        reduced.text = L("Reduce motion", "動きを減らす");
        RefreshGgufCatalog(); BuildCards(); CustomVisibility(); UpdateSummary(); UpdateActions(); if (latest != null) RenderProgress(latest, false);
    }
    void BuildCards()
    {
        cards.Clear(); var catalog = PlayerSetupPlan.Choices();
        for (int i = 0; i < catalog.Length; i++)
        {
            int index = i; var button = Btn(cards, "", () => Select(catalog[index]), "model-" + i); button.AddToClassList("model-card");
            var labels = Box(button, "model-labels");
            Text(labels, catalog[i].Level == ModelLevel.Light ? L("Lightweight", "ライト") + (i == 0 ? L(" · recommended", "・おすすめ") : "") : catalog[i].Level == ModelLevel.Balanced ? L("Medium", "ミディアム") : L("Large", "ラージ"), "model-tier");
            Text(labels, catalog[i].Model, "note"); Text(button, catalog[i].ModelBytes > 0 ? FormatBytes(catalog[i].ModelBytes) : L("Review size", "容量を確認"), "model-size");
            button.EnableInClassList("selected", selection.Mode == "Ollama" && selection.Model.Equals(catalog[i].Model, StringComparison.OrdinalIgnoreCase));
        }
    }
    void Select(PlayerSetupPlan plan)
    {
        selection = plan.Snapshot(); consent.value = false; error.text = "";
        tag.SetValueWithoutNotify(selection.Model); size.SetValueWithoutNotify(Math.Ceiling(selection.ModelBytes / 1000000d).ToString(CultureInfo.InvariantCulture)); backend.SetValueWithoutNotify("Ollama");
        CustomVisibility(); BuildCards(); UpdateSummary();
    }
    void RefreshGgufCatalog()
    {
        int index = Math.Max(0, ggufCatalog.index);
        ggufCatalog.label = L("Model information", "モデル情報");
        ggufCatalog.choices = new List<string> { L("Other GGUF · review terms at its source", "その他のGGUF・入手元で利用条件を確認") };
        ggufCatalog.choices.AddRange(GgufCatalog().Select(e => e.ModelName));
        ggufCatalog.SetValueWithoutNotify(ggufCatalog.choices[Math.Min(index, ggufCatalog.choices.Count - 1)]);
        UpdateModelTermsNote();
    }
    void ResetGgufCatalog()
    {
        // Never infer a license from a filename. The player can identify a configured model.
        if (ggufCatalog.choices.Count > 0) ggufCatalog.SetValueWithoutNotify(ggufCatalog.choices[0]);
    }
    string CustomModelTerms()
    {
        if (backend.index == 1)
        {
            var entry = ggufCatalog.index > 0 ? GgufCatalog().ElementAtOrDefault(ggufCatalog.index - 1) : null;
            return !string.IsNullOrWhiteSpace(entry?.LicenseUrl) ? entry.LicenseUrl : entry?.DownloadUrl ?? "";
        }
        string configured = PlayerSetupPlan.Choices().FirstOrDefault(p => p.Model.Equals(tag.value.Trim(), StringComparison.OrdinalIgnoreCase))?.ModelTerms;
        return !string.IsNullOrWhiteSpace(configured) ? configured : PlayerSetupPlan.OllamaModelPage(tag.value.Trim());
    }
    void UpdateModelTermsNote()
    {
        if (modelTermsNote == null || backend == null) return;
        modelTermsNote.text = backend.index == 1 && string.IsNullOrWhiteSpace(CustomModelTerms())
            ? L("No URL to enter. Review this model's terms at its original source before continuing.", "URL入力は不要です。モデルの入手元で利用条件を確認してください。")
            : L("Terms are provided automatically. Review them on the next screen.", "利用条件は自動で表示します。次の画面で確認してください。");
    }
    bool ApplyCustom()
    {
        var plan = new PlayerSetupPlan { Model = tag.value.Trim(), Mode = backend.index == 1 ? "Llama" : "Ollama", ModelTerms = CustomModelTerms(),
            ModelSource = PlayerSetupPlan.OllamaModelPage(tag.value.Trim()), RuntimePath = runtimePath.value.Trim().Trim('"'), ModelPath = modelPath.value.Trim().Trim('"'),
            RuntimeTerms = backend.index == 1 ? "https://github.com/ggml-org/llama.cpp/blob/master/LICENSE" : RuntimeRelease.TermsUrl };
        plan.ReviewModelTermsAtSource = plan.Mode == "Llama" && string.IsNullOrWhiteSpace(plan.ModelTerms);
        plan.ModelBytes = double.TryParse(size.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0 && mb < 1000000 ? (long)(mb * 1000000) : 0;
        try
        {
            if (plan.Mode == "Llama" && File.Exists(plan.ModelPath)) { plan.ModelBytes = new FileInfo(plan.ModelPath).Length; plan.Model = Path.GetFileName(plan.ModelPath); }
            plan.Validate(); selection = plan; consent.value = false; error.text = ""; BuildCards(); UpdateSummary(); return true;
        }
        catch (Exception) { error.text = L("Check the model, size and selected files.", "モデル名・容量・選んだファイルを確認してください。"); return false; }
    }
    void CustomVisibility()
    {
        bool gguf = backend.index == 1;
        Visible(runtimeChoice, gguf); Visible(modelChoice, gguf); Visible(llamaHelp, gguf); Visible(tag, !gguf); Visible(size, !gguf); Visible(installed, !gguf);
        Visible(ggufCatalog, gguf); UpdateModelTermsNote();
        root.Q<Label>("custom-title").text = gguf ? L("Use your GGUF model", "手持ちのGGUFモデルを使う") : L("Use another model", "別のモデルを使う");
        root.Q<Label>("custom-copy").text = gguf ? L("Choose two files, then review their licenses.\nNo terminal commands needed.", "ファイルを2つ選び、利用条件を確認します。\nコマンド入力は不要です。") : L("Choose an installed model or enter a local model tag.", "導入済みモデルを選ぶか、ローカルモデル名を指定します。");
        bool ollama = installedModels.Length > 0 || !string.IsNullOrEmpty(PlayerSetupServices.FindOllama());
        bool llama = File.Exists(runtimePath.value) || !string.IsNullOrEmpty(FindLlama());
        backendStatus.text = ollama && llama ? L("Both runtimes found. Only your selection will run.", "両方の実行方法が見つかりました。選んだ方だけを使います。") : gguf ? L("Keep the runtime's DLLs and license notices together.", "実行ソフトはDLL・ライセンス通知と一緒に保管してください。") : L("Automatic preparation · recommended", "自動で準備・おすすめ");
        Visible(root.Q<Label>("file-note"), false);
    }
    static string FindLlama()
    {
        var saved = ModelInfo.LoadFromFile();
        if (saved?.Mode == "Llama" && File.Exists(saved.RuntimePath)) return saved.RuntimePath;
        string name = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor ? "llama-server.exe" : "llama-server";
        string folder = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
        foreach (string relative in new[] { name, Path.Combine("bin", name) })
        { var path = Path.Combine(folder, relative); if (File.Exists(path)) return path; }
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { try { var path = Path.Combine(directory.Trim('"'), name); if (File.Exists(path)) return path; } catch (ArgumentException) { } }
        return "";
    }
    static ModelInfoConfig[] GgufCatalog() => (AIDrivenConfig.Instance?.RecommendModelInfos ?? AIDrivenConfig.CreateRecommendedModels(false)).Where(e => e != null && !string.IsNullOrWhiteSpace(e.ModelName)).ToArray();
    void ChooseFile(bool model, string initial = null)
    {
        fileBrowser?.Dispose();
        var browse = slides[Slide.Browse]; browse.Clear();
        fileBrowser = new SetupFilePicker(browse, ja, model, initial ?? (model ? modelPath.value : runtimePath.value), atmosphere.Accent, value =>
        {
            if (model) modelPath.value = value; else runtimePath.value = value;
            Go(Slide.Custom);
        });
        Go(Slide.Browse);
    }
    void UpdateFileSize() => ReadFileSizeAsync(modelPath.value).Forget();
    async UniTask ReadFileSizeAsync(string path)
    {
        try
        {
            var ct = destroyCancellationToken;
            long length = await System.Threading.Tasks.Task.Run(() => new FileInfo(path).Length, ct).AsUniTask().AttachExternalCancellation(ct);
            await UniTask.SwitchToMainThread();
            if (!destroyed && modelPath.value == path) size.SetValueWithoutNotify(Math.Ceiling(length / 1000000d).ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OperationCanceledException || ex is System.Security.SecurityException || ex is NotSupportedException) { }
    }
    async UniTaskVoid LoadInstalledAsync()
    {
        try
        {
            installedModels = await PlayerSetupServices.InstalledModelsAsync(destroyCancellationToken); if (destroyed) return;
            var restored = installedModels.FirstOrDefault(m => m.name.Equals(selection.Model, StringComparison.OrdinalIgnoreCase));
            if (restored != null && selection.ModelBytes <= 0)
            {
                selection.ModelBytes = restored.size;
                size.SetValueWithoutNotify(Math.Ceiling(restored.size / 1000000d).ToString(CultureInfo.InvariantCulture));
            }
            installed.choices = new List<string> { L("Choose a model", "モデルを選ぶ") }; installed.choices.AddRange(installedModels.Select(m => m.name)); installed.SetValueWithoutNotify(installed.choices[0]); UpdateSummary(); CustomVisibility();
        }
        catch (OperationCanceledException) { }
    }
    void UpdateSummary()
    {
        bool exists = installedModels.Any(m => m.name.Equals(selection.Model, StringComparison.OrdinalIgnoreCase));
        bool needsRuntime = selection.Mode == "Ollama" && string.IsNullOrEmpty(PlayerSetupServices.FindOllama()) && installedModels.Length == 0;
        string engine = selection.Mode == "Llama" ? "llama.cpp" : "Ollama";
        chosen.text = engine + " · " + selection.Model;
        root.Q<Label>("selection-name").text = engine + " · " + selection.Model;
        download.text = selection.Mode == "Llama" ? L("Use your files · no model download", "手持ちのファイルを使用・モデル取得なし") : exists ? L("Model installed · no download", "モデル導入済み・取得なし") : L("Model download: about ", "モデル取得：約") + FormatBytes(selection.ModelBytes);
        runtime.text = needsRuntime ? RuntimeRelease.CanAcquire ? "Ollama " + RuntimeRelease.Version + L(": about ", "：約") + FormatBytes(RuntimeRelease.Bytes) + L(" extra · 5 GB unpacking space", "を追加取得・展開用に5 GBの空き") : L("Install and start Ollama first. OS confirmation may be required.", "Ollamaの導入・起動が必要です。OSの確認操作が出る場合があります。") : selection.Mode == "Llama" ? "llama.cpp / GGUF" : L("Use installed Ollama", "導入済みOllamaを使用");
        source.text = selection.Mode == "Llama" ? L("No file transfer. Keep your selected files.", "ファイルの移動はしません。指定した場所に保管してください。") : L("Sources: Ollama registry / CDN", "取得先：Ollamaの配信サーバー") + (needsRuntime ? " / GitHub" : "") + "\n" + L("Allow model size + 1 GB free space. Sizes are estimates.", "モデル容量＋1 GBの空きを確保してください。容量は目安です。");
        bool externalTerms = selection.Mode == "Llama" && selection.ReviewModelTermsAtSource;
        Visible(root.Q<Button>("model-license"), !externalTerms);
        consent.text = externalTerms
            ? L("I've reviewed my model's terms at its source and the runtime license, and agree to run these files.", "モデルの入手元の利用条件と実行ソフトのライセンスを確認し、このファイルの実行に同意します。")
            : L("I've reviewed both licenses and agree to download and run this selection.", "両方の利用条件を確認し、取得と実行に同意します。");
        if (externalTerms) source.text += "\n" + L("Review your model's terms where you obtained it. Its license is not identified here.", "モデルの利用条件は入手元で確認してください。この画面ではライセンスを特定していません。");

    }
    void UpdateActions()
    {
        primary.SetEnabled(!busy && (slide != Slide.Consent || consent.value));
        primary.text = slide == Slide.Consent ? L("Agree & prepare", "同意して準備する") : slide == Slide.Result ? complete ? L("Back to game", "ゲームへ進む") : L("Review & retry", "確認して再開") : L("Continue", "続ける");
        secondary.text = busy ? L("Pause", "中断する") : slide == Slide.Welcome || slide == Slide.Result ? L("Not now", "今はやめる") : L("Back", "戻る");
        Visible(primary, slide != Slide.Progress && slide != Slide.Runtime && slide != Slide.Browse); Visible(secondary, !complete);
    }
    void Primary()
    {
        if (busy || closing) return;
        if (slide == Slide.Welcome && !runtimeChosen &&
            (((installedModels.Length > 0 || !string.IsNullOrEmpty(PlayerSetupServices.FindOllama())) && !string.IsNullOrEmpty(FindLlama())) || !(AIDrivenConfig.Instance?.IsAllaiveOllama ?? true)))
        { Go(Slide.Runtime); return; }
        if (slide == Slide.Welcome || slide == Slide.Models)
        {
            try { selection.Validate(); Go(Slide.Consent); }
            catch (ArgumentException)
            {
                error.text = L("Review the saved model's license and size before continuing.", "保存されたモデルの利用条件と容量を確認してください。");
                Go(Slide.Custom);
            }
        }
        else if (slide == Slide.Custom) { if (ApplyCustom()) Go(Slide.Consent); }
        else if (slide == Slide.Consent && consent.value) RunAsync().Forget();
        else if (slide == Slide.Result) { if (complete) FinishAsync(true).Forget(); else { consent.value = false; Go(Slide.Consent); } }
    }
    void Secondary()
    {
        if (busy) operation?.Cancel();
        else if (slide == Slide.Welcome || slide == Slide.Result) FinishAsync(false).Forget();
        else if (slide == Slide.Browse) Go(Slide.Custom);
        else Go(slide == Slide.Models || (slide == Slide.Custom && backend.index == 1) ? Slide.Runtime : slide == Slide.Custom ? Slide.Models : Slide.Welcome);
    }
    async UniTaskVoid RunAsync()
    {
        busy = true; complete = false; completion = new SetupCompletion(selection.ModelBytes);
        operation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken); Go(Slide.Progress);
        try
        {
            bool ok = await flow.RunAsync(selection, consent.value, operation.Token); if (destroyed) return;
            complete = ok; busy = false; services.Dispose(); DrainProgress();
            RenderProgress(new SetupProgress { Stage = flow.Stage, Problem = flow.Problem }, true);
            if (ok) { await UniTask.Delay(1600, ignoreTimeScale: true, cancellationToken: destroyCancellationToken); await FinishAsync(true); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { busy = false; Debug.LogWarning("Player setup could not continue: " + ex.Message); RenderProgress(new SetupProgress { Stage = SetupStage.Failed, Problem = SetupProblem.Settings }, true); }
        finally { operation?.Dispose(); operation = null; }
    }
    void RenderProgress(SetupProgress p, bool navigate)
    {
        latest = p;
        float value = completion?.Observe(p) ?? 0; progressBar.value = value; percent.text = Math.Floor(value).ToString(CultureInfo.InvariantCulture) + "%";
        statusTitle.text = p.Stage == SetupStage.Runtime ? L("Preparing the engine", "ソフトを準備しています。") : p.Stage == SetupStage.Model ? L("Preparing your AI", "AIモデルを準備しています。") : L("Checking that it works", "動作を確認しています。");
        statusCopy.text = p.Stage == SetupStage.Runtime ? L("The official download is verified before it runs.", "公式の配布物を検証してから実行します。") : p.Stage == SetupStage.Model ? L("You can pause and resume the model download.", "モデルの取得は中断・再開できます。") : L("Creating a short greeting on this PC.", "このPCで短い挨拶を生成しています。");
        stageCaption.text = p.Stage == SetupStage.Runtime ? L("1 / 3 · Runtime", "1 / 3 ・ 実行ソフト") : p.Stage == SetupStage.Model ? L("2 / 3 · Model", "2 / 3 ・ AIモデル") : L("3 / 3 · Verify & save", "3 / 3 ・ 応答確認と保存");
        bool transferring = p.Stage == SetupStage.Runtime || p.Stage == SetupStage.Model;
        if (transferring && completion?.HasDownloadBytes == true)
        {
            string item = p.Stage == SetupStage.Runtime ? L("Runtime", "ソフト") : L("Model", "モデル");
            bytes.text = item + L(" downloaded ", "取得済み ") + FormatBytes(completion.DownloadedBytes) + " / " + (completion.IsEstimate ? L("about ", "約") : "") + FormatBytes(completion.TotalBytes);
            remaining.text = item + L(" remaining ", "の残り ") + (completion.IsEstimate ? L("about ", "約") : "") + FormatBytes(completion.RemainingBytes);
        }
        else
        {
            bytes.text = selection.Mode == "Llama" || p.Detail == "installed" ? L("No download needed", "ダウンロード不要") : transferring ? L("Checking download size…", "取得量を確認しています…") : L("Downloads complete", "ファイルの準備が完了しました");
            remaining.text = "";
        }
        if (selection.Mode == "Llama" && transferring)
        { statusTitle.text = L("Checking your files", "手持ちのファイルを確認しています。"); statusCopy.text = L("Using only the runtime and model you chose.", "選んだ実行ソフトとモデルだけを使います。"); }
        if (p.Stage == SetupStage.Model && (p.Detail == "verifying sha256 digest" || p.Detail == "writing manifest"))
            statusCopy.text = L("Download complete. Checking the model files.", "取得が完了しました。モデルの整合性を確認しています。");
        if (p.Stage == SetupStage.Complete || p.Stage == SetupStage.Cancelled || p.Stage == SetupStage.Failed)
        {
            resultTitle.text = p.Stage == SetupStage.Complete ? L("You're ready.", "準備ができました。") : p.Stage == SetupStage.Cancelled ? L("Paused.", "中断しました。") : L("Let's try again.", "もう一度、準備しましょう。");
            resultCopy.text = p.Stage == SetupStage.Complete ? L("A real response was checked.\nReturning to your game…", "実際の応答を確認しました。\nゲームへ戻ります…") : p.Stage == SetupStage.Cancelled ? L("Model progress is kept.\nReview your selection to resume.", "取得済みのモデルデータは保持します。\n内容を確認して再開できます。") : ProblemText(p.Problem);
            root.Q<Label>("result-symbol").text = p.Stage == SetupStage.Complete ? "✓" : p.Stage == SetupStage.Cancelled ? "Ⅱ" : "!";
            Visible(resultBar, p.Stage == SetupStage.Complete); Visible(root.Q<Label>("completed-percent"), p.Stage == SetupStage.Complete);
            Visible(root.Q<Button>("result-model"), p.Stage != SetupStage.Complete); consent.value = false;
            if (navigate && slide != Slide.Result) Go(Slide.Result);
        }
        UpdateActions();
    }
    string ProblemText(SetupProblem problem)
    {
        switch (problem)
        {
            case SetupProblem.Space: return L("Free some disk space, then retry.\nYour previous setup is unchanged.", "空き容量を増やして再試行してください。\n以前の設定は保持しています。");
            case SetupProblem.Runtime: return L("Check your connection and OS permissions.\nIf needed, install Ollama from ollama.com/download.", "通信とOSの許可を確認してください。\n必要ならollama.com/downloadから導入できます。");
            case SetupProblem.Model: return L("Choose a model that supports local text generation.", "ローカルで文章を生成できるモデルを選んでください。");
            case SetupProblem.Verification: return L("Try a smaller model or close other apps.\nYour previous setup is unchanged.", "小さいモデルに変えるか、他のアプリを閉じてください。\n以前の設定は保持しています。");
            case SetupProblem.Settings: return L("Check write permission for the game's data folder.", "ゲームの保存先への書き込み権限を確認してください。");
            default: return L("Check your connection and retry.\nOllama resumes downloaded model data.", "ネット接続を確認して再試行してください。\n取得済みモデルの続きから再開します。");
        }
    }
    async UniTask FinishAsync(bool succeeded)
    {
        if (destroyed || closing) return; closing = true; AIDrivenInitializer.ReportSetupResult(succeeded);
        if (SceneManager.sceneCount > 1) { await SceneManager.UnloadSceneAsync(gameObject.scene); return; }
        if (succeeded) { primary.text = L("Setup complete", "セットアップ完了"); primary.SetEnabled(false); }
        else { Go(Slide.Welcome); closing = false; }
    }
    void DrainProgress()
    {
        lock (progressLock) while (pending.Count > 0) RenderProgress(pending.Dequeue(), true);
    }
    void Update()
    {
        DrainProgress(); if (root == null || current == null) return;
        root.EnableInClassList("compact", (panel.targetTexture != null ? panel.targetTexture.width : Screen.width) / panel.scale < 800 ||
            (panel.targetTexture != null ? panel.targetTexture.height : Screen.height) / panel.scale < 780);
        float time = Time.unscaledTime, age = time - changedAt;
        float t = reduced.value ? 1 : Mathf.Clamp01(age / .48f), ease = 1 - Mathf.Pow(1 - t, 3);
        // Keep text fully opaque throughout arrivals so animation cannot lower its contrast.
        current.style.opacity = 1; current.style.translate = new Translate(0, (1 - ease) * 16);
        current.style.scale = new Scale(new Vector3(.985f + .015f * ease, .985f + .015f * ease, 1));
        atmosphere.Tick(time, reduced.value);
        foreach (var icon in icons)
        {
            icon.style.translate = new Translate(0, reduced.value ? 0 : Mathf.Sin(time * 1.8f) * 3);
            icon.style.rotate = new Rotate(new Angle(reduced.value ? 0 : Mathf.Sin(time * .9f) * 2));
        }
        for (int i = 0; i < sparks.Count; i++)
        {
            float angle = i % 3 * Mathf.PI * 2 / 3 + (reduced.value ? 0 : time * .38f);
            sparks[i].style.translate = new Translate(Mathf.Cos(angle) * 65, Mathf.Sin(angle) * 43);
            sparks[i].style.opacity = reduced.value ? .65f : .55f + .3f * Mathf.Sin(time * 1.2f + i);
        }
        float settle = reduced.value ? 1 : 1 + Mathf.Sin(Mathf.Clamp01(age / .7f) * Mathf.PI) * .12f;
        resultSymbol.style.scale = new Scale(new Vector3(settle, settle, 1));
        for (int i = 0; i < celebration.Count; i++)
        {
            float burst = Mathf.Clamp01(age / 1.2f), angle = i * Mathf.PI / 4;
            celebration[i].style.translate = new Translate(Mathf.Cos(angle) * (48 + burst * 32), Mathf.Sin(angle) * (48 + burst * 16));
            celebration[i].style.opacity = !reduced.value && complete ? Mathf.Sin(burst * Mathf.PI) * .8f : 0;
        }
    }
    void OnDestroy()
    { destroyed = true; fileBrowser?.Dispose(); operation?.Cancel(); services?.Dispose(); atmosphere?.Dispose(); if (panel != null) Destroy(panel); if (font != null) Destroy(font); }
    static void OpenTerms(string url) { try { PlayerSetupPlan.ValidateHttps(url); Application.OpenURL(url); } catch (ArgumentException) { } }
    Label T(VisualElement parent, string name, string en, string jp, string css)
    { copy[name] = new[] { en, jp }; var label = Text(parent, "", css); label.name = name; return label; }
    static VisualElement Box(VisualElement parent, string css) { var item = new VisualElement(); item.AddToClassList(css); parent.Add(item); return item; }
    static Label Text(VisualElement parent, string text, string css) { var item = new Label(text); item.AddToClassList(css); parent.Add(item); return item; }
    static Button Btn(VisualElement parent, string text, Action click, string name) { var item = new Button(click) { text = text, name = name }; parent.Add(item); return item; }
    static TextField Field(VisualElement parent, string name, string value) { var item = new TextField { name = name, value = value }; parent.Add(item); return item; }
    static void Visible(VisualElement element, bool value) => element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    static string FormatBytes(long value) => value >= 1000000000 ? (value / 1000000000d).ToString("0.0", CultureInfo.InvariantCulture) + " GB" :
        value >= 1000000 ? (value / 1000000d).ToString(value < 100000000 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB" :
        value >= 1000 ? (value / 1000d).ToString("0", CultureInfo.InvariantCulture) + " KB" : Math.Max(0, value) + " B";
}
