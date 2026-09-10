using SomeGame.Core;
using SomeGame.Core.Balance;
using Xunit;

namespace SomeGame.Core.Tests;

public class BalanceTests
{
    private static readonly string[] CombatCards = { "gun", "knife", "dodge", "shield", "stim", "aim", "rapidfire" };

    [Fact]
    public void Deck_CombatShareWithin55to60_Percent()
    {
        var cfg = GameConfig.Default();
        var deck = BattleSim.BuildDeck(cfg);
        int combat = deck.Count(id => CombatCards.Contains(id));
        double frac = (double)combat / deck.Count * 100;
        Assert.InRange(frac, 55, 60.1);
    }

    [Fact]
    public void Deck_Specials_Each2to4()
    {
        var cfg = GameConfig.Default();
        var counts = BattleSim.BuildDeck(cfg).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, n) in counts)
            if (!CombatCards.Contains(id))
                Assert.InRange(n, 2, 4);
    }

    [Fact]
    public void GunDuel_WithTempSupply_ReachesThreeConfrontations()
    {
        var cfg = GameConfig.Default();
        var rng = Rand.Create(4242);
        int confront = 0;
        const int seeds = 1200;
        for (int i = 0; i < seeds; i++)
        {
            var a = BattleSim.DrawFighter(cfg, rng, 5, false, "gun_temp");
            var b = BattleSim.DrawFighter(cfg, rng, 5, false, "gun_temp");
            confront += BattleSim.Battle(cfg, rng, a, b, 2, false).Confrontations;
        }
        var avg = (double)confront / seeds;
        Assert.InRange(avg, 3.0, 4.5);
    }

    [Fact]
    public void MeleeCrowd_AroundThreeConfrontations()
    {
        var cfg = GameConfig.Default();
        var rng = Rand.Create(999);
        int confront = 0;
        const int seeds = 1200;
        for (int i = 0; i < seeds; i++)
        {
            var a = BattleSim.DrawFighter(cfg, rng, 5, false);
            var b = BattleSim.DrawFighter(cfg, rng, 5, false);
            confront += BattleSim.Battle(cfg, rng, a, b, 1, true).Confrontations;
        }
        var avg = (double)confront / seeds;
        Assert.InRange(avg, 2.0, 3.6);
    }

    [Fact]
    public void EightPlayer_DualCase_BotGames_AllConverge()
    {
        var cfg = GameConfig.Default();
        var report = Report.RunStress(cfg, 6);
        Assert.Equal(6, report.Converged);
        Assert.InRange(report.AvgRounds, 1, cfg.Turn.MaxRounds);
    }
}