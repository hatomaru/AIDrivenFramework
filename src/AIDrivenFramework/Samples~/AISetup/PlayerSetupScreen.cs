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
    enum Slide { Welcome, Models, Custom, Consent, Progress, Result }
    PanelSettings panel;
    Font font;
    VisualElement root, content, current, cards;
    readonly Dictionary<Slide, VisualElement> slides = new Dictionary<Slide, VisualElement>();
    readonly Dictionary<string, string[]> copy = new Dictionary<string, string[]>();
    Label chosen, download, runtime, source, error, statusTitle, statusCopy, percent, bytes, resultTitle, resultCopy;
    ProgressBar progressBar, resultBar;
    Button primary, secondary;
    Toggle consent, reduced;
    DropdownField installed, backend;
    TextField tag, terms, size, runtimePath, modelPath;
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
    bool ja, busy, complete, closing, destroyed;
    float changedAt;
    public VisualElement Root => root;
    public PlayerSetupFlow Flow => flow;
    string L(string en, string jp) => ja ? jp : en;

    void Awake()
    {
        ja = PlayerPrefs.GetString("AIDrivenFW.Setup.Language", Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en") == "ja";
        selection = PlayerSetupPlan.Choices()[0];
        var journal = SetupJournal.Load();
        if (journal?.Revision == ModelInfo.CurrentConsentRevision && journal.Plan != null)
        {
            try { journal.Plan.Validate(); selection = journal.Plan; } catch (ArgumentException) { }
        }
        else
        {
            var saved = ModelInfo.LoadFromFile();
            var known = PlayerSetupPlan.Choices().FirstOrDefault(p => p.Model.Equals(saved?.Name, StringComparison.OrdinalIgnoreCase));
            if (known != null) selection = known;
            else if (!string.IsNullOrWhiteSpace(saved?.Name)) selection = new PlayerSetupPlan
            {
                Model = saved.Name, Mode = saved.Mode, RuntimePath = saved.RuntimePath, ModelPath = saved.ModelPath,
                ModelTerms = saved.ModelTermsUrl, RuntimeTerms = saved.RuntimeTermsUrl,
                ModelSource = "https://ollama.com/library/" + saved.Name, ModelBytes = 0
            };
        }
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
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "slide-scroll" }; scroll.AddToClassList("slide-scroll"); card.Add(scroll);
        scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        content = scroll.contentContainer;

        var welcome = MakeSlide(Slide.Welcome);
        var mascot = Box(welcome, "mascot"); var face = Box(mascot, "face"); Box(face, "eye"); Box(face, "eye"); Box(mascot, "smile");
        T(welcome, "title", "A little setup.\nThen play.", "このゲームに、\nあなたのAIを。", "title");
        T(welcome, "intro", "A one-time setup for AI on this PC.\nNo AI account or subscription needed.", "このPCで動くAIを、初回だけ準備します。\nAI用の登録や月額契約は不要です。", "description");
        chosen = Text(welcome, "", "chosen");
        Btn(welcome, "", () => Go(Slide.Models), "change-model").AddToClassList("link");

        var models = MakeSlide(Slide.Models);
        T(models, "models-title", "Choose your AI", "AIモデルを選ぶ", "title");
        T(models, "models-copy", "Start small. You can choose a different model.", "まずは軽いモデルから。自由に選べます。", "description");
        cards = Box(models, "model-cards");
        T(models, "memory-note", "Larger models use more memory and may respond slower.", "大きいモデルほどメモリを使い、応答が遅くなる場合があります。", "note");
        Btn(models, "", () => Go(Slide.Custom), "custom-model").AddToClassList("link");

        var custom = MakeSlide(Slide.Custom);
        T(custom, "custom-title", "Use your own model", "別のモデルを使う", "title");
        T(custom, "custom-copy", "Choose a local text model and review its license.", "ローカルの文章生成モデルと利用条件を指定します。", "description");
        installed = new DropdownField { name = "installed-models", choices = new List<string> { "…" }, index = 0 }; custom.Add(installed);
        backend = new DropdownField { name = "backend", choices = new List<string> { "Ollama", "llama.cpp / GGUF" }, index = selection.Mode == "Llama" ? 1 : 0 }; custom.Add(backend);
        tag = Field(custom, "model-tag", selection.Model); terms = Field(custom, "model-terms", selection.ModelTerms);
        size = Field(custom, "model-size", Math.Ceiling(selection.ModelBytes / 1000000d).ToString(CultureInfo.InvariantCulture));
        runtimePath = Field(custom, "runtime-path", selection.RuntimePath); modelPath = Field(custom, "gguf-path", selection.ModelPath);
        T(custom, "file-note", "For GGUF, keep the selected files in their current locations.", "GGUFの場合、指定したファイルは現在の場所に保管してください。", "note");
        error = Text(custom, "", "error");
        backend.RegisterValueChangedCallback(_ => { consent.value = false; CustomVisibility(); });
        foreach (var field in new[] { tag, terms, size, runtimePath, modelPath }) field.RegisterValueChangedCallback(_ => consent.value = false);
        installed.RegisterValueChangedCallback(e =>
        {
            var model = installedModels.FirstOrDefault(m => m.name == e.newValue); if (model == null) return;
            backend.SetValueWithoutNotify("Ollama"); tag.SetValueWithoutNotify(model.name);
            size.SetValueWithoutNotify(Math.Ceiling(model.size / 1000000d).ToString(CultureInfo.InvariantCulture));
            terms.SetValueWithoutNotify(PlayerSetupPlan.Choices().FirstOrDefault(p => p.Model.Equals(model.name, StringComparison.OrdinalIgnoreCase))?.ModelTerms ?? "");
            consent.value = false; CustomVisibility();
        });

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
        statusTitle = Text(progress, "", "title"); statusTitle.name = "status-title";
        statusCopy = Text(progress, "", "description");
        progressBar = new ProgressBar { name = "overall-progress", lowValue = 0, highValue = 100, value = 0 }; progress.Add(progressBar);
        percent = Text(progress, "0%", "percent"); bytes = Text(progress, "", "note");
        T(progress, "progress-note", "Progress reflects completed steps and download data.\nThe first response can take a little longer.", "完了した工程と取得量からの目安です。\n最初の応答確認には時間がかかる場合があります。", "note");

        var result = MakeSlide(Slide.Result);
        Text(result, "", "result-symbol").name = "result-symbol";
        resultTitle = Text(result, "", "title"); resultCopy = Text(result, "", "description");
        resultBar = new ProgressBar { name = "completed-progress", lowValue = 0, highValue = 100, value = 100 }; result.Add(resultBar);
        Text(result, "100%", "percent").name = "completed-percent";
        Btn(result, "", () => Go(Slide.Models), "result-model").AddToClassList("link");
        var actions = Box(card, "actions"); secondary = Btn(actions, "", Secondary, "secondary"); primary = Btn(actions, "", Primary, "primary"); primary.AddToClassList("primary");
        var footer = Box(root, "footer"); reduced = new Toggle { name = "reduce-motion", value = PlayerPrefs.GetInt("AIDrivenFW.Setup.ReduceMotion", 0) == 1 }; footer.Add(reduced);
        reduced.RegisterValueChangedCallback(e => { PlayerPrefs.SetInt("AIDrivenFW.Setup.ReduceMotion", e.newValue ? 1 : 0); PlayerPrefs.Save(); });
        CustomVisibility();
    }

    VisualElement MakeSlide(Slide value)
    { var element = Box(content, "slide"); element.name = "slide-" + value; slides[value] = element; return element; }
    void Go(Slide value)
    {
        slide = value; current = slides[value];
        foreach (var pair in slides) Visible(pair.Value, pair.Key == value);
        root.Q<ScrollView>("slide-scroll").scrollOffset = Vector2.zero;
        changedAt = Time.unscaledTime; UpdateActions();
        // Reused controls can be reached naturally with the keyboard after each transition.
        if (value == Slide.Consent) consent.Focus(); else primary.Focus();
    }
    void SetLanguage(bool value)
    { ja = value; PlayerPrefs.SetString("AIDrivenFW.Setup.Language", ja ? "ja" : "en"); PlayerPrefs.Save(); RefreshLanguage(); }
    void RefreshLanguage()
    {
        foreach (var pair in copy) root.Q<Label>(pair.Key).text = pair.Value[ja ? 1 : 0];
        root.Q<Button>("language-ja").EnableInClassList("active-language", ja); root.Q<Button>("language-en").EnableInClassList("active-language", !ja);
        root.Q<Button>("change-model").text = L("Change model", "モデルを変更"); root.Q<Button>("custom-model").text = L("Use another model or my files", "別のモデル・手持ちのファイルを使う");
        root.Q<Button>("result-model").text = L("Choose another model", "モデルを選び直す");
        root.Q<Button>("model-license").text = L("Model license ↗", "モデルの利用条件 ↗"); root.Q<Button>("runtime-license").text = L("Runtime license ↗", "実行ソフトの利用条件 ↗");
        installed.label = L("Installed on this PC", "導入済みモデル"); backend.label = L("Runtime", "実行方法");
        tag.label = L("Ollama model tag", "Ollamaモデル名"); terms.label = L("Model license URL (HTTPS)", "ライセンスURL（HTTPS）"); size.label = L("Estimated download (MB)", "取得容量の目安（MB）");
        runtimePath.label = L("llama-server file", "llama-serverファイル"); modelPath.label = L("GGUF model file", "GGUFモデルファイル");
        consent.text = L("I've reviewed both licenses and agree to download and run this selection.", "両方の利用条件を確認し、取得と実行に同意します。");
        reduced.text = L("Reduce motion", "動きを減らす");
        BuildCards(); UpdateSummary(); UpdateActions(); if (latest != null) RenderProgress(latest, false);
    }
    void BuildCards()
    {
        cards.Clear(); var catalog = PlayerSetupPlan.Choices();
        for (int i = 0; i < catalog.Length; i++)
        {
            int index = i; var button = Btn(cards, "", () => Select(catalog[index]), "model-" + i); button.AddToClassList("model-card");
            var labels = Box(button, "model-labels");
            Text(labels, i == 0 ? L("Lightweight · recommended", "ライト・おすすめ") : i == 1 ? L("Medium", "ミディアム") : L("Large", "ラージ"), "model-tier");
            Text(labels, catalog[i].Model, "note"); Text(button, FormatBytes(catalog[i].ModelBytes), "model-size");
            button.EnableInClassList("selected", selection.Mode == "Ollama" && selection.Model.Equals(catalog[i].Model, StringComparison.OrdinalIgnoreCase));
        }
    }
    void Select(PlayerSetupPlan plan)
    {
        selection = plan.Snapshot(); consent.value = false; error.text = "";
        tag.SetValueWithoutNotify(selection.Model); terms.SetValueWithoutNotify(selection.ModelTerms); size.SetValueWithoutNotify(Math.Ceiling(selection.ModelBytes / 1000000d).ToString(CultureInfo.InvariantCulture)); backend.SetValueWithoutNotify("Ollama");
        CustomVisibility(); BuildCards(); UpdateSummary();
    }
    bool ApplyCustom()
    {
        var plan = new PlayerSetupPlan { Model = tag.value.Trim(), Mode = backend.index == 1 ? "Llama" : "Ollama", ModelTerms = terms.value.Trim(),
            ModelSource = "https://ollama.com/library/" + tag.value.Trim(), RuntimePath = runtimePath.value.Trim().Trim('"'), ModelPath = modelPath.value.Trim().Trim('"'),
            RuntimeTerms = backend.index == 1 ? "https://github.com/ggml-org/llama.cpp/blob/master/LICENSE" : RuntimeRelease.TermsUrl };
        plan.ModelBytes = double.TryParse(size.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0 && mb < 1000000 ? (long)(mb * 1000000) : 0;
        try
        {
            if (plan.Mode == "Llama" && File.Exists(plan.ModelPath)) { plan.ModelBytes = new FileInfo(plan.ModelPath).Length; plan.Model = Path.GetFileName(plan.ModelPath); }
            plan.Validate(); selection = plan; consent.value = false; error.text = ""; BuildCards(); UpdateSummary(); return true;
        }
        catch (Exception) { error.text = L("Check the model, HTTPS license link, size and file paths.", "モデル名・HTTPSのライセンスURL・容量・ファイルを確認してください。"); return false; }
    }
    void CustomVisibility()
    { bool gguf = backend.index == 1; Visible(runtimePath, gguf); Visible(modelPath, gguf); Visible(tag, !gguf); Visible(size, !gguf); Visible(installed, !gguf); }
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
            installed.choices = new List<string> { L("Choose a model", "モデルを選ぶ") }; installed.choices.AddRange(installedModels.Select(m => m.name)); installed.SetValueWithoutNotify(installed.choices[0]); UpdateSummary();
        }
        catch (OperationCanceledException) { }
    }
    void UpdateSummary()
    {
        bool exists = installedModels.Any(m => m.name.Equals(selection.Model, StringComparison.OrdinalIgnoreCase));
        bool needsRuntime = selection.Mode == "Ollama" && string.IsNullOrEmpty(PlayerSetupServices.FindOllama()) && installedModels.Length == 0;
        chosen.text = selection.Model + L(" · runs on this PC", "・このPCで動作");
        root.Q<Label>("selection-name").text = selection.Model;
        download.text = selection.Mode == "Llama" ? L("Use your files · no model download", "手持ちのファイルを使用・モデル取得なし") : exists ? L("Model installed · no download", "モデル導入済み・取得なし") : L("Model download: about ", "モデル取得：約") + FormatBytes(selection.ModelBytes);
        runtime.text = needsRuntime ? RuntimeRelease.CanAcquire ? "Ollama " + RuntimeRelease.Version + L(": about ", "：約") + FormatBytes(RuntimeRelease.Bytes) + L(" extra · 5 GB unpacking space", "を追加取得・展開用に5 GBの空き") : L("Install and start Ollama first. OS confirmation may be required.", "Ollamaの導入・起動が必要です。OSの確認操作が出る場合があります。") : selection.Mode == "Llama" ? "llama.cpp / GGUF" : L("Use installed Ollama", "導入済みOllamaを使用");
        source.text = selection.Mode == "Llama" ? L("No file transfer. Keep your selected files.", "ファイルの移動はしません。指定した場所に保管してください。") : L("Sources: Ollama registry / CDN", "取得先：Ollamaの配信サーバー") + (needsRuntime ? " / GitHub" : "") + "\n" + L("Allow model size + 1 GB free space. Sizes are estimates.", "モデル容量＋1 GBの空きを確保してください。容量は目安です。");
    }
    void UpdateActions()
    {
        primary.SetEnabled(!busy && (slide != Slide.Consent || consent.value));
        primary.text = slide == Slide.Consent ? L("Agree & prepare", "同意して準備する") : slide == Slide.Result ? complete ? L("Back to game", "ゲームへ進む") : L("Review & retry", "確認して再開") : L("Continue", "続ける");
        secondary.text = busy ? L("Pause", "中断する") : slide == Slide.Welcome || slide == Slide.Result ? L("Not now", "今はやめる") : L("Back", "戻る");
        Visible(primary, slide != Slide.Progress); Visible(secondary, !complete);
    }
    void Primary()
    {
        if (busy || closing) return;
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
        else Go(slide == Slide.Custom ? Slide.Models : Slide.Welcome);
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
        bytes.text = p.TotalBytes > 0 ? FormatBytes(p.CompletedBytes) + " / " + FormatBytes(p.TotalBytes) + L(" · current file", "・取得中のファイル") : "";
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
        float t = reduced.value ? 1 : Mathf.Clamp01((Time.unscaledTime - changedAt) / .22f);
        current.style.opacity = t; current.style.translate = new Translate(0, (1 - t) * 8);
    }
    void OnDestroy()
    { destroyed = true; operation?.Cancel(); services?.Dispose(); if (panel != null) Destroy(panel); if (font != null) Destroy(font); }
    static void OpenTerms(string url) { try { PlayerSetupPlan.ValidateHttps(url); Application.OpenURL(url); } catch (ArgumentException) { } }
    Label T(VisualElement parent, string name, string en, string jp, string css)
    { copy[name] = new[] { en, jp }; var label = Text(parent, "", css); label.name = name; return label; }
    static VisualElement Box(VisualElement parent, string css) { var item = new VisualElement(); item.AddToClassList(css); parent.Add(item); return item; }
    static Label Text(VisualElement parent, string text, string css) { var item = new Label(text); item.AddToClassList(css); parent.Add(item); return item; }
    static Button Btn(VisualElement parent, string text, Action click, string name) { var item = new Button(click) { text = text, name = name }; parent.Add(item); return item; }
    static TextField Field(VisualElement parent, string name, string value) { var item = new TextField { name = name, value = value }; parent.Add(item); return item; }
    static void Visible(VisualElement element, bool value) => element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    static string FormatBytes(long value) => value >= 1000000000 ? (value / 1000000000d).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : (value / 1000000d).ToString("0", CultureInfo.InvariantCulture) + " MB";
}
