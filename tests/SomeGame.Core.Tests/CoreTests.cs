using SomeGame.Core;
using Xunit;

namespace SomeGame.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void DefaultConfig_Loads_And_DeckSumsWithinRange()
    {
        var cfg = GameConfig.Default();
        Assert.NotNull(cfg);
        Assert.Equal(102, cfg.Deck.Sum(d => d.Count));
        Assert.True(cfg.Deck.Sum(d => d.Count) is >= 90 and <= 120);
        Assert.Equal(4, cfg.PlanFor(4).TotalSeats);
        Assert.Equal(8, cfg.PlanFor(8).TotalSeats);
        foreach (var id in cfg.Deck.Select(d => d.CardId))
            Assert.NotNull(cfg.Card(id));
    }
}

public class SimTests
{
    private static GameConfig Small()
    {
        var c = GameConfig.Default();
        c.Map.Width = 11;
        c.Map.Height = 11;
        c.Map.DecoyNpcCount = 5;
        c.ProtectedNpcHp = 3;
        c.Turn.MaxRounds = 80;
        return c;
    }

    private static SeatIn[] FourBots() =>
        new[] { new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true), new SeatIn("B4", true) };

    [Fact]
    public void Deterministic_SameSeed_SameResult()
    {
        var cfg = Small();
        var a = RunAll(cfg, 12345);
        var b = RunAll(cfg, 12345);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FourBotGame_Terminates_WithinRoundCap()
    {
        var cfg = Small();
        var game = new Game(cfg, 777, FourBots());
        game.Continue();
        Assert.True(game.Terminal || game.Round > cfg.Turn.MaxRounds,
            $"未在允许轮数内结束：round={game.Round}, terminal={game.Terminal}, reason={game.TerminalReason}, " +
            $"stage={game.Stage}, await={game.Await?.Kind}@{game.Await?.SeatIndex}, battle={game.Battle?.Active}, " +
            $"alive={string.Join(",", game.Players.Where(p => !p.Dead).Select(p => p.Role))}, " +
            $"protected={string.Join(",", game.ProtectedNpcs.Select(n => $"{n.CaseColor}:hp{n.Hp}@{(n.Dead ? "死" : n.Pos.ToString())}"))}");
    }

    private static string RunAll(GameConfig cfg, long seed)
    {
        var g = new Game(cfg, seed, FourBots());
        g.Continue();
        return $"{g.Round}|{g.TerminalReason}|{string.Join(",", g.Winners.OrderBy(x => x))}";
    }
}
