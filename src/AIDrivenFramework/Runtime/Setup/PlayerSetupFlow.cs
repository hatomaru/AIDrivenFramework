using System;
using System.IO;
using System.Threading;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;

namespace AIDrivenFW.Setup
{
    public enum SetupStage { Consent, Runtime, Model, Verify, Complete, Cancelled, Failed }
    public enum SetupProblem { None, Space, Connection, Runtime, Model, Verification, Settings }

    public sealed class SetupProgress
    {
        public SetupStage Stage;
        public long CompletedBytes;
        public long TotalBytes;
        public string Item;
        public SetupProblem Problem;
        public string Detail;
    }

    // Injected for deterministic failure/cancellation tests and alternative runtime providers.
    public interface IPlayerSetupServices : IDisposable
    {
        UniTask<string> PrepareRuntimeAsync(PlayerSetupPlan plan, Action<SetupProgress> progress, CancellationToken ct);
        UniTask PrepareModelAsync(PlayerSetupPlan plan, Action<SetupProgress> progress, CancellationToken ct);
        UniTask VerifyAsync(PlayerSetupPlan plan, CancellationToken ct);
        void RecordAttempt(PlayerSetupPlan plan, string consentUtc, SetupStage stage);
        void Commit(ModelInfo settings);
    }

    public sealed class PlayerSetupFlow
    {
        readonly IPlayerSetupServices services;
        public SetupStage Stage { get; private set; } = SetupStage.Consent;
        public SetupProblem Problem { get; private set; }
        public string Detail { get; private set; }
        public event Action<SetupProgress> Changed;
        bool running;

        public PlayerSetupFlow(IPlayerSetupServices services) => this.services = services ?? throw new ArgumentNullException(nameof(services));

        public async UniTask<bool> RunAsync(PlayerSetupPlan selection, bool accepted, CancellationToken ct)
        {
            // This gate precedes even a journal write, process launch or disk-space probe.
            if (!accepted) throw new InvalidOperationException("Player consent is required.");
            if (running) throw new InvalidOperationException("Setup is already running.");
            var plan = selection?.Snapshot() ?? throw new ArgumentNullException(nameof(selection));
            plan.Validate();
            ct.ThrowIfCancellationRequested();
            running = true;
            Problem = SetupProblem.None;
            Detail = null;
            string consentUtc = DateTime.UtcNow.ToString("o");
            try
            {
                SetStage(SetupStage.Runtime);
                services.RecordAttempt(plan, consentUtc, Stage);
                plan.RuntimePath = await services.PrepareRuntimeAsync(plan, Report, ct);
                ct.ThrowIfCancellationRequested();
                SetStage(SetupStage.Model);
                services.RecordAttempt(plan, consentUtc, Stage);
                await services.PrepareModelAsync(plan, Report, ct);
                ct.ThrowIfCancellationRequested();
                SetStage(SetupStage.Verify);
                services.RecordAttempt(plan, consentUtc, Stage);
                await services.VerifyAsync(plan, ct);
                ct.ThrowIfCancellationRequested();
                // The old working configuration survives every failure before this point.
                try { services.Commit(plan.ToSettings(consentUtc)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { throw new SetupException(SetupProblem.Settings, "Could not save verified setup settings.", ex); }
                SetStage(SetupStage.Complete);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetStage(SetupStage.Cancelled);
                return false;
            }
            catch (Exception ex)
            {
                Problem = ex is SetupException setup ? setup.Problem :
                    Stage == SetupStage.Verify ? SetupProblem.Verification :
                    Stage == SetupStage.Model ? SetupProblem.Connection :
                    Stage == SetupStage.Runtime ? SetupProblem.Runtime : SetupProblem.Settings;
                Detail = ex.Message;
                SetStage(SetupStage.Failed);
                return false;
            }
            finally { running = false; }
        }

        void SetStage(SetupStage stage)
        {
            Stage = stage;
            Report(new SetupProgress { Stage = stage, Problem = Problem, Detail = Detail });
        }
        void Report(SetupProgress progress) => Changed?.Invoke(progress);
    }

    public sealed class SetupException : Exception
    {
        public SetupProblem Problem { get; }
        public SetupException(SetupProblem problem, string message, Exception inner = null) : base(message, inner) => Problem = problem;
    }
}
