using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared
{
    public enum FeedbackKind { Bug, Feel, Idea, Love }
    public enum FeedbackPhase { Frontend, Match, Results, Replay }

    public sealed class FeedbackContext
    {
        public string AppVersion { get; }
        public byte ProtocolVersion { get; }
        public string Platform { get; }
        public string Scene { get; }
        public FeedbackPhase Phase { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public FeedbackMatchContext? Match { get; }
        public FeedbackContext(string appVersion, byte protocolVersion, string platform, string scene,
            FeedbackPhase phase, DateTimeOffset observedAtUtc, FeedbackMatchContext? match = null)
        {
            AppVersion = appVersion; ProtocolVersion = protocolVersion; Platform = platform;
            Scene = scene; Phase = phase; ObservedAtUtc = observedAtUtc.ToUniversalTime();
            Match = phase == FeedbackPhase.Frontend ? null : match;
        }
    }

    public sealed class FeedbackMatchContext
    {
        public string Mode { get; }
        public string? Format { get; }
        public string? StageId { get; }
        public Guid? OnlineMatchId { get; }
        public ulong? LocalEntityId { get; }
        public uint? LastObservedServerTick { get; }
        public IReadOnlyList<FeedbackFighter> Fighters { get; }
        public FeedbackMatchContext(string mode, string? format, string? stageId, Guid? onlineMatchId,
            ulong? localEntityId, uint? lastObservedServerTick, IEnumerable<FeedbackFighter> fighters)
        {
            var copy = fighters.ToArray();
            if (copy.Length > 4 || copy.Any(f => f.EntityId == 0) || copy.Select(f => f.EntityId).Distinct().Count() != copy.Length)
                throw new ArgumentException("Feedback requires up to four distinct fighters.", nameof(fighters));
            Mode = mode; Format = format; StageId = stageId; OnlineMatchId = onlineMatchId;
            LocalEntityId = localEntityId; LastObservedServerTick = lastObservedServerTick;
            Fighters = Array.AsReadOnly(copy);
        }
    }

    public sealed class FeedbackFighter
    {
        public ulong EntityId { get; }
        public string PackageId { get; }
        public string? PackageVersion { get; }
        public string? CookedContentHash { get; }
        public FeedbackFighter(ulong entityId, string packageId, string? packageVersion = null, string? cookedContentHash = null)
        { EntityId = entityId; PackageId = packageId; PackageVersion = packageVersion; CookedContentHash = cookedContentHash; }
    }

    public sealed class FeedbackImage
    {
        private readonly byte[] _jpeg;
        public ReadOnlyMemory<byte> Jpeg => new ReadOnlyMemory<byte>((byte[])_jpeg.Clone());
        public int Width { get; }
        public int Height { get; }
        public FeedbackImage(byte[] jpeg, int width, int height)
        {
            if (jpeg == null || jpeg.Length < 3 || jpeg.Length > 1048576 || jpeg[0] != 255 || jpeg[1] != 216 || jpeg[2] != 255 ||
                width < 1 || width > 1280 || height < 1 || height > 1280 || (long)width * height > 921600)
                throw new ArgumentException("Invalid feedback JPEG.", nameof(jpeg));
            _jpeg = (byte[])jpeg.Clone(); Width = width; Height = height;
        }
    }

    public sealed class FeedbackSubmission
    {
        public Guid SubmissionId { get; }
        public FeedbackKind Kind { get; }
        public string Message { get; }
        public FeedbackContext Context { get; }
        public FeedbackImage? Image { get; }
        public FeedbackSubmission(Guid submissionId, FeedbackKind kind, string message, FeedbackContext context, FeedbackImage? image)
        {
            if (submissionId == Guid.Empty || !Enum.IsDefined(typeof(FeedbackKind), kind) || message == null || message.Length > 2000)
                throw new ArgumentException("Invalid feedback submission.");
            for (int i = 0; i < message.Length; i++)
            {
                char c = message[i];
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') throw new ArgumentException("Invalid message control.");
                if (char.IsHighSurrogate(c))
                { if (++i >= message.Length || !char.IsLowSurrogate(message[i])) throw new ArgumentException("Invalid Unicode."); }
                else if (char.IsLowSurrogate(c)) throw new ArgumentException("Invalid Unicode.");
            }
            SubmissionId = submissionId; Kind = kind; Message = message;
            Context = context ?? throw new ArgumentNullException(nameof(context)); Image = image;
        }
    }

    public sealed class FeedbackReceipt
    {
        public Guid ReportId { get; }
        public DateTimeOffset ReceivedAtUtc { get; }
        public FeedbackReceipt(Guid reportId, DateTimeOffset receivedAtUtc)
        { ReportId = reportId; ReceivedAtUtc = receivedAtUtc; }
    }

    public abstract class FeedbackSendResult
    {
        private FeedbackSendResult() { }
        public sealed class Accepted : FeedbackSendResult
        {
            public FeedbackReceipt Receipt { get; }
            public Accepted(FeedbackReceipt receipt) { Receipt = receipt; }
        }
        public sealed class Retryable : FeedbackSendResult
        {
            public string Reason { get; }
            public TimeSpan? RetryAfter { get; }
            public Retryable(string reason, TimeSpan? retryAfter = null) { Reason = reason; RetryAfter = retryAfter; }
        }
        public sealed class Rejected : FeedbackSendResult
        {
            public string Code { get; }
            public Rejected(string code) { Code = code; }
        }
    }
}
