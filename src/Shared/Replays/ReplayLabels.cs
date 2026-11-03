namespace SlopArena.Shared.Replays;

public static class ReplayLabels
{
    /// <summary>Recorded name, or fighter name with the last six digits of the Steam ID.</summary>
    public static string ReplayParticipantLabel(ReplayParticipant participant, string fighterName)
    {
        if (participant.DisplayName.Length != 0) return participant.DisplayName;
        if (participant.ParticipantProvider != "steam") return fighterName;
        string id = participant.ParticipantSubject;
        return fighterName + " (" + (id.Length > 6 ? id.Substring(id.Length - 6) : id) + ")";
    }
}
