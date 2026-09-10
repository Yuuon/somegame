using SomeGame.Core;
using Xunit;

namespace SomeGame.Core.Tests;

public class SettlementTests
{
    private static (Game g, PlayerActor t, PlayerActor k, PlayerActor b, PlayerActor m, NpcActor npc) Game4()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 3;
        var g = new Game(cfg, 100, new[]
        {
            new SeatIn("A", true), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
        var t = g.Players.First(p => p.Role == RoleId.Thief);
        var k = g.Players.First(p => p.Role == RoleId.Killer);
        var b = g.Players.First(p => p.Role == RoleId.Bodyguard);
        var m = g.Players.First(p => p.Role == RoleId.Madman);
        return (g, t, k, b, m, g.ProtectedNpcs[0]);
    }

    [Fact]
    public void E1_NpcArrivesAlive_WithIntactItem_BodyguardWins()
    {
        var (g, _, _, b, _, npc) = Game4();
        npc.Pos = g.Extraction;
        npc.Hp = 1;
        npc.ItemIntact = true;
        var s = g.CheckSettlement("test");
        Assert.NotNull(s);
        Assert.Contains(b.SeatIndex, s!.Value.Winners);
    }

    [Fact]
    public void E1_ItemStolen_NoBodyguardWin()
    {
        var (g, _, _, b, _, npc) = Game4();
        npc.Pos = g.Extraction;
        npc.ItemIntact = false;
        Assert.Null(g.CheckSettlement("test"));
        _ = b;
    }

    [Fact]
    public void E2_ThiefWithItemAtExtraction_Wins()
    {
        var (g, t, _, _, _, _) = Game4();
        t.CarriedCase = 0;
        t.Pos = g.Extraction;
        var s = g.CheckSettlement("test");
        Assert.NotNull(s);
        Assert.Contains(t.SeatIndex, s!.Value.Winners);
    }

    [Fact]
    public void E3_KillerAtExtraction_AfterNpcDead_Wins()
    {
        var (g, _, k, _, _, npc) = Game4();
        npc.Dead = true;
        k.Pos = g.Extraction;
        var s = g.CheckSettlement("test");
        Assert.NotNull(s);
        Assert.Contains(k.SeatIndex, s!.Value.Winners);
    }

    [Fact]
    public void E4_MadmanWins_WhenAllOthersDead()
    {
        var (g, t, k, b, m, _) = Game4();
        t.Dead = true; k.Dead = true; b.Dead = true;
        var s = g.CheckSettlement("test");
        Assert.NotNull(s);
        Assert.Contains(m.SeatIndex, s!.Value.Winners);
    }

    [Fact]
    public void RevealCheck_EnemyDetection_ByRoleTable()
    {
        var (g, t, k, b, _, _) = Game4();
        var kill = g.RevealCheck(k, b);
        Assert.True(kill.Enemy);
        var kthief = g.RevealCheck(k, t);
        Assert.False(kthief.Enemy); // 杀手与小偷并非敌对
        var mAd = g.RevealCheck(g.Players.First(p => p.Role == RoleId.Madman), t);
        Assert.True(mAd.Enemy);
    }
}

public class FlowTests
{
    [Fact]
    public void HumanFreeMove_Works()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 2;
        var g = new Game(cfg, 5, new[]
        {
            new SeatIn("H1", false), new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true),
        });
        g.Continue();
        Assert.Equal(Stage.Free, g.Stage);
        Assert.NotNull(g.Await);
        Assert.Equal(AwaitKind.FreeAction, g.Await!.Kind);
        Assert.Equal(0, g.Await.SeatIndex);
        var p = g.Player(0);
        var from = p.Pos;
        var dest = new CellPos(from.X, from.Y + 2);
        if (!g.InMap(dest)) dest = new CellPos(from.X + 1, from.Y);
        g.SubmitFree(0, new FreeCmd("move", X: dest.X, Y: dest.Y));
        Assert.Equal(dest, p.Pos);
    }

    [Fact]
    public void HumanFree_LastApAction_AutoFinishes()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 2;
        var g = new Game(cfg, 8, new[]
        {
            new SeatIn("H1", false), new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true),
        });
        g.Continue();
        Assert.Equal(Stage.Free, g.Stage);
        Assert.Equal(AwaitKind.FreeAction, g.Await!.Kind);
        var p = g.Player(0);
        p.Ap = 1; // 仅剩 1 行动点
        var dest = new CellPos(p.Pos.X + 1, p.Pos.Y);
        g.SubmitFree(0, new FreeCmd("move", X: dest.X, Y: dest.Y));
        Assert.True(p.FinishedFree, "AP 归零后应自动结束本轮，而非卡在行动窗口");
        Assert.Equal(dest, p.Pos);
    }

    [Fact]
    public void BuildView_ExposesRoleKey()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 1;
        var g = new Game(cfg, 9, new[]
        {
            new SeatIn("H1", false), new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true),
        });
        var v = g.BuildView(g.Player(0));
        Assert.False(string.IsNullOrEmpty(v.RoleKey));
        Assert.Contains(g.Player(0).Role.ToString().ToLowerInvariant(), v.RoleKey);
    }

    private static Game GameWithOneHuman()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 1;
        return new Game(cfg, 10, new[]
        {
            new SeatIn("H1", false), new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true),
        });
    }

    [Fact]
    public void HumanCheckSkip_AdvancesToNextSeat()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 1;
        var g = new Game(cfg, 6, new[]
        {
            new SeatIn("H1", false), new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true),
        });
        // 直接推进到查验阶段
        while (g.Stage == Stage.Free)
        {
            if (g.Await?.Kind == AwaitKind.FreeAction) g.SubmitFree(0, new FreeCmd("finish"));
            else g.Continue();
        }
        Assert.Equal(Stage.Check, g.Stage);
        Assert.Equal(0, g.Await!.SeatIndex);
        g.SubmitCheck(0, new CheckCmd(Skip: true));
        g.Continue();
        Assert.Equal(Stage.Free, g.Stage); // 其余座位为机器人，查验完毕后进入下一回合
        Assert.Equal(AwaitKind.FreeAction, g.Await!.Kind);
    }
}