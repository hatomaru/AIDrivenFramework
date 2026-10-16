using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIDrivenFW.API;
using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.Setup
{
    [Serializable] public sealed class InstalledModel { public string name; public long size; }
    [Serializable] sealed class ModelList { public InstalledModel[] models; }
    [Serializable] sealed class ModelBody { public string model; public bool stream = true; }
    [Serializable] sealed class PullLine { public string status; public string digest; public long completed; public long total; public string error; }
    [Serializable] sealed class ModelDetails { public string remote_host; public string remote_model; public string[] capabilities; }
    [Serializable] sealed class ProbeBody
    {
        public string model;
        public string prompt = "Reply with one short greeting. /no_think";
        public bool stream = false;
        public bool think = false;
        public string keep_alive = "0";
        public ProbeOptions options = new ProbeOptions();
    }
    [Serializable] sealed class ProbeOptions { public int num_predict = 24; public int num_ctx = 2048; }
    [Serializable] sealed class ProbeResult { public bool done; public string response; }

    [Serializable]
    public sealed class SetupJournal
    {
        public PlayerSetupPlan Plan;
        public string ConsentUtc;
        public string Revision;
        public SetupStage Stage;
        public static string FilePath => Path.Combine(Application.persistentDataPath, "AIDrivenFramework", "setup-attempt.json");
        public static SetupJournal Load()
        {
            try { return File.Exists(FilePath) ? JsonUtility.FromJson<SetupJournal>(File.ReadAllText(FilePath)) : null; }
            catch { return null; }
        }
    }

    public sealed class PlayerSetupServices : IPlayerSetupServices
    {
        // Only this loopback server receives prompts. Model acquisition uses the server's registry/CDN.
        public const string Server = "http://127.0.0.1:11434";
        static readonly HttpClient local = new HttpClient(new HttpClientHandler { UseProxy = false })
            { Timeout = Timeout.InfiniteTimeSpan };
        Process ownedServer;
        readonly string endpoint;
        readonly HttpClient client;
        public PlayerSetupServices() : this(11434, local) { }
        internal PlayerSetupServices(int port, HttpClient client)
        {
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            endpoint = "http://127.0.0.1:" + port;
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public static string FindOllama()
        {
            if (File.Exists(RuntimeRelease.ExecutablePath)) return RuntimeRelease.ExecutablePath;
            var saved = ModelInfo.LoadFromFile();
            if (saved?.Mode == "Ollama" && File.Exists(saved.RuntimePath)) return saved.RuntimePath;
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                "/usr/local/bin/ollama", "/opt/homebrew/bin/ollama", "/usr/bin/ollama",
                "/Applications/Ollama.app/Contents/Resources/ollama"
            };
            foreach (string path in candidates) if (File.Exists(path)) return path;
            string filename = Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer
                ? "ollama.exe" : "ollama";
            foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { string candidate = Path.Combine(path.Trim('"'), filename); if (File.Exists(candidate)) return candidate; }
                catch (ArgumentException) { }
            }
            return "";
        }

        public static async UniTask<InstalledModel[]> InstalledModelsAsync(CancellationToken ct)
            => await ReadInstalledAsync(local, Server, ct);

        static async UniTask<InstalledModel[]> ReadInstalledAsync(HttpClient client, string address, CancellationToken ct)
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probe.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                using var response = await client.GetAsync(address + "/api/tags", probe.Token);
                if (!response.IsSuccessStatusCode) return Array.Empty<InstalledModel>();
                string json = await response.Content.ReadAsStringAsync();
                return JsonUtility.FromJson<ModelList>(json)?.models ?? Array.Empty<InstalledModel>();
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return Array.Empty<InstalledModel>(); }
        }

        async UniTask<bool> ServerReadyAsync(CancellationToken ct)
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probe.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                using var response = await client.GetAsync(endpoint + "/api/version", probe.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        }

        public async UniTask<string> PrepareRuntimeAsync(PlayerSetupPlan plan, Action<SetupProgress> progress, CancellationToken ct)
        {
            if (plan.Mode == "Llama") return plan.RuntimePath;
            if (await ServerReadyAsync(ct)) return File.Exists(plan.RuntimePath) ? plan.RuntimePath : FindOllama();
            string path = File.Exists(plan.RuntimePath) ? plan.RuntimePath : FindOllama();
            if (string.IsNullOrEmpty(path))
            {
                if (!RuntimeRelease.CanAcquire)
                    throw new SetupException(SetupProblem.Runtime, "Install and start Ollama from ollama.com/download, then retry. Your OS may ask for confirmation.");
                path = await RuntimeAcquisition.AcquireAsync(RuntimeRelease.DirectoryPath, progress, ct);
            }
            ct.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(path, "serve")
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path)
            };
            // A managed portable runtime stores models in the game's writable data directory.
            if (path == RuntimeRelease.ExecutablePath)
            {
                start.EnvironmentVariables["OLLAMA_MODELS"] = ManagedModelDirectory;
                start.EnvironmentVariables["OLLAMA_NO_CLOUD"] = "1";
            }
            start.EnvironmentVariables["OLLAMA_HOST"] = endpoint.Substring("http://".Length);
            try { ownedServer = Process.Start(start); }
            catch (Exception ex) { throw new SetupException(SetupProblem.Runtime, "Ollama could not start. Check OS permissions and retry.", ex); }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                while (!await ServerReadyAsync(deadline.Token))
                {
                    if (ownedServer == null || ownedServer.HasExited)
                        throw new SetupException(SetupProblem.Runtime, "Ollama stopped during startup.");
                    await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new SetupException(SetupProblem.Runtime, "Ollama did not become ready. Check firewall permissions."); }
            return path;
        }

        public static string ManagedModelDirectory => Path.Combine(Application.persistentDataPath, "AIDrivenFramework", "OllamaModels");

        public async UniTask PrepareModelAsync(PlayerSetupPlan plan, Action<SetupProgress> progress, CancellationToken ct)
        {
            if (plan.Mode == "Llama") return;
            var installed = await ReadInstalledAsync(client, endpoint, ct);
            bool exists = installed.Any(m => string.Equals(m.name, plan.Model, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                string modelDirectory = plan.RuntimePath == RuntimeRelease.ExecutablePath ? ManagedModelDirectory :
                    Environment.GetEnvironmentVariable("OLLAMA_MODELS") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
                RuntimeAcquisition.RequireSpace(modelDirectory, checked(plan.ModelBytes + 1024L * 1024 * 1024));
                await PullAtAsync(client, endpoint, plan.Model, progress, ct);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var body = new StringContent(JsonUtility.ToJson(new ModelBody { model = plan.Model, stream = false }), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint + "/api/show", body, deadline.Token);
            response.EnsureSuccessStatusCode();
            var details = JsonUtility.FromJson<ModelDetails>(await response.Content.ReadAsStringAsync());
            if (details == null || !string.IsNullOrEmpty(details.remote_host) || !string.IsNullOrEmpty(details.remote_model))
                throw new SetupException(SetupProblem.Model, "Choose a local model. Cloud models are not supported by this setup.");
            if (details.capabilities != null && !details.capabilities.Contains("completion"))
                throw new SetupException(SetupProblem.Model, "This model does not support text generation.");
        }

        public static async UniTask PullAsync(string model, Action<SetupProgress> progress, CancellationToken ct)
            => await PullAtAsync(local, Server, model, progress, ct);

        static async UniTask PullAtAsync(HttpClient client, string address, string model, Action<SetupProgress> progress, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, address + "/api/pull")
                { Content = new StringContent(JsonUtility.ToJson(new ModelBody { model = model }), Encoding.UTF8, "application/json") };
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromHours(2));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                using var abort = deadline.Token.Register(() => stream.Dispose());
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var read = reader.ReadLineAsync();
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    var delay = Task.Delay(TimeSpan.FromMinutes(3), idle.Token);
                    if (await Task.WhenAny(read, delay) != read)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        throw new TimeoutException("Model download stopped responding.");
                    }
                    idle.Cancel();
                    string line = await read;
                    if (line == null) throw new IOException("Model pull ended before success.");
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (ParsePullLine(line, progress)) return;
                }
            }
            catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            catch (SetupException) { throw; }
            catch (Exception ex) { throw new SetupException(SetupProblem.Connection, "Model download failed. Retry to resume the partial download.", ex); }
        }

        public static bool ParsePullLine(string json, Action<SetupProgress> progress)
        {
            var line = JsonUtility.FromJson<PullLine>(json);
            if (line == null || (!string.IsNullOrEmpty(line.error)))
                throw new SetupException(SetupProblem.Model, line?.error ?? "Invalid model pull response.");
            if (string.IsNullOrEmpty(line.status)) throw new SetupException(SetupProblem.Connection, "Missing model pull status.");
            progress?.Invoke(new SetupProgress { Stage = SetupStage.Model,
                CompletedBytes = line.completed, TotalBytes = line.total, Item = line.digest });
            return line.status == "success";
        }

        public async UniTask VerifyAsync(PlayerSetupPlan plan, CancellationToken ct)
        {
            if (plan.Mode == "Ollama")
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMinutes(3));
                using var body = new StringContent(JsonUtility.ToJson(new ProbeBody { model = plan.Model }), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(endpoint + "/api/generate", body, deadline.Token);
                response.EnsureSuccessStatusCode();
                var result = JsonUtility.FromJson<ProbeResult>(await response.Content.ReadAsStringAsync());
                if (result == null || !result.done || string.IsNullOrWhiteSpace(result.response))
                    throw new SetupException(SetupProblem.Verification, "The model returned no completed greeting.");
                return;
            }
            var config = ScriptableObject.CreateInstance<GenAIConfig>();
            var ai = new GenAI(new AIExecutorContext(new LlamaHTTPExecutor()));
            try
            {
                config.aiSoftwarePath = plan.RuntimePath;
                config.modelFilePath = plan.ModelPath;
                config.arguments = AIDrivenConfig.autoDetect;
                string result = await ai.Generate("Reply with one short greeting.", config, ct: ct, retryAfterInitialization: false);
                if (string.IsNullOrWhiteSpace(result)) throw new SetupException(SetupProblem.Verification, "The model returned no greeting.");
            }
            finally
            {
                ai.KillProcess();
                if (Application.isPlaying) UnityEngine.Object.Destroy(config);
                else UnityEngine.Object.DestroyImmediate(config);
            }
        }

        public void RecordAttempt(PlayerSetupPlan plan, string consentUtc, SetupStage stage) =>
            ModelInfo.WriteAtomic(SetupJournal.FilePath, JsonUtility.ToJson(new SetupJournal
            { Plan = plan, ConsentUtc = consentUtc, Revision = ModelInfo.CurrentConsentRevision, Stage = stage }, true));

        public void Commit(ModelInfo settings)
        {
            settings.SaveToFile();
            AIDrivenConfig.isUseOllama = settings.Mode == "Ollama";
            // The journal is advisory; cleanup failure must not undo a verified commit.
            try { if (File.Exists(SetupJournal.FilePath)) File.Delete(SetupJournal.FilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (ownedServer == null) return;
            try { if (!ownedServer.HasExited) { ownedServer.Kill(); ownedServer.WaitForExit(3000); } }
            catch (InvalidOperationException) { }
            finally { ownedServer.Dispose(); ownedServer = null; }
        }
    }
}
