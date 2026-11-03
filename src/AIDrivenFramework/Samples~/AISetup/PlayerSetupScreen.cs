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
using UnityEngine.UI;

/// <summary>Controls the Canvas authored in AIDrivenSetup.unity. No visual hierarchy is built at runtime.</summary>
public sealed class PlayerSetupScreen : MonoBehaviour
{
    public enum Slide { Welcome, Runtime, Models, Custom, Browse, Consent, Progress, Result }
    enum ListPurpose { Ollama, GgufInfo, Installed }
    [Serializable] public sealed class ModelSlot
    {
        public Button Button;
        public Text Tier, Name, Capacity;
        public Image Surface;
    }
    [Header("Scene-authored screens")]
    public Canvas Canvas;
    public GameObject[] Slides;
    public SetupCanvasLabel[] Labels;
    public SetupCanvasMotion Motion;
    public SetupFilePicker Browser;
    public Image SetupStageFill;
    public Text SetupStageCaption;
    [Header("Navigation")]
    public Button PrimaryButton, SecondaryButton, JapaneseButton, EnglishButton, ChangeModelButton;
    public Text PrimaryCaption, SecondaryCaption;
    public Sprite PrimaryGradient, ButtonShape;
    public Toggle ConsentToggle, ReduceMotionToggle;
    [Header("Runtime and model choices")]
    public Button OllamaButton, LlamaButton, CustomModelButton, InstalledModelsButton;
    public Button PreviousModelsButton, NextModelsButton, OtherGgufButton;
    public Text ModelsTitle, ModelsCopy, ModelsPage;
    public ModelSlot[] ModelSlots;
    [Header("Custom model form")]
    public GameObject OllamaFields, LlamaFields;
    public InputField ModelTag, ModelSize, RuntimePath, ModelPath;
    public Button BrowseRuntimeButton, BrowseModelButton, GetRuntimeButton, GetModelButton, ModelInfoButton;
    public Text CustomTitle, CustomCopy, BackendStatus, ModelInfoCaption, ModelTermsNote, ErrorText;
    [Header("Selection review")]
    public Text WelcomeSelection, ReviewName, ReviewDownload, ReviewRuntime, ReviewSource, ConsentCaption;
    public Button ModelLicenseButton, RuntimeLicenseButton;
    public GameObject ModelTermsPanel;
    public Text ModelTermsBody, ModelTermsTitle;
    public Button CloseModelTermsButton;
    public ScrollRect ModelTermsScroll;
    public Button PreviousTermsButton, NextTermsButton;
    public Text TermsPageCaption;
    public Func<string, CancellationToken, UniTask<string>> ModelLicenseReader { get; set; }
    [Header("Preparation and result")]
    public Text StatusTitle, StatusCopy, StageCaption, Percent, DownloadedBytes, RemainingBytes;
    public Image ProgressFill, ResultFill;
    public Text ResultTitle, ResultCopy, ResultSymbol, ResultPercent;
    public Button ResultModelButton;

    readonly Queue<SetupProgress> pending = new Queue<SetupProgress>();
    readonly object progressLock = new object();
    IPlayerSetupServices services;
    PlayerSetupFlow flow;
    PlayerSetupPlan selection;
    InstalledModel[] installedModels = Array.Empty<InstalledModel>();
    SetupCompletion completion;
    CancellationTokenSource operation;
    CancellationTokenSource termsRequest;
    int termsGeneration;
    bool termsLoading;
    int termsPage;
    Slide slide;
    ListPurpose listPurpose;
    int modelPage, ggufIndex = -1;
    bool ja, llama, runtimeChosen, busy, complete, closing, destroyed;
    public Slide CurrentSlide => slide;
    public PlayerSetupFlow Flow => flow;
    public bool IsJapanese => ja;
    string L(string en, string jp) => ja ? jp : en;

    void Awake()
    {
        if (Canvas == null || Slides == null || Slides.Length != 8 || PrimaryButton == null || Browser == null || ModelTermsPanel == null)
        { Debug.LogError("Open the authored AIDrivenSetup scene. Canvas references are required."); enabled = false; return; }
        ja = PlayerPrefs.GetString("AIDrivenFW.Setup.Language", Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en") == "ja";
        selection = PlayerSetupPlan.Choices().FirstOrDefault() ?? new PlayerSetupPlan { Model = "", ModelSource = "", ModelTerms = "", ModelBytes = 0 };
        var journal = SetupJournal.Load();
        if (journal?.Revision == ModelInfo.CurrentConsentRevision && journal.Plan != null)
        { try { journal.Plan.Validate(); selection = journal.Plan; } catch (ArgumentException) { } }
        else
        {
            var saved = ModelInfo.LoadFromFile();
            var known = PlayerSetupPlan.Choices().FirstOrDefault(p => p.Model.Equals(saved?.Name, StringComparison.OrdinalIgnoreCase));
            if (known != null && saved.Mode == "Ollama") selection = known;
            else if (!string.IsNullOrWhiteSpace(saved?.Name)) selection = new PlayerSetupPlan
            {
                Model = saved.Name, Mode = saved.Mode, RuntimePath = saved.RuntimePath, ModelPath = saved.ModelPath,
                ModelTerms = saved.ModelTermsUrl, RuntimeTerms = saved.RuntimeTermsUrl, ModelBytes = 0,
                ModelSource = PlayerSetupPlan.OllamaModelPage(saved.Name), ReviewModelTermsAtSource = saved.LocalModelTermsReviewed
            };
        }
        selection.ModelTermsText = "";
        llama = selection.Mode == "Llama";
        ModelTag.SetTextWithoutNotify(selection.Model);
        ModelSize.SetTextWithoutNotify(Math.Ceiling(selection.ModelBytes / 1000000d).ToString(CultureInfo.InvariantCulture));
        RuntimePath.SetTextWithoutNotify(selection.RuntimePath); ModelPath.SetTextWithoutNotify(selection.ModelPath);
        ReduceMotionToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("AIDrivenFW.Setup.ReduceMotion", 0) == 1);
        SetServices(new PlayerSetupServices());
        PrimaryButton.onClick.AddListener(Primary); SecondaryButton.onClick.AddListener(Secondary);
        JapaneseButton.onClick.AddListener(() => SetLanguage(true)); EnglishButton.onClick.AddListener(() => SetLanguage(false));
        ChangeModelButton.onClick.AddListener(() => Go(Slide.Runtime));
        OllamaButton.onClick.AddListener(() => { runtimeChosen = true; llama = false; if (selection.Mode != "Ollama") Select(PlayerSetupPlan.Choices().FirstOrDefault() ?? new PlayerSetupPlan { Model = "", ModelBytes = 0, ModelTerms = "" }); OpenModels(ListPurpose.Ollama); });
        LlamaButton.onClick.AddListener(() => { runtimeChosen = true; llama = true; if (string.IsNullOrEmpty(RuntimePath.text)) RuntimePath.SetTextWithoutNotify(FindLlama()); Go(Slide.Custom); });
        CustomModelButton.onClick.AddListener(() => { llama = false; Go(Slide.Custom); });
        InstalledModelsButton.onClick.AddListener(() => OpenModels(ListPurpose.Installed));
        ModelInfoButton.onClick.AddListener(() => OpenModels(ListPurpose.GgufInfo));
        OtherGgufButton.onClick.AddListener(() => { ggufIndex = -1; ConsentToggle.SetIsOnWithoutNotify(false); Go(Slide.Custom); });
        PreviousModelsButton.onClick.AddListener(() => { modelPage = Math.Max(0, modelPage - 1); RefreshModels(); });
        NextModelsButton.onClick.AddListener(() => { modelPage++; RefreshModels(); });
        for (int i = 0; i < ModelSlots.Length; i++) { int slot = i; ModelSlots[i].Button.onClick.AddListener(() => SelectSlot(slot)); }
        BrowseRuntimeButton.onClick.AddListener(() => OpenBrowser(false)); BrowseModelButton.onClick.AddListener(() => OpenBrowser(true));
        GetRuntimeButton.onClick.AddListener(() => OpenTerms("https://github.com/ggml-org/llama.cpp/releases"));
        GetModelButton.onClick.AddListener(() => { var entry = GgufCatalog().FirstOrDefault(); if (entry != null) OpenTerms(entry.DownloadUrl); });
        ModelLicenseButton.onClick.AddListener(OpenModelTerms); RuntimeLicenseButton.onClick.AddListener(() => OpenTerms(selection.RuntimeTerms));
        CloseModelTermsButton.onClick.AddListener(() => { ModelTermsPanel.SetActive(false); Slides[(int)slide].SetActive(true); UpdateActions(); });
        PreviousTermsButton.onClick.AddListener(() => { termsPage--; RenderModelTerms(); });
        NextTermsButton.onClick.AddListener(() => { termsPage++; RenderModelTerms(); });
        ResultModelButton.onClick.AddListener(() => OpenModels(ListPurpose.Ollama));
        ConsentToggle.onValueChanged.AddListener(_ => UpdateActions());
        ReduceMotionToggle.onValueChanged.AddListener(value => { PlayerPrefs.SetInt("AIDrivenFW.Setup.ReduceMotion", value ? 1 : 0); PlayerPrefs.Save(); });
        foreach (var field in new[] { ModelTag, ModelSize, RuntimePath, ModelPath }) field.onValueChanged.AddListener(_ => { ConsentToggle.SetIsOnWithoutNotify(false); UpdateActions(); });
        ModelTag.onValueChanged.AddListener(_ => RefreshCustom());
        ModelPath.onValueChanged.AddListener(value => { ggufIndex = -1; ReadFileSizeAsync(value).Forget(); RefreshCustom(); });
        RefreshLanguage(); Go(Slide.Welcome); LoadInstalledAsync().Forget();
    }
    /// <summary>Allows games and deterministic validation to supply another setup service.</summary>
    public void SetServices(IPlayerSetupServices provider)
    {
        services?.Dispose(); services = provider ?? throw new ArgumentNullException(nameof(provider)); flow = new PlayerSetupFlow(provider);
        flow.Changed += progress => { lock (progressLock) pending.Enqueue(progress); };
    }
    public void SetLanguage(bool japanese)
    { ja = japanese; PlayerPrefs.SetString("AIDrivenFW.Setup.Language", ja ? "ja" : "en"); PlayerPrefs.Save(); RefreshLanguage(); }
    void RefreshLanguage()
    {
        foreach (var label in Labels) if (label != null) label.Apply(ja);
        OllamaButton.interactable = AIDrivenConfig.Instance?.IsAllaiveOllama ?? true;
        RefreshCustom(); RefreshModels(); UpdateSummary(); UpdateActions();
        if (slide == Slide.Browse) Browser.SetLanguage(ja);
        if (ModelTermsPanel.activeSelf) ModelTermsTitle.text = L("Model terms · ", "モデルの利用条件 ・ ") + selection.Model;
    }
    public void ShowEditorPreview(Slide value, bool japanese)
    {
        for (int i = 0; i < Slides.Length; i++) if (Slides[i] != null) Slides[i].SetActive(i == (int)value);
        foreach (var label in Labels) if (label != null) label.Apply(japanese);
    }
    public void Go(Slide value)
    {
        termsGeneration++; termsRequest?.Cancel(); termsRequest?.Dispose(); termsRequest = null; termsLoading = false;
        ModelTermsPanel.SetActive(false);
        if (slide == Slide.Browse && value != Slide.Browse) Browser.Close();
        slide = value;
        for (int i = 0; i < Slides.Length; i++) Slides[i].SetActive(i == (int)value);
        if (value == Slide.Custom) RefreshCustom();
        if (value == Slide.Models) RefreshModels();
        UpdateActions(); Motion.Arrive(Slides[(int)value].GetComponent<RectTransform>(), complete && value == Slide.Result);
        if (value == Slide.Consent && selection.Mode == "Ollama" && string.IsNullOrWhiteSpace(selection.ModelTerms)) ReadModelTermsAsync().Forget();
    }
    async UniTaskVoid ReadModelTermsAsync()
    {
        int generation = termsGeneration; string tag = selection.Model;
        termsRequest = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        var request = termsRequest; termsLoading = true; selection.ModelTermsText = ""; ConsentToggle.SetIsOnWithoutNotify(false); UpdateSummary(); UpdateActions();
        try
        {
            string text = await (ModelLicenseReader ?? PlayerSetupServices.ModelLicenseAsync)(tag, request.Token).AttachExternalCancellation(request.Token);
            if (!destroyed && generation == termsGeneration && selection.Model == tag) selection.ModelTermsText = text ?? "";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Missing/unavailable metadata falls back to explicit manual review. */ }
        finally
        {
            if (!destroyed && generation == termsGeneration) { termsLoading = false; UpdateSummary(); UpdateActions(); }
        }
    }
    void OpenModelTerms()
    {
        if (!string.IsNullOrWhiteSpace(selection.ModelTermsText))
        {
            ModelTermsTitle.text = L("Model terms · ", "モデルの利用条件 ・ ") + selection.Model;
            foreach (var panel in Slides) panel.SetActive(false); ModelTermsPanel.SetActive(true);
            termsPage = 0; RenderModelTerms(); UpdateActions();
        }
        else if (!selection.ReviewModelTermsAtSource) OpenTerms(selection.ModelTerms);
    }
    void RenderModelTerms()
    {
        const int chunk = 2200;
        string text = selection.ModelTermsText; int count = Math.Max(1, (text.Length + chunk - 1) / chunk);
        termsPage = Mathf.Clamp(termsPage, 0, count - 1);
        int start = termsPage * chunk, end = Math.Min(text.Length, start + chunk);
        if (start > 0 && char.IsLowSurrogate(text[start])) start--;
        if (end < text.Length && char.IsLowSurrogate(text[end])) end--;
        ModelTermsBody.text = text.Substring(start, end - start);
        TermsPageCaption.text = (termsPage + 1) + " / " + count;
        PreviousTermsButton.interactable = termsPage > 0; NextTermsButton.interactable = termsPage + 1 < count;
        Canvas.ForceUpdateCanvases(); ModelTermsScroll.verticalNormalizedPosition = 1;
    }
    void OpenModels(ListPurpose purpose) { listPurpose = purpose; modelPage = 0; Go(Slide.Models); }
    void RefreshModels()
    {
        if (ModelSlots == null || ModelSlots.Length == 0) return;
        int count = listPurpose == ListPurpose.GgufInfo ? GgufCatalog().Length : listPurpose == ListPurpose.Installed ? installedModels.Length : PlayerSetupPlan.Choices().Length;
        modelPage = Mathf.Clamp(modelPage, 0, Math.Max(0, (count - 1) / ModelSlots.Length));
        ModelsTitle.text = listPurpose == ListPurpose.GgufInfo ? L("Identify your model", "モデル情報を選ぶ") : listPurpose == ListPurpose.Installed ? L("Models on this PC", "導入済みモデルを選ぶ") : L("Choose your AI", "AIモデルを選ぶ");
        ModelsCopy.text = count == 0 ? L("No models are listed. Use another local model.", "登録モデルがありません。別のローカルモデルを指定できます。") : listPurpose == ListPurpose.GgufInfo ? L("Choose its entry only if it matches your file.", "選んだファイルに対応する情報を選んでください。") : L("Start small. You can choose a different model.", "まずは軽いモデルから。自由に選べます。");
        for (int i = 0; i < ModelSlots.Length; i++)
        {
            int index = modelPage * ModelSlots.Length + i; var slot = ModelSlots[i]; slot.Button.gameObject.SetActive(index < count); if (index >= count) continue;
            string name; long bytes; ModelLevel level;
            if (listPurpose == ListPurpose.Installed) { name = installedModels[index].name; bytes = installedModels[index].size; level = ModelLevel.Light; }
            else if (listPurpose == ListPurpose.GgufInfo) { var e = GgufCatalog()[index]; name = e.ModelName; bytes = e.EstimatedDownloadBytes; level = e.Level; }
            else { var e = PlayerSetupPlan.Choices()[index]; name = e.Model; bytes = e.ModelBytes; level = e.Level; }
            slot.Tier.text = listPurpose == ListPurpose.Installed ? L("Installed", "導入済み") : level == ModelLevel.Light ? L("Lightweight", "ライト") : level == ModelLevel.Balanced ? L("Medium", "ミディアム") : L("Large", "ラージ");
            slot.Name.text = name; slot.Capacity.text = bytes > 0 ? FormatBytes(bytes) : L("Review size", "容量を確認");
            bool selected = listPurpose == ListPurpose.Ollama && selection != null && selection.Mode == "Ollama" && selection.Model == name;
            var colors = slot.Button.colors; colors.normalColor = selected ? new Color(.933f, .957f, .988f) : Color.white; slot.Button.colors = colors;
        }
        PreviousModelsButton.gameObject.SetActive(modelPage > 0); NextModelsButton.gameObject.SetActive((modelPage + 1) * ModelSlots.Length < count);
        ModelsPage.text = count > ModelSlots.Length ? (modelPage + 1) + " / " + Math.Max(1, (count + ModelSlots.Length - 1) / ModelSlots.Length) : "";
        CustomModelButton.gameObject.SetActive(listPurpose != ListPurpose.GgufInfo); OtherGgufButton.gameObject.SetActive(listPurpose == ListPurpose.GgufInfo);
    }
    void SelectSlot(int slot)
    {
        int i = modelPage * ModelSlots.Length + slot;
        if (listPurpose == ListPurpose.GgufInfo) { if (i >= GgufCatalog().Length) return; ggufIndex = i; ConsentToggle.SetIsOnWithoutNotify(false); Go(Slide.Custom); }
        else if (listPurpose == ListPurpose.Installed)
        {
            if (i >= installedModels.Length) return; llama = false; ModelTag.SetTextWithoutNotify(installedModels[i].name); ModelSize.SetTextWithoutNotify(Math.Ceiling(installedModels[i].size / 1000000d).ToString(CultureInfo.InvariantCulture)); Go(Slide.Custom);
        }
        else { var choices = PlayerSetupPlan.Choices(); if (i < choices.Length) Select(choices[i]); }
    }
    void Select(PlayerSetupPlan plan)
    {
        selection = plan.Snapshot(); llama = false; ConsentToggle.SetIsOnWithoutNotify(false); ErrorText.text = "";
        selection.ModelTermsText = "";
        ModelTag.SetTextWithoutNotify(selection.Model); ModelSize.SetTextWithoutNotify(Math.Ceiling(selection.ModelBytes / 1000000d).ToString(CultureInfo.InvariantCulture)); RefreshModels(); UpdateSummary(); UpdateActions();
    }
    static ModelInfoConfig[] GgufCatalog() => (AIDrivenConfig.Instance?.RecommendModelInfos ?? AIDrivenConfig.CreateRecommendedModels(false)).Where(e => e != null && !string.IsNullOrWhiteSpace(e.ModelName)).ToArray();
    string CustomTerms()
    {
        if (llama) { var e = ggufIndex >= 0 ? GgufCatalog().ElementAtOrDefault(ggufIndex) : null; return PlayerSetupPlan.ConfiguredModelTerms(e?.ModelName, e == null ? null : new[] { e }); }
        return OllamaTerms(ModelTag.text);
    }
    static string OllamaTerms(string tag) => PlayerSetupPlan.ConfiguredModelTerms(tag,
        AIDrivenConfig.Instance?.OllamaRecommendModelInfos ?? AIDrivenConfig.CreateRecommendedModels(true));
    void RefreshCustom()
    {
        OllamaFields.SetActive(!llama); LlamaFields.SetActive(llama);
        CustomTitle.text = llama ? L("Use your GGUF model", "手持ちのGGUFモデルを使う") : L("Use another model", "別のモデルを使う");
        CustomCopy.text = llama ? L("Choose two files and review their terms. No terminal needed.", "ファイルを2つ選び、利用条件を確認します。コマンド入力は不要です。") : L("Choose an installed model or enter a local model tag.", "導入済みモデルを選ぶか、ローカルモデル名を指定します。");
        BackendStatus.text = llama ? L("llama.cpp / GGUF · Keep DLLs and notices together.", "llama.cpp / GGUF・DLLとライセンス通知も保管してください。") : L("Ollama · automatic preparation", "Ollama・自動で準備");
        ModelInfoCaption.text = L("Model information: ", "モデル情報：") + (ggufIndex >= 0 ? GgufCatalog().ElementAtOrDefault(ggufIndex)?.ModelName : L("Other GGUF", "その他のGGUF"));
        ModelTermsNote.text = string.IsNullOrWhiteSpace(CustomTerms()) ? L("No terms page is registered. Manually review the license at the model's original source.", "利用条件ページが未登録です。モデルの入手元でライセンスを手動確認してください。") : L("Terms are supplied automatically. Review them on the next screen.", "利用条件は自動で表示します。次の画面で確認してください。");
    }
    void OpenBrowser(bool model)
    {
        Go(Slide.Browse);
        Browser.Open(model, model ? ModelPath.text : RuntimePath.text, ja, value => { if (model) ModelPath.text = value; else RuntimePath.text = value; Go(Slide.Custom); });
    }
    async UniTask ReadFileSizeAsync(string path)
    {
        try
        {
            var ct = destroyCancellationToken; long length = await System.Threading.Tasks.Task.Run(() => new FileInfo(path).Length, ct).AsUniTask().AttachExternalCancellation(ct);
            await UniTask.SwitchToMainThread(); if (!destroyed && ModelPath.text == path) ModelSize.SetTextWithoutNotify(Math.Ceiling(length / 1000000d).ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is OperationCanceledException || e is System.Security.SecurityException || e is NotSupportedException) { }
    }
    async UniTaskVoid ApplyCustomAsync()
    {
        if (busy) return;
        var plan = new PlayerSetupPlan { Mode = llama ? "Llama" : "Ollama", Model = ModelTag.text.Trim(), ModelTerms = CustomTerms(), ModelSource = PlayerSetupPlan.OllamaModelPage(ModelTag.text.Trim()), RuntimePath = RuntimePath.text.Trim().Trim('"'), ModelPath = ModelPath.text.Trim().Trim('"'), RuntimeTerms = llama ? "https://github.com/ggml-org/llama.cpp/blob/master/LICENSE" : RuntimeRelease.TermsUrl };
        plan.ReviewModelTermsAtSource = string.IsNullOrWhiteSpace(plan.ModelTerms);
        plan.ModelBytes = double.TryParse(ModelSize.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0 && mb < 1000000 ? (long)(mb * 1000000) : 0;
        busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken); UpdateActions();
        ErrorText.color = new Color(.282f, .31f, .337f); ErrorText.text = L("Checking your selection…", "選んだ内容を確認しています…");
        try
        {
            var ct = operation.Token;
            await System.Threading.Tasks.Task.Run(() => { if (plan.Mode == "Llama") { plan.ModelBytes = new FileInfo(plan.ModelPath).Length; plan.Model = Path.GetFileName(plan.ModelPath); } plan.Validate(); }, ct).AsUniTask().AttachExternalCancellation(ct);
            await UniTask.SwitchToMainThread(); if (destroyed) return;
            selection = plan; ConsentToggle.SetIsOnWithoutNotify(false); ErrorText.text = ""; busy = false; UpdateSummary(); Go(Slide.Consent);
        }
        catch (OperationCanceledException) { await UniTask.SwitchToMainThread(); if (!destroyed) ErrorText.text = L("Paused. You can review and retry.", "中断しました。確認して再開できます。"); }
        catch (Exception) { await UniTask.SwitchToMainThread(); if (!destroyed) { ErrorText.color = new Color(.647f, .165f, .165f); ErrorText.text = L("Check the model, size and selected files.", "モデル名・容量・選んだファイルを確認してください。"); } }
        finally { await UniTask.SwitchToMainThread(); busy = false; operation?.Dispose(); operation = null; if (!destroyed) UpdateActions(); }
    }
    async UniTaskVoid LoadInstalledAsync()
    {
        try { installedModels = await PlayerSetupServices.InstalledModelsAsync(destroyCancellationToken); if (!destroyed) { UpdateSummary(); if (slide == Slide.Models) RefreshModels(); } }
        catch (OperationCanceledException) { }
    }
    void UpdateSummary()
    {
        if (selection == null) return;
        // Re-resolve restored/custom Ollama selections by their own tag, never a previous model's link.
        if (selection.Mode == "Ollama") { selection.ModelTerms = OllamaTerms(selection.Model); selection.ReviewModelTermsAtSource = string.IsNullOrWhiteSpace(selection.ModelTerms); }
        bool exists = installedModels.Any(m => m.name.Equals(selection.Model, StringComparison.OrdinalIgnoreCase));
        bool needsRuntime = selection.Mode == "Ollama" && string.IsNullOrEmpty(PlayerSetupServices.FindOllama()) && installedModels.Length == 0;
        string engine = selection.Mode == "Llama" ? "llama.cpp" : "Ollama";
        WelcomeSelection.text = ReviewName.text = engine + " · " + selection.Model;
        ReviewDownload.text = selection.Mode == "Llama" ? L("Use your files · no model download", "手持ちのファイルを使用・モデル取得なし") : exists ? L("Model installed · no download", "モデル導入済み・取得なし") : L("Model download: about ", "モデル取得：約") + FormatBytes(selection.ModelBytes);
        ReviewRuntime.text = selection.Mode == "Llama" ? "llama.cpp / GGUF" : needsRuntime ? "Ollama " + RuntimeRelease.Version + (RuntimeRelease.CanAcquire ? L(" · about ", "・約") + FormatBytes(RuntimeRelease.Bytes) : L(" · install from ollama.com/download", "・公式ページから導入")) : L("Use installed Ollama", "導入済みOllamaを使用");
        bool sourceReview = selection.ReviewModelTermsAtSource;
        ReviewSource.text = selection.Mode == "Llama" ? L("Keep files in their chosen locations.", "指定した場所にファイルを保管してください。") : L("Sources: Ollama registry/CDN", "取得先：Ollamaの配信サーバー") + (needsRuntime ? " / GitHub" : "") + "\n" + L("Allow model size + 1 GB free. Runtime unpacking needs 5 GB.", "モデル容量＋1 GB、ソフトの展開用に5 GBの空きを確保してください。");
        bool runtimeText = !string.IsNullOrWhiteSpace(selection.ModelTermsText);
        if (sourceReview) ReviewSource.text = termsLoading ? L("Checking this model's terms in Ollama…", "Ollamaでこのモデルの利用条件を確認しています…") : runtimeText ? L("Model terms were supplied by this PC's Ollama.\nRead the full text with the Model terms button.", "このPCのOllamaからモデルの利用条件を取得しました。\n「モデルの利用条件」で全文を確認してください。") : L("No model terms page or text is available.\nManually review the license at the model's original source before agreeing.", "モデルの利用条件ページ・本文を取得できません。\n同意する前に、モデルの入手元でライセンスを手動確認してください。");
        ModelLicenseButton.gameObject.SetActive(!termsLoading && (!sourceReview || runtimeText));
        ConsentCaption.text = sourceReview && !runtimeText ? L("I manually reviewed the model terms at its source and the runtime license, and agree to prepare and run this selection.", "モデルの入手元の利用条件と実行ソフトのライセンスを手動確認し、準備と実行に同意します。") : L("I reviewed both licenses and agree to download and run this selection.", "両方の利用条件を確認し、取得と実行に同意します。");
    }
    void UpdateActions()
    {
        int step = slide == Slide.Welcome ? 1 : slide == Slide.Consent ? 3 : slide == Slide.Progress || slide == Slide.Result ? 4 : 2;
        SetupStageFill.fillAmount = complete ? 1 : (step - 1) / 4f;
        SetupStageCaption.text = L("Setup ", "セットアップ ") + step + " / 4";
        bool showPrimary = slide != Slide.Runtime && slide != Slide.Browse && slide != Slide.Progress;
        if (slide == Slide.Models && listPurpose != ListPurpose.Ollama) showPrimary = false;
        bool viewingTerms = ModelTermsPanel.activeSelf;
        PrimaryButton.gameObject.SetActive(showPrimary && !viewingTerms); SecondaryButton.gameObject.SetActive(!complete && !viewingTerms);
        ConsentToggle.interactable = !termsLoading;
        PrimaryButton.interactable = !busy && !termsLoading && (slide != Slide.Consent || ConsentToggle.isOn);
        var primarySurface = PrimaryButton.GetComponent<Image>();
        primarySurface.sprite = PrimaryButton.interactable ? PrimaryGradient : ButtonShape;
        primarySurface.type = PrimaryButton.interactable ? Image.Type.Simple : Image.Type.Sliced;
        PrimaryCaption.text = slide == Slide.Consent ? L("Agree & prepare", "同意して準備する") : slide == Slide.Result ? complete ? L("Back to game", "ゲームへ進む") : L("Review & retry", "確認して再開") : L("Continue", "続ける");
        PrimaryCaption.color = PrimaryButton.interactable ? Color.white : new Color(.227f, .275f, .322f);
        SecondaryCaption.text = busy ? L("Pause", "中断する") : slide == Slide.Welcome || slide == Slide.Result ? L("Not now", "今はやめる") : L("Back", "戻る");
    }
    void Primary()
    {
        if (busy || closing) return;
        if (slide == Slide.Welcome && !runtimeChosen && (((installedModels.Length > 0 || !string.IsNullOrEmpty(PlayerSetupServices.FindOllama())) && !string.IsNullOrEmpty(FindLlama())) || !(AIDrivenConfig.Instance?.IsAllaiveOllama ?? true))) { Go(Slide.Runtime); return; }
        if (slide == Slide.Welcome || slide == Slide.Models)
        { try { selection.Validate(); UpdateSummary(); Go(Slide.Consent); } catch (ArgumentException) { ErrorText.text = L("Review the model and capacity.", "モデルと容量を確認してください。"); Go(Slide.Custom); } }
        else if (slide == Slide.Custom) ApplyCustomAsync().Forget();
        else if (slide == Slide.Consent && ConsentToggle.isOn) RunAsync().Forget();
        else if (slide == Slide.Result) { if (complete) FinishAsync(true).Forget(); else { ConsentToggle.SetIsOnWithoutNotify(false); Go(Slide.Consent); } }
    }
    void Secondary()
    {
        if (busy) operation?.Cancel();
        else if (slide == Slide.Welcome || slide == Slide.Result) FinishAsync(false).Forget();
        else if (slide == Slide.Browse) Go(Slide.Custom);
        else if (slide == Slide.Models && listPurpose != ListPurpose.Ollama) Go(Slide.Custom);
        else Go(slide == Slide.Models || (slide == Slide.Custom && llama) ? Slide.Runtime : slide == Slide.Custom ? Slide.Models : Slide.Welcome);
    }
    async UniTaskVoid RunAsync()
    {
        busy = true; complete = false; completion = new SetupCompletion(selection.ModelBytes); operation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken); Go(Slide.Progress);
        try
        {
            bool ok = await flow.RunAsync(selection, ConsentToggle.isOn, operation.Token); if (destroyed) return;
            complete = ok; busy = false; services.Dispose(); DrainProgress(); RenderProgress(new SetupProgress { Stage = flow.Stage, Problem = flow.Problem });
            if (ok) { await UniTask.Delay(1600, ignoreTimeScale: true, cancellationToken: destroyCancellationToken); await FinishAsync(true); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { busy = false; Debug.LogWarning("Player setup could not continue: " + e.Message); RenderProgress(new SetupProgress { Stage = SetupStage.Failed, Problem = SetupProblem.Settings }); }
        finally { operation?.Dispose(); operation = null; }
    }
    void RenderProgress(SetupProgress p)
    {
        float value = completion?.Observe(p) ?? 0; ProgressFill.fillAmount = value / 100; Percent.text = Math.Floor(value).ToString(CultureInfo.InvariantCulture) + "%";
        StatusTitle.text = p.Stage == SetupStage.Runtime ? L("Preparing the engine", "ソフトを準備しています。") : p.Stage == SetupStage.Model ? L("Preparing your AI", "AIモデルを準備しています。") : L("Checking that it works", "動作を確認しています。");
        StatusCopy.text = p.Stage == SetupStage.Runtime ? L("Official downloads are verified before execution.", "公式の配布物を検証してから実行します。") : p.Stage == SetupStage.Model ? L("You can pause and resume the model download.", "モデルの取得は中断・再開できます。") : L("Creating a short greeting on this PC.", "このPCで短い挨拶を生成しています。");
        StageCaption.text = p.Stage == SetupStage.Runtime ? L("1 / 3 · Runtime", "1 / 3 ・ 実行ソフト") : p.Stage == SetupStage.Model ? L("2 / 3 · Model", "2 / 3 ・ AIモデル") : L("3 / 3 · Verify & save", "3 / 3 ・ 応答確認と保存");
        bool transferring = p.Stage == SetupStage.Runtime || p.Stage == SetupStage.Model;
        if (transferring && completion?.HasDownloadBytes == true)
        {
            string item = p.Stage == SetupStage.Runtime ? L("Runtime", "ソフト") : L("Model", "モデル");
            DownloadedBytes.text = item + L(" downloaded ", "取得済み ") + FormatBytes(completion.DownloadedBytes) + " / " + (completion.IsEstimate ? L("about ", "約") : "") + FormatBytes(completion.TotalBytes);
            RemainingBytes.text = item + L(" remaining ", "の残り ") + (completion.IsEstimate ? L("about ", "約") : "") + FormatBytes(completion.RemainingBytes);
        }
        else { DownloadedBytes.text = selection.Mode == "Llama" || p.Detail == "installed" ? L("No download needed", "ダウンロード不要") : transferring ? L("Checking download size…", "取得量を確認しています…") : L("Downloads complete", "ファイルの準備が完了しました"); RemainingBytes.text = ""; }
        if (selection.Mode == "Llama" && transferring) { StatusTitle.text = L("Checking your files", "手持ちのファイルを確認しています。"); StatusCopy.text = L("Using only your selected runtime and model.", "選んだ実行ソフトとモデルだけを使います。"); }
        if (p.Stage == SetupStage.Model && (p.Detail == "verifying sha256 digest" || p.Detail == "writing manifest")) StatusCopy.text = L("Download complete. Checking integrity.", "取得が完了しました。モデルの整合性を確認しています。");
        if (p.Stage == SetupStage.Complete || p.Stage == SetupStage.Cancelled || p.Stage == SetupStage.Failed)
        {
            ResultTitle.text = p.Stage == SetupStage.Complete ? L("You're ready.", "準備ができました。") : p.Stage == SetupStage.Cancelled ? L("Paused.", "中断しました。") : L("Let's try again.", "もう一度、準備しましょう。");
            ResultCopy.text = p.Stage == SetupStage.Complete ? L("A real response was checked. Returning to your game…", "実際の応答を確認しました。ゲームへ戻ります…") : p.Stage == SetupStage.Cancelled ? L("Downloaded model data is kept. Review your selection to resume.", "取得済みのモデルデータは保持します。内容を確認して再開できます。") : ProblemText(p.Problem);
            ResultSymbol.text = p.Stage == SetupStage.Complete ? "✓" : p.Stage == SetupStage.Cancelled ? "Ⅱ" : "!";
            ResultFill.transform.parent.gameObject.SetActive(p.Stage == SetupStage.Complete); ResultPercent.gameObject.SetActive(p.Stage == SetupStage.Complete); ResultModelButton.gameObject.SetActive(p.Stage != SetupStage.Complete);
            ConsentToggle.SetIsOnWithoutNotify(false); if (slide != Slide.Result) Go(Slide.Result);
        }
        UpdateActions();
    }
    string ProblemText(SetupProblem p)
    {
        switch (p)
        {
            case SetupProblem.Space: return L("Free some disk space, then retry. Previous settings are kept.", "空き容量を増やして再試行してください。以前の設定は保持しています。");
            case SetupProblem.Runtime: return L("Check connection/permissions. Install Ollama from ollama.com/download if needed.", "通信とOSの許可を確認してください。必要ならOllama公式ページから導入できます。");
            case SetupProblem.Model: return L("Choose a model for local text generation.", "ローカルで文章を生成できるモデルを選んでください。");
            case SetupProblem.Verification: return L("Try a smaller model or close other apps. Previous settings are kept.", "小さいモデルに変えるか、他のアプリを閉じてください。以前の設定は保持しています。");
            case SetupProblem.Settings: return L("Check write permission for the game's data folder.", "ゲームの保存先への書き込み権限を確認してください。");
            default: return L("Check your connection and retry. Ollama resumes downloaded model data.", "ネット接続を確認して再試行してください。取得済みモデルの続きから再開します。");
        }
    }
    async UniTask FinishAsync(bool success)
    {
        if (destroyed || closing) return; closing = true; AIDrivenInitializer.ReportSetupResult(success);
        if (SceneManager.sceneCount > 1) { await SceneManager.UnloadSceneAsync(gameObject.scene); return; }
        if (success) { PrimaryCaption.text = L("Setup complete", "セットアップ完了"); PrimaryButton.interactable = false; }
        else { Go(Slide.Welcome); closing = false; }
    }
    static string FindLlama()
    {
        var saved = ModelInfo.LoadFromFile(); if (saved?.Mode == "Llama" && File.Exists(saved.RuntimePath)) return saved.RuntimePath;
        string name = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor ? "llama-server.exe" : "llama-server";
        string folder = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
        foreach (string relative in new[] { name, Path.Combine("bin", name) }) { string path = Path.Combine(folder, relative); if (File.Exists(path)) return path; }
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) { try { var path = Path.Combine(directory.Trim('"'), name); if (File.Exists(path)) return path; } catch (ArgumentException) { } }
        return "";
    }
    void DrainProgress() { lock (progressLock) while (pending.Count > 0) RenderProgress(pending.Dequeue()); }
    void Update() => DrainProgress();
    void OnDestroy() { destroyed = true; Browser?.Close(); operation?.Cancel(); termsRequest?.Cancel(); termsRequest?.Dispose(); services?.Dispose(); }
    static void OpenTerms(string url) { try { PlayerSetupPlan.ValidateHttps(url); Application.OpenURL(url); } catch (ArgumentException) { } }
    static string FormatBytes(long value) => value >= 1000000000 ? (value / 1000000000d).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : value >= 1000000 ? (value / 1000000d).ToString(value < 100000000 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB" : value >= 1000 ? (value / 1000d).ToString("0", CultureInfo.InvariantCulture) + " KB" : Math.Max(0, value) + " B";
}
