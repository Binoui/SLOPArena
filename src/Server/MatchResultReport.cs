namespace SlopArena.Server
{
	/// <summary>What a finished match reports to Master: its winner and each player's connected time.</summary>
	public sealed record MatchResultReport(Guid MatchId, long WinnerSteamId, IReadOnlyList<MatchPlayerTime> Players);

	/// <summary>Seconds a player's peer stayed connected between the countdown and the result.</summary>
	public sealed record MatchPlayerTime(long SteamId, int ConnectedSeconds);
}
