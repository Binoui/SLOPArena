#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SlopArena.Client.Workshop
{
    public enum ModelSwapPhase { None, Loading, Worn, Refused }

    /// <summary>
    /// One fighter's model swap: reads its player's <see cref="MatchConfig.ModelSwaps"/> choice, imports the GLB
    /// while the built-in model plays, then has the renderer wear it. A refusal or failure leaves the built-in model
    /// on and the match running. Presentation only.
    /// </summary>
    public sealed class FighterModelSwap : MonoBehaviour
    {
        private PlayerRenderer _renderer = null!;
        private string _packageId = "";
        private bool _pending;
        private CancellationTokenSource? _cancel;
        private ImportedSwapModel? _worn;

        public ModelSwapPhase Phase { get; private set; }
        public ModelSwapChoice? Choice { get; private set; }
        public ModelSwapRefusal? Refusal { get; private set; }
        public ModelSwapImportStats? Stats { get; private set; }
        /// <summary>Milliseconds from asking for the swap to wearing it or refusing it.</summary>
        public double SettleMs { get; private set; }

        /// <summary>Called whenever a renderer is (re)configured for a fighter; the swap follows on the next frame.</summary>
        public static void Track(PlayerRenderer renderer, string packageId)
        {
            var swap = renderer.GetComponent<FighterModelSwap>() ?? renderer.gameObject.AddComponent<FighterModelSwap>();
            swap.Restart(renderer, packageId);
        }

        private void Restart(PlayerRenderer renderer, string packageId)
        {
            bool unchanged = Phase == ModelSwapPhase.Worn && _worn?.Root != null && renderer.IsWearing(_worn.Root)
                && packageId == _packageId;
            _renderer = renderer;
            _packageId = packageId;
            if (unchanged) return;
            Drop();
            _pending = true;
        }

        // PlayerNumber is final only after the match's setup returns, so the choice is read on the next frame.
        private void Update()
        {
            if (Phase == ModelSwapPhase.Worn && (_worn?.Root == null || !_renderer.IsWearing(_worn.Root)))
            {
                _worn?.Dispose();
                _worn = null;
                Phase = ModelSwapPhase.None;
            }
            if (!_pending) return;
            _pending = false;
            if (MatchConfig.ModelSwaps.TryGetValue(_renderer.PlayerNumber, out var choice))
                Begin(choice);
        }

        // A swap must never throw into the match: anything unexpected becomes a refusal.
        private async void Begin(ModelSwapChoice choice)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                await Wear(choice, clock);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (this != null) Refuse(ModelSwapRefusalReason.ImportFailed, exception.Message, clock);
            }
        }

        private async Task Wear(ModelSwapChoice choice, Stopwatch clock)
        {
            Choice = choice;
            Refusal = null;
            Stats = null;
            if (choice.BasePackageId != null && choice.BasePackageId != _packageId) return;
            if (!ModelSwapBases.Accepts(_packageId, out string notReady))
            {
                Refuse(ModelSwapRefusalReason.BaseFighterNotReady, notReady, clock);
                return;
            }
            var builtIn = _renderer.Animator;
            if (builtIn == null || !builtIn.isHuman)
            {
                Refuse(ModelSwapRefusalReason.BaseFighterNotReady, $"{_packageId}'s built-in model has no humanoid avatar to match.", clock);
                return;
            }
            var target = new ModelSwapTarget(builtIn.humanScale, MatchVisualStyle.RimColorFor(_renderer), builtIn.cullingMode);
            var cancel = _cancel = new CancellationTokenSource();
            Phase = ModelSwapPhase.Loading;
            ModelSwapImportResult result;
            try
            {
                var token = cancel.Token;
                var (glb, boneMap) = await Task.Run(() => Read(choice, token), token);
                result = await ModelSwapImporter.ImportAsync(glb, boneMap, target, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (TooLarge tooLarge)
            {
                if (cancel == _cancel) Refuse(ModelSwapRefusalReason.FileLimits, tooLarge.Message, clock);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (cancel == _cancel) Refuse(ModelSwapRefusalReason.FileUnreadable, exception.Message, clock);
                return;
            }
            if (cancel != _cancel || this == null)
            {
                result.Model?.Dispose();
                return;
            }
            _cancel = null;
            if (result.Model == null)
            {
                Refuse(result.Refusal!.Reason, result.Refusal.Message, clock);
                return;
            }
            Stats = result.Model.Stats;
            bool worn;
            string error;
            try
            {
                worn = _renderer.WearModel(result.Model.Root!, result.Model.Animancer!, out error);
            }
            catch
            {
                result.Model.Dispose();
                throw;
            }
            if (!worn)
            {
                result.Model.Dispose();
                Refuse(ModelSwapRefusalReason.ImportFailed, error, clock);
                return;
            }
            result.Model.Show();
            _worn = result.Model;
            Phase = ModelSwapPhase.Worn;
            SettleMs = clock.Elapsed.TotalMilliseconds;
            Debug.Log($"[ModelSwap] P{_renderer.PlayerNumber} wears {choice.Name} on {_packageId} after {SettleMs:0} ms: {Stats}");
        }

        /// <summary>The largest humanoid.json read; a full Mixamo map is about 3 KB.</summary>
        public const int MaxBoneMapBytes = 64 * 1024;

        // Runs on a worker thread. Sizes are checked before a byte is read, and a dropped swap stops the read.
        private static async Task<(byte[] Glb, string? BoneMap)> Read(ModelSwapChoice choice, CancellationToken cancel)
        {
            if (choice.BasePackageId != null) WorkshopLibrary.Verify(Path.GetDirectoryName(choice.GlbPath)!);
            byte[] glb = await ReadCapped(choice.GlbPath, WorkshopGlbLimits.Default.MaxFileBytes, "glb.limit.file-bytes", cancel);
            string? boneMap = null;
            if (choice.BoneMapPath is { } path)
            {
                byte[] json = await ReadCapped(path, MaxBoneMapBytes, "humanoid.limit.file-bytes", cancel);
                using var reader = new StreamReader(new MemoryStream(json), Encoding.UTF8, true);
                boneMap = reader.ReadToEnd();
            }
            return (glb, boneMap);
        }

        private static async Task<byte[]> ReadCapped(string path, long maxBytes, string code, CancellationToken cancel)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
            if (stream.Length > maxBytes)
                throw new TooLarge($"{code}: {Path.GetFileName(path)} is {stream.Length} bytes; the limit is {maxBytes}.");
            var bytes = new byte[stream.Length];
            for (int read = 0; read < bytes.Length;)
            {
                int n = await stream.ReadAsync(bytes, read, bytes.Length - read, cancel).ConfigureAwait(false);
                if (n == 0) throw new IOException($"{Path.GetFileName(path)} ended before its length.");
                read += n;
            }
            return bytes;
        }

        private sealed class TooLarge : Exception
        {
            public TooLarge(string message) : base(message) { }
        }

        private void Refuse(ModelSwapRefusalReason reason, string message, Stopwatch clock)
        {
            _cancel = null;
            Refusal = new ModelSwapRefusal(reason, message);
            Phase = ModelSwapPhase.Refused;
            SettleMs = clock.Elapsed.TotalMilliseconds;
            Debug.LogWarning($"[ModelSwap] P{_renderer.PlayerNumber} keeps the built-in {_packageId}; {Choice?.Name} refused: {Refusal}");
        }

        /// <summary>Stop any import and give the fighter its built-in model back.</summary>
        public void Drop()
        {
            _pending = false;
            _cancel?.Cancel();
            _cancel = null;
            if (_worn != null)
            {
                if (_worn.Root != null && _renderer != null && _renderer.IsWearing(_worn.Root))
                    _renderer.TakeOffModel();
                _worn.Dispose();
                _worn = null;
            }
            Phase = ModelSwapPhase.None;
        }

        private void OnDestroy()
        {
            _cancel?.Cancel();
            _cancel = null;
            if (_worn == null) return;
            // The fighter's own hierarchy is being destroyed and takes the worn model with it.
            _worn.Root = null;
            _worn.Dispose();
            _worn = null;
        }
    }
}
