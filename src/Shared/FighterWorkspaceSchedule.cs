namespace SlopArena.Shared;

/// <summary>One attempt per stable revision, with explicit requests retained while an attempt is running.</summary>
public sealed class FighterWorkspaceSchedule
{
    public string? Pending { get; private set; }
    public string? Attempted { get; private set; }
    public long Requested { get; private set; }
    public long Handled { get; private set; }
    private double changedAt;
    public void Request() => Requested++;
    public bool Observe(string probe, double now)
    {
        if (Pending != probe) { Pending = probe; changedAt = now; return false; }
        return now - changedAt >= .75 && (Attempted != probe || Requested != Handled);
    }
    public void Begin(string probe) { Attempted = probe; Handled = Requested; }
}
