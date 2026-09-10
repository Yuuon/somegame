namespace SomeGame.Core;

public sealed class GameMetrics
{
    public int FirstKillerNearVipRound { get; set; } = -1;
    public int FirstThiefNearVipRound { get; set; } = -1;
    public int FirstBodyguardNearVipRound { get; set; } = -1;
    public int FirstBattleRound { get; set; } = -1;
    public int FirstVipExposeRound { get; set; } = -1;
    public int FirstStealRound { get; set; } = -1;
    public int TotalPlayerFreeActions { get; set; }
    public int TotalPlayerCheckActions { get; set; }
    public int TotalBattles { get; set; }
    public Dictionary<string, int> ActionCounts { get; } = new();
}