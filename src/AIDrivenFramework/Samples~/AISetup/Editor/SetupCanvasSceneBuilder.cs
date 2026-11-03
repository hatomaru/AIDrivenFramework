using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Editor authoring only: saves editable Canvas objects and original sprite assets.</summary>
public static class SetupCanvasSceneBuilder
{
    const string Root = "Assets/AIDrivenFW/AISetup";
    const string Art = Root + "/Visuals";
    static Font font;
    static Sprite round, circle, accent, glass, background, hero, chip, model, folder, seal;
    static readonly Color Ink = new Color32(32, 33, 36, 255);
    static readonly Color Secondary = new Color32(72, 79, 86, 255);
    static readonly Color Blue = new Color32(0, 86, 179, 255);
    static readonly List<SetupCanvasLabel> labels = new List<SetupCanvasLabel>();
    static readonly List<RectTransform> icons = new List<RectTransform>();
    static readonly List<RectTransform> sparks = new List<RectTransform>();
    [MenuItem("Tools/AIDrivenFW/Canvas Setup/Rebuild Authored Scene")]
    static void RebuildMenu() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build(); }
    public static void Build()
    {
        Directory.CreateDirectory(Art); BakeArtwork(); labels.Clear(); icons.Clear(); sparks.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Setup Preview Camera", typeof(Camera)).GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white; camera.cullingMask = 0; camera.depth = -100;
        var go = new GameObject("AI Setup Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10000;
        var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1440, 900); scaler.matchWidthOrHeight = 1;
        var ui = go.AddComponent<PlayerSetupScreen>(); ui.Canvas = canvas; ui.PrimaryGradient = accent; ui.ButtonShape = round; var motion = go.AddComponent<SetupCanvasMotion>(); ui.Motion = motion;
        var root = go.GetComponent<RectTransform>();
        var bg = Image("Aurora Backdrop", root, background, Color.white); Stretch(bg.rectTransform, -40, -40, -40, -40); bg.raycastTarget = true; motion.Backdrop = bg.rectTransform;
        var shadow = Image("Card Shadow", root, round, new Color(.15f, .2f, .38f, .08f)); Center(shadow.rectTransform, 646, 684, 0, -6);
        var card = Image("Glass Card", root, glass, Color.white); Center(card.rectTransform, 640, 680); card.raycastTarget = true;
        var stage = Bar("Setup Steps", root, 568, 5, 0, 0, out ui.SetupStageFill); Center(stage.rectTransform, 568, 5, 0, 365);
        ui.SetupStageCaption = Text("Setup Step Caption", root, "セットアップ 1 / 4", 13, 568, 24, 0, Secondary); Center(ui.SetupStageCaption.rectTransform, 568, 24, 0, 386);
        var languages = Rect("Language", root, 210, 42); languages.anchorMin = languages.anchorMax = new Vector2(1, 1); languages.pivot = new Vector2(1, 1); languages.anchoredPosition = new Vector2(-28, -20);
        ui.JapaneseButton = Button("Japanese", languages, 94, 36, -53, 0, false); LocalCaption(ui.JapaneseButton, "日本語", "日本語", 14);
        ui.EnglishButton = Button("English", languages, 94, 36, 53, 0, false); LocalCaption(ui.EnglishButton, "English", "English", 14);
        var footer = Image("Motion Preference", root, round, Color.white); footer.rectTransform.anchorMin = footer.rectTransform.anchorMax = new Vector2(1, 0); footer.rectTransform.pivot = new Vector2(1, 0); footer.rectTransform.sizeDelta = new Vector2(196, 44); footer.rectTransform.anchoredPosition = new Vector2(-28, 20);
        ui.ReduceMotionToggle = Toggle("Reduce Motion", footer.transform, "動きを減らす", "Reduce motion", 178, 40, 14); Center(ui.ReduceMotionToggle.GetComponent<RectTransform>(), 178, 40); motion.ReduceMotion = ui.ReduceMotionToggle;
        ui.Slides = new GameObject[8];
        for (int i = 0; i < 8; i++) { var page = Rect(((PlayerSetupScreen.Slide)i) + " Slide", card.transform, 0, 0); Stretch(page, 0, 90, 0, 0); ui.Slides[i] = page.gameObject; }
        Welcome(ui); Runtime(ui); Models(ui); Custom(ui); Browser(ui); Consent(ui); Progress(ui); Result(ui, motion);
        var actions = Rect("Actions", card.transform, 568, 46); actions.anchorMin = actions.anchorMax = new Vector2(.5f, 0); actions.pivot = new Vector2(.5f, 0); actions.anchoredPosition = new Vector2(0, 26);
        var layout = actions.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.childAlignment = TextAnchor.MiddleCenter; layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = false; layout.spacing = 12;
        ui.SecondaryButton = Button("Back or Cancel", actions, 130, 46, 0, 0, false); ui.SecondaryCaption = PlainCaption(ui.SecondaryButton, "今はやめる", 16);
        ui.PrimaryButton = Button("Continue or Prepare", actions, 158, 46, 0, 0, true); ui.PrimaryCaption = PlainCaption(ui.PrimaryButton, "続ける", 16, Color.white);
        TermsViewer(ui, card.transform);
        ui.Labels = labels.ToArray(); motion.FloatingIcons = icons.ToArray(); motion.Sparks = sparks.ToArray();
        for (int i = 0; i < 8; i++) ui.Slides[i].SetActive(i == 0);
        ui.OllamaFields.SetActive(false); ui.LlamaFields.SetActive(true);
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(SetupCanvasInputBridge)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(events, scene);
        Canvas.ForceUpdateCanvases(); EditorSceneManager.SaveScene(scene, Root + "/AIDrivenSetup.unity"); AssetDatabase.SaveAssets();
        Debug.Log("CANVAS AUTHORING PASS: " + go.GetComponentsInChildren<RectTransform>(true).Length + " editable RectTransforms, 8 slides and 8 reusable file rows.");
    }
    static Transform Page(PlayerSetupScreen ui, PlayerSetupScreen.Slide slide) => ui.Slides[(int)slide].transform;
    [MenuItem("Tools/AIDrivenFW/Canvas Setup/Add Terms Viewer")]
    public static void InstallTermsViewer()
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<PlayerSetupScreen>();
        if (!ui || ui.ModelTermsPanel) return;
        font = AssetDatabase.LoadAssetAtPath<Font>(Art + "/NotoSansJP-Regular.otf"); round = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "/Rounded.png");
        labels.Clear(); TermsViewer(ui, ui.Slides[0].transform.parent);
        var combined = new List<SetupCanvasLabel>(ui.Labels); combined.AddRange(labels); ui.Labels = combined.ToArray();
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene); EditorSceneManager.SaveScene(ui.gameObject.scene);
        Debug.Log("TERMS VIEWER AUTHORING PASS: added saved Canvas controls without replacing existing layout.");
    }
    static void TermsViewer(PlayerSetupScreen ui, Transform card)
    {
        var p = Image("Model Terms Viewer", card, round, new Color32(250, 251, 254, 255)); Stretch(p.rectTransform, 0, 0, 0, 0); p.raycastTarget = true; ui.ModelTermsPanel = p.gameObject;
        ui.ModelTermsTitle = Text("Model Terms Title", p.transform, "モデルの利用条件", 23, 568, 64, 24, Ink, true);
        Copy("Source", p.transform, "Ollamaが返した本文です。内容を確認してください。", "Text supplied by Ollama. Please review it carefully.", 14, 568, 40, 90);
        var viewport = Image("Terms Viewport", p.transform, round, Color.white); Top(viewport.rectTransform, 568, 416, 142); viewport.gameObject.AddComponent<RectMask2D>(); viewport.raycastTarget = true;
        var text = Text("Terms Text", viewport.transform, "", 14, 536, 416, 0, Ink); text.alignment = TextAnchor.UpperLeft; text.supportRichText = false; text.rectTransform.pivot = new Vector2(.5f, 1); text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ui.ModelTermsBody = text; var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport.rectTransform; scroll.content = text.rectTransform; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32; ui.ModelTermsScroll = scroll;
        ui.PreviousTermsButton = Link("Previous Terms Page", p.transform, "前へ", "Previous", 108, 36, 574, -222);
        ui.NextTermsButton = Link("Next Terms Page", p.transform, "次へ", "Next", 108, 36, 574, 222);
        ui.TermsPageCaption = Text("Terms Page", p.transform, "1 / 1", 14, 240, 36, 574, Secondary);
        ui.CloseModelTermsButton = Link("Close Terms", p.transform, "内容を確認して戻る", "Back to setup", 250, 42, 622);
        p.gameObject.SetActive(false);
    }
    static void Welcome(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Welcome); Hero(p, 34);
        Copy("Title", p, "このゲームに、\nあなたのAIを。", "A little setup.\nThen play.", 32, 568, 94, 178, true);
        Copy("Introduction", p, "このPCで動くAIを、初回だけ準備します。\nAI用の登録や月額契約は不要です。", "A one-time setup for AI on this PC.\nNo AI account or subscription needed.", 17, 568, 64, 292);
        ui.WelcomeSelection = Text("Current Selection", p, "Ollama · qwen3:0.6b", 16, 568, 30, 384, Secondary);
        ui.ChangeModelButton = Link("Change Model or Runtime", p, "モデル・実行方法を変更", "Change model or runtime", 316, 42, 432);
    }
    static void Runtime(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Runtime);
        Copy("Title", p, "AIの使い方を選ぶ", "How would you like to start?", 32, 568, 64, 34, true);
        Copy("Description", p, "このPCで使う方法を選んでください。\n選んだ方だけを準備・実行します。", "Choose the way that suits you.\nOnly your selection will be prepared and run.", 17, 568, 64, 112);
        ui.OllamaButton = Choice("Ollama Automatic", p, 204, hero, "かんたん自動準備", "Easy automatic setup", "Ollama・おすすめ\nモデルを選ぶだけで準備できます。", "Ollama · recommended\nChoose a model; we'll handle preparation.");
        ui.LlamaButton = Choice("Llama Existing Files", p, 340, chip, "手持ちのモデルを使う", "Use my model files", "llama.cpp / GGUF\n実行ソフトとモデルのファイルを選びます。", "llama.cpp / GGUF\nChoose your runtime and model files.");
    }
    static void Models(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Models);
        ui.ModelsTitle = Text("Title", p, "AIモデルを選ぶ", 32, 568, 60, 24, Ink, true);
        ui.ModelsCopy = Text("Description", p, "まずは軽いモデルから。自由に選べます。", 17, 568, 54, 94, Secondary);
        ui.ModelSlots = new PlayerSetupScreen.ModelSlot[3];
        for (int i = 0; i < 3; i++)
        {
            var b = Button("Model Slot " + (i + 1), p, 568, 76, 0, 0, false); Top(b.GetComponent<RectTransform>(), 568, 76, 158 + i * 86);
            var tier = Text("Tier", b.transform, i == 0 ? "ライト・おすすめ" : i == 1 ? "ミディアム" : "ラージ", 18, 360, 26, 12, Ink, true); tier.alignment = TextAnchor.MiddleLeft; tier.rectTransform.anchoredPosition = new Vector2(-78, -12);
            var name = Text("Model Name", b.transform, i == 0 ? "qwen3:0.6b" : i == 1 ? "qwen3:1.7b" : "qwen3:4b", 14, 360, 22, 42, Secondary); name.alignment = TextAnchor.MiddleLeft; name.rectTransform.anchoredPosition = new Vector2(-78, -42);
            var size = Text("Download Size", b.transform, i == 0 ? "523 MB" : i == 1 ? "1.4 GB" : "2.5 GB", 16, 124, 32, 22, Secondary); size.rectTransform.anchoredPosition = new Vector2(204, -22);
            ui.ModelSlots[i] = new PlayerSetupScreen.ModelSlot { Button = b, Tier = tier, Name = name, Capacity = size, Surface = b.GetComponent<Image>() };
        }
        Copy("Memory Note", p, "大きいモデルほどメモリを使い、遅くなる場合があります。", "Larger models use more memory and may respond slower.", 14, 568, 32, 422);
        ui.PreviousModelsButton = Link("Previous Model Page", p, "前へ", "Previous", 90, 34, 464, -222);
        ui.NextModelsButton = Link("Next Model Page", p, "次へ", "Next", 90, 34, 464, 222);
        ui.ModelsPage = Text("Page", p, "", 14, 240, 34, 464, Secondary);
        ui.CustomModelButton = Link("Use Custom Model", p, "別のOllamaモデルを使う", "Use another Ollama model", 340, 38, 512);
        ui.OtherGgufButton = Link("Other GGUF", p, "その他のGGUF・入手元で条件を確認", "Other GGUF · review terms at source", 490, 38, 512); ui.OtherGgufButton.gameObject.SetActive(false);
    }
    static void Custom(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Custom);
        ui.CustomTitle = Text("Title", p, "手持ちのGGUFモデルを使う", 28, 568, 52, 18, Ink, true);
        ui.CustomCopy = Text("Description", p, "ファイルを2つ選び、利用条件を確認します。コマンド入力は不要です。", 15, 568, 52, 76, Secondary);
        ui.BackendStatus = Text("Runtime Information", p, "llama.cpp / GGUF・DLLとライセンス通知も保管してください。", 14, 568, 28, 130, Secondary);
        var ollama = Rect("Ollama Fields", p, 0, 0); Stretch(ollama, 0, 0, 0, 0); ui.OllamaFields = ollama.gameObject;
        ui.InstalledModelsButton = Link("Installed Models", ollama, "導入済みモデルから選ぶ", "Choose an installed model", 568, 42, 172);
        Copy("Model Tag Label", ollama, "Ollamaモデル名", "Ollama model tag", 14, 568, 22, 230, false, TextAnchor.MiddleLeft);
        ui.ModelTag = Input("Model Tag", ollama, "qwen3:0.6b", "Ollama model tag", 568, 42, 256);
        Copy("Capacity Label", ollama, "取得容量の目安（MB）", "Estimated download (MB)", 14, 568, 22, 318, false, TextAnchor.MiddleLeft);
        ui.ModelSize = Input("Model Size", ollama, "523", "MB", 568, 42, 344); ui.ModelSize.contentType = InputField.ContentType.DecimalNumber;
        var llama = Rect("Llama Files", p, 0, 0); Stretch(llama, 0, 0, 0, 0); ui.LlamaFields = llama.gameObject;
        ui.GetRuntimeButton = Link("Get Runtime", llama, "実行ソフトを入手", "Get llama.cpp", 270, 34, 166, -145);
        ui.GetModelButton = Link("Get Model", llama, "モデルの配布ページ", "Model distribution page", 270, 34, 166, 145);
        Copy("Runtime File Label", llama, "1. llama-serverファイル", "1. llama-server file", 14, 516, 22, 218, false, TextAnchor.MiddleLeft, 26);
        var ri = Image("Runtime Chip Icon", llama, chip, Color.white); Top(ri.rectTransform, 40, 40, 246, -264);
        ui.RuntimePath = Input("Runtime File Path", llama, "", "llama-server", 390, 42, 244, -41);
        ui.BrowseRuntimeButton = Link("Choose Runtime File", llama, "ファイルを選ぶ", "Choose file", 118, 42, 244, 225);
        Copy("Model File Label", llama, "2. GGUFモデルファイル", "2. GGUF model file", 14, 516, 22, 306, false, TextAnchor.MiddleLeft, 26);
        var mi = Image("Model Layers Icon", llama, model, Color.white); Top(mi.rectTransform, 40, 40, 334, -264);
        ui.ModelPath = Input("GGUF File Path", llama, "", "GGUF", 390, 42, 332, -41);
        ui.BrowseModelButton = Link("Choose GGUF File", llama, "ファイルを選ぶ", "Choose file", 118, 42, 332, 225);
        ui.ModelInfoButton = Button("Model Information", llama, 568, 42, 0, 0, false); Top(ui.ModelInfoButton.GetComponent<RectTransform>(), 568, 42, 398); ui.ModelInfoCaption = PlainCaption(ui.ModelInfoButton, "モデル情報：その他のGGUF", 15);
        ui.ModelTermsNote = Text("Automatic Terms Note", p, "URL入力は不要です。モデルの入手元で利用条件を確認してください。", 14, 568, 56, 454, Secondary);
        ui.ErrorText = Text("Validation Message", p, "", 14, 568, 48, 524, new Color32(165, 42, 42, 255));
    }

    static void Browser(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Browse); var b = p.gameObject.AddComponent<SetupFilePicker>(); ui.Browser = b;
        b.Title = Text("Title", p, "ファイルを選ぶ", 26, 568, 46, 12, Ink, true);
        b.Description = Text("Description", p, "モデルと実行ソフトをアイコンで確認できます。", 14, 568, 44, 66, Secondary);
        b.UpButton = Link("Up", p, "上へ", "Up", 98, 34, 116, -235);
        b.HomeButton = Link("Home", p, "ホーム", "Home", 88, 34, 116, -136);
        b.DownloadsButton = Link("Downloads", p, "ダウンロード", "Downloads", 136, 34, 116, -18);
        b.DriveButton = Button("Drive", p, 106, 34, 0, 0, false); Top(b.DriveButton.GetComponent<RectTransform>(), 106, 34, 116, 112); b.DriveCaption = PlainCaption(b.DriveButton, "C:/", 14);
        b.Location = Input("Location", p, "", "Folder path", 468, 38, 166, -50); b.OpenButton = Link("Open", p, "開く", "Open", 84, 38, 166, 242);
        b.Search = Input("Filter", p, "", "Filter filenames", 568, 38, 220);
        b.Status = Text("Status", p, "", 13, 568, 28, 266, Secondary);
        var track = Bar("Loading", p, 568, 6, 300, .3f, out var fill); b.LoadingTrack = track.gameObject; b.LoadingFill = fill.rectTransform;
        fill.type = UnityEngine.UI.Image.Type.Simple; fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(0, .5f); fill.rectTransform.pivot = new Vector2(0, .5f); fill.rectTransform.sizeDelta = new Vector2(168, 6); b.LoadingTrack.SetActive(false);
        var viewport = Rect("File Viewport", p, 568, 226); Top(viewport, 568, 226, 318); viewport.gameObject.AddComponent<RectMask2D>(); var hit = viewport.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, .001f);
        b.Content = Rect("File Rows", viewport, 548, 544); b.Content.anchorMin = b.Content.anchorMax = b.Content.pivot = new Vector2(0, 1); b.Content.anchoredPosition = Vector2.zero;
        b.Scroll = viewport.gameObject.AddComponent<ScrollRect>(); b.Scroll.viewport = viewport; b.Scroll.content = b.Content; b.Scroll.horizontal = false; b.Scroll.movementType = ScrollRect.MovementType.Clamped; b.Scroll.scrollSensitivity = 32;
        b.Rows = new SetupFilePicker.Row[8]; b.FolderIcon = folder; b.ModelIcon = model; b.RuntimeIcon = chip;
        for (int i = 0; i < 8; i++)
        {
            var row = Button("File Slot " + (i + 1), b.Content, 548, 62, 0, 0, false); var r = row.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(0, -i * 68);
            var icon = Image("File Kind Icon", row.transform, i == 0 ? folder : i == 1 ? model : chip, Color.white); Top(icon.rectTransform, 40, 40, 11, -238);
            var name = Text("Filename", row.transform, i == 0 ? "Models" : i == 1 ? "model.gguf" : "llama-server.exe", 15, 450, 24, 8, Ink); name.alignment = TextAnchor.MiddleLeft; name.rectTransform.anchoredPosition = new Vector2(38, -8);
            var kind = Text("File Type", row.transform, i == 0 ? "フォルダー" : i == 1 ? "GGUFモデル" : "実行ソフト", 13, 450, 22, 34, Secondary); kind.alignment = TextAnchor.MiddleLeft; kind.rectTransform.anchoredPosition = new Vector2(38, -34);
            b.Rows[i] = new SetupFilePicker.Row { Button = row, Icon = icon, Name = name, Kind = kind }; row.gameObject.SetActive(i < 3);
        }
        b.PreviousButton = Link("Previous Files", p, "前へ", "Previous", 90, 30, 554, -222); b.NextButton = Link("Next Files", p, "次へ", "Next", 90, 30, 554, 222);
        b.PageCaption = Text("Page", p, "1 / 1", 13, 300, 30, 554, Secondary); b.ReduceMotion = ui.ReduceMotionToggle;
    }
    static void Consent(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Consent);
        Copy("Title", p, "準備を始めましょう。", "Ready when you are.", 32, 568, 64, 20, true);
        Copy("Description", p, "内容と利用条件をご確認ください。", "Review your selection and its terms.", 17, 568, 44, 96);
        var details = Image("Selection Summary", p, round, new Color32(245, 245, 247, 255)); Top(details.rectTransform, 568, 192, 154);
        ui.ReviewName = Text("Model", details.transform, "qwen3:0.6b", 18, 528, 30, 16, Ink, true);
        ui.ReviewDownload = Text("Download", details.transform, "523 MB", 15, 528, 26, 52, Secondary);
        ui.ReviewRuntime = Text("Runtime", details.transform, "Ollama", 15, 528, 26, 84, Secondary);
        ui.ReviewSource = Text("Source", details.transform, "", 13, 528, 72, 114, Secondary);
        foreach (var t in new[] { ui.ReviewName, ui.ReviewDownload, ui.ReviewRuntime, ui.ReviewSource }) t.alignment = TextAnchor.MiddleLeft;
        Copy("Privacy", p, "会話のAI処理は、このPCで行います。\n初回の取得にはインターネットを使います。", "AI conversations run on this PC.\nInitial downloads use the internet.", 16, 568, 60, 370);
        ui.ModelLicenseButton = Link("Model Terms", p, "モデルの利用条件", "Model terms", 250, 34, 440, -136);
        ui.RuntimeLicenseButton = Link("Runtime Terms", p, "実行ソフトの利用条件", "Runtime terms", 250, 34, 440, 136);
        ui.ConsentToggle = Toggle("Explicit Consent", p, "利用条件を確認し、準備に同意します", "I reviewed the terms and agree to prepare AI", 568, 66, 15); Top(ui.ConsentToggle.GetComponent<RectTransform>(), 568, 66, 494); ui.ConsentCaption = ui.ConsentToggle.transform.Find("Caption").GetComponent<Text>();
    }
    static void Progress(PlayerSetupScreen ui)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Progress); Hero(p, 24, 96);
        ui.StatusTitle = Text("Title", p, "AIモデルを準備しています。", 28, 568, 68, 154, Ink, true);
        ui.StatusCopy = Text("Description", p, "モデルの取得は中断・再開できます。", 17, 568, 52, 232, Secondary);
        ui.StageCaption = Text("Current Stage", p, "", 14, 568, 28, 294, Secondary);
        Bar("Overall Progress", p, 568, 10, 338, .25f, out ui.ProgressFill);
        ui.Percent = Text("Percent", p, "25%", 22, 568, 40, 366, Ink, true);
        ui.DownloadedBytes = Text("Downloaded Bytes", p, "", 16, 568, 30, 424, Secondary);
        ui.RemainingBytes = Text("Remaining Bytes", p, "", 16, 568, 30, 458, Secondary);
        Copy("Progress Note", p, "完了した工程と取得量からの目安です。\n最初の応答確認には時間がかかる場合があります。", "Estimated from completed steps and downloaded bytes.\nThe first response check may take a little time.", 14, 568, 58, 512);
    }
    static void Result(PlayerSetupScreen ui, SetupCanvasMotion motion)
    {
        var p = Page(ui, PlayerSetupScreen.Slide.Result); var symbol = Image("Completion Seal", p, seal, Color.white); Top(symbol.rectTransform, 96, 96, 62);
        ui.ResultSymbol = Text("Symbol", symbol.transform, "✓", 44, 96, 96, 0, Color.white);
        motion.CompletionSparks = new Image[8]; for (int i = 0; i < 8; i++) { var dot = Image("Completion Spark " + i, symbol.transform, circle, new Color(.2f, .3f, .7f, 0)); Center(dot.rectTransform, 6, 6); motion.CompletionSparks[i] = dot; }
        ui.ResultTitle = Text("Title", p, "準備ができました。", 32, 568, 64, 208, Ink, true);
        ui.ResultCopy = Text("Description", p, "あなたのAIと、ゲームを始めましょう。", 17, 568, 72, 290, Secondary);
        Bar("Completed Progress", p, 568, 10, 404, 1, out ui.ResultFill); ui.ResultPercent = Text("Percent", p, "100%", 22, 568, 44, 432, Ink, true);
        ui.ResultModelButton = Link("Select Another Model", p, "別のモデルを選ぶ", "Choose another model", 340, 38, 504); ui.ResultModelButton.gameObject.SetActive(false);
    }
    static RectTransform Rect(string name, Transform parent, float width, float height)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); Center(r, width, height); return r;
    }
    static void Center(RectTransform r, float width, float height, float x = 0, float y = 0) { r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.sizeDelta = new Vector2(width, height); r.anchoredPosition = new Vector2(x, y); }
    static void Top(RectTransform r, float width, float height, float top, float x = 0) { r.anchorMin = r.anchorMax = new Vector2(.5f, 1); r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(width, height); r.anchoredPosition = new Vector2(x, -top); }
    static void Stretch(RectTransform r, float left, float bottom, float right, float top) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f); r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top); }
    static Image Image(string name, Transform parent, Sprite sprite, Color color)
    {
        var image = Rect(name, parent, 0, 0).gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false; if (sprite == round) image.type = UnityEngine.UI.Image.Type.Sliced; return image;
    }
    static Text Text(string name, Transform parent, string value, int size, float width, float height, float top, Color color, bool bold = false)
    {
        var t = Rect(name, parent, width, height).gameObject.AddComponent<Text>(); Top(t.rectTransform, width, height, top); t.font = font; t.text = value; t.fontSize = size; t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; t.color = color; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; return t;
    }
    static Text Copy(string name, Transform parent, string jp, string en, int size, float width, float height, float top, bool bold = false, TextAnchor alignment = TextAnchor.MiddleCenter, float x = 0)
    {
        var t = Text(name, parent, jp, size, width, height, top, bold ? Ink : Secondary, bold); t.alignment = alignment; t.rectTransform.anchoredPosition = new Vector2(x, -top); var l = t.gameObject.AddComponent<SetupCanvasLabel>(); l.Japanese = jp; l.English = en; labels.Add(l); return t;
    }
    static Button Button(string name, Transform parent, float width, float height, float x, float y, bool primary)
    {
        var image = Image(name, parent, primary ? accent : round, Color.white); Center(image.rectTransform, width, height, x, y); image.raycastTarget = true;
        if (!primary) { var outline = image.gameObject.AddComponent<Outline>(); outline.effectColor = new Color32(112, 122, 134, 255); outline.effectDistance = new Vector2(1, -1); }
        var b = image.gameObject.AddComponent<Button>(); b.targetGraphic = image; var colors = b.colors; colors.highlightedColor = new Color(.91f, .94f, 1); colors.pressedColor = new Color(.79f, .85f, .96f); colors.disabledColor = new Color(.75f, .78f, .83f); b.colors = colors; return b;
    }
    static Text PlainCaption(Button b, string value, int size, Color? color = null) { var t = Text("Caption", b.transform, value, size, 0, 0, 0, color ?? Ink); Stretch(t.rectTransform, 8, 0, 8, 0); return t; }
    static Text LocalCaption(Button b, string jp, string en, int size) { var t = PlainCaption(b, jp, size); var l = t.gameObject.AddComponent<SetupCanvasLabel>(); l.Japanese = jp; l.English = en; labels.Add(l); return t; }
    static Button Link(string name, Transform parent, string jp, string en, float width, float height, float top, float x = 0) { var b = Button(name, parent, width, height, 0, 0, false); Top(b.GetComponent<RectTransform>(), width, height, top, x); LocalCaption(b, jp, en, 14).color = Blue; return b; }
    static Toggle Toggle(string name, Transform parent, string jp, string en, float width, float height, int size)
    {
        var root = Rect(name, parent, width, height); var t = root.gameObject.AddComponent<Toggle>(); var box = Image("Checkbox", root, round, Color.white); Center(box.rectTransform, 22, 22, -width / 2 + 16); box.raycastTarget = true; var outline = box.gameObject.AddComponent<Outline>(); outline.effectColor = Secondary; outline.effectDistance = new Vector2(1, -1);
        var mark = Image("Check", box.transform, round, Blue); Center(mark.rectTransform, 14, 14); t.targetGraphic = box; t.graphic = mark; t.isOn = false;
        Copy("Caption", root, jp, en, size, width - 42, height, 0, false, TextAnchor.MiddleLeft, 21); return t;
    }
    static InputField Input(string name, Transform parent, string value, string placeholder, float width, float height, float top, float x = 0)
    {
        var bg = Image(name, parent, round, Color.white); Top(bg.rectTransform, width, height, top, x); bg.raycastTarget = true; var outline = bg.gameObject.AddComponent<Outline>(); outline.effectColor = Secondary; outline.effectDistance = new Vector2(1, -1);
        var input = bg.gameObject.AddComponent<InputField>(); input.targetGraphic = bg; var text = Text("Value", bg.transform, value, 15, 0, 0, 0, Ink); Stretch(text.rectTransform, 12, 4, 12, 4); text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false; input.textComponent = text;
        var hint = Text("Placeholder", bg.transform, placeholder, 15, 0, 0, 0, Secondary); Stretch(hint.rectTransform, 12, 4, 12, 4); hint.alignment = TextAnchor.MiddleLeft; input.placeholder = hint; input.text = value; return input;
    }
    static Image Bar(string name, Transform parent, float width, float height, float top, float value, out Image fill)
    {
        var track = Image(name, parent, round, new Color32(220, 226, 232, 255)); Top(track.rectTransform, width, height, top); fill = Image("Fill", track.transform, accent, Color.white); Stretch(fill.rectTransform, 0, 0, 0, 0); fill.type = UnityEngine.UI.Image.Type.Filled; fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillAmount = value; return track;
    }
    static void Hero(Transform parent, float top, float size = 104)
    {
        var orbit = Rect("Icon Orbit", parent, 160, 128); Top(orbit, 160, 128, top); var image = Image("Gradient Sparkle", orbit, hero, Color.white); Center(image.rectTransform, size, size); icons.Add(image.rectTransform);
        for (int i = 0; i < 3; i++) { var dot = Image("Orbit Spark " + i, orbit, circle, new Color(.31f, .39f, .64f, .6f)); Center(dot.rectTransform, 6, 6, Mathf.Cos(i * 2.094f) * 65, Mathf.Sin(i * 2.094f) * 43); sparks.Add(dot.rectTransform); }
    }
    static Button Choice(string name, Transform parent, float top, Sprite icon, string jpTitle, string enTitle, string jpCopy, string enCopy)
    {
        var b = Button(name, parent, 568, 116, 0, 0, false); Top(b.GetComponent<RectTransform>(), 568, 116, top); var pic = Image("Icon", b.transform, icon, Color.white); Center(pic.rectTransform, 54, 54, -236);
        Copy("Title", b.transform, jpTitle, enTitle, 20, 430, 30, 14, true, TextAnchor.MiddleLeft, 44); Copy("Description", b.transform, jpCopy, enCopy, 14, 430, 56, 48, false, TextAnchor.MiddleLeft, 44); return b;
    }
    static void BakeArtwork()
    {
        AssetDatabase.Refresh();
        font = AssetDatabase.LoadAssetAtPath<Font>(Art + "/NotoSansJP-Regular.otf");
        if (!font) throw new InvalidOperationException("The bundled Noto Sans JP font and its OFL notice are required.");
        var type = typeof(PlayerSetupScreen).Assembly.GetType("SetupAtmosphere"); var owner = Activator.CreateInstance(type, new object[] { new UnityEngine.UIElements.VisualElement(), new UnityEngine.UIElements.VisualElement() }); var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Texture2D Read(string field) { var source = (Texture2D)type.GetField(field, flags).GetValue(owner); var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32); var previous = RenderTexture.active; Graphics.Blit(source, rt); RenderTexture.active = rt; var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false); result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); result.Apply(); RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); return result; }
        var bg = Read("backdrop"); var frost = Read("frost"); var gradient = Read("Accent"); var sparkle = Read("Icon"); background = SaveSprite("Aurora", bg); accent = SaveSprite("Accent", gradient); hero = SaveSprite("Sparkle", sparkle);
        glass = SaveSprite("Glass", Paint(640, 680, (x, y) => { var c = frost.GetPixelBilinear((400f + x) / 1440, (110f + y) / 900); c.a = Coverage(Rounded(new Vector2(x, y), new Vector2(640, 680), 30)); return c; }));
        round = SaveSprite("Rounded", Paint(64, 64, (x, y) => new Color(1, 1, 1, Coverage(Rounded(new Vector2(x, y), new Vector2(64, 64), 12)))), true);
        circle = SaveSprite("Circle", Paint(96, 96, (x, y) => new Color(1, 1, 1, Coverage(Vector2.Distance(new Vector2(x, y), new Vector2(48, 48)) - 47))));
        seal = SaveSprite("Seal", Paint(192, 192, (x, y) => { var c = Accent(x / 192); c.a = Coverage(Vector2.Distance(new Vector2(x, y), new Vector2(96, 96)) - 94); return c; }));
        chip = Glyph("Processor", 0); model = Glyph("Model", 1); folder = Glyph("Folder", 2);
        UnityEngine.Object.DestroyImmediate(bg); UnityEngine.Object.DestroyImmediate(frost); UnityEngine.Object.DestroyImmediate(gradient); UnityEngine.Object.DestroyImmediate(sparkle);
        foreach (var name in new[] { "backdrop", "frost", "Accent", "Icon" }) UnityEngine.Object.DestroyImmediate((Texture2D)type.GetField(name, flags).GetValue(owner));
    }
    static Color Accent(float t) => Color.Lerp(new Color(.02f, .29f, .67f), new Color(.32f, .18f, .64f), t);
    static float Coverage(float distance) => Mathf.Clamp01(.5f - distance);
    static float Rounded(Vector2 p, Vector2 size, float radius) { var q = new Vector2(Mathf.Abs(p.x - size.x / 2), Mathf.Abs(p.y - size.y / 2)) - size / 2 + Vector2.one * radius; return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius; }
    static Texture2D Paint(int w, int h, Func<float, float, Color> pixel) { var t = new Texture2D(w, h, TextureFormat.RGBA32, false); var colors = new Color[w * h]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) colors[y * w + x] = pixel(x + .5f, y + .5f); t.SetPixels(colors); t.Apply(); return t; }
    static Sprite SaveSprite(string name, Texture2D texture, bool sliced = false)
    {
        var path = Art + "/" + name + ".png"; File.WriteAllBytes(path, texture.EncodeToPNG()); AssetDatabase.ImportAsset(path); var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.alphaIsTransparency = true; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = true; importer.filterMode = FilterMode.Trilinear; importer.spritePixelsPerUnit = 100; importer.spriteBorder = sliced ? new Vector4(16, 16, 16, 16) : Vector4.zero; var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings); importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static Sprite Glyph(string name, int kind)
    {
        var lines = new List<Vector4>(); void Line(float x, float y, float xx, float yy) => lines.Add(new Vector4(x, y, xx, yy) / 40);
        void Box(float a, float b) { Line(a,a,b,a); Line(b,a,b,b); Line(b,b,a,b); Line(a,b,a,a); }
        if (kind == 0) { Box(13, 27); Box(17, 23); for (int k = 16; k <= 24; k += 4) { Line(k,9,k,13); Line(k,27,k,31); Line(9,k,13,k); Line(27,k,31,k); } }
        else if (kind == 1) { for (int k = 17; k <= 27; k += 5) { Line(10,k,20,k+5); Line(20,k+5,30,k); } Line(10,17,20,12); Line(20,12,30,17); }
        else { Line(9,13,17,13); Line(17,13,20,16); Line(20,16,31,16); Line(31,16,31,28); Line(31,28,9,28); Line(9,28,9,13); }
        return SaveSprite(name, Paint(160,160,(x,y) => { var p = new Vector2(x/160,1-y/160); float d = 1; foreach (var l in lines) { var a = new Vector2(l.x,l.y); var ab = new Vector2(l.z-l.x,l.w-l.y); d = Mathf.Min(d, Vector2.Distance(p,a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude))); } var c = Color.Lerp(Accent(x/160),Color.white,Mathf.Clamp01((.025f-d)*160)); c.a = Coverage(Rounded(new Vector2(x,y),new Vector2(160,160),42)); return c; }));
    }
}

[CustomEditor(typeof(PlayerSetupScreen))]
public sealed class SetupCanvasScreenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Scene-authored setup", EditorStyles.boldLabel);
        foreach (PlayerSetupScreen.Slide slide in Enum.GetValues(typeof(PlayerSetupScreen.Slide)))
            if (GUILayout.Button("Preview: " + slide)) { var ui = (PlayerSetupScreen)target; Undo.RecordObjects(ui.Slides, "Preview setup slide"); ui.ShowEditorPreview(slide, true); EditorSceneManager.MarkSceneDirty(ui.gameObject.scene); }
        DrawDefaultInspector();
    }
}
