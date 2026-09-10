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
        npc.Evacuated = true; // 到达撤离点且成功撤离
        var s = g.CheckSettlement("test");
        Assert.NotNull(s);
        Assert.Contains(b.SeatIndex, s!.Value.Winners);
    }

    [Fact]
    public void E1_NotEvacuatedYet_NoWin()
    {
        var (g, _, _, b, _, npc) = Game4();
        npc.Pos = g.Extraction;
        npc.Hp = 1;
        npc.ItemIntact = true;
        npc.Evacuated = false; // 已抵达但尚未撤离（例如有敌对玩家在场）
        Assert.Null(g.CheckSettlement("test"));
        _ = b;
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

public class SpawnTests
{
    private static Game FourBots(long seed)
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 13;
        cfg.Map.Height = 13;
        cfg.Map.DecoyNpcCount = 4;
        return new Game(cfg, seed, new[]
        {
            new SeatIn("A", true), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
    }

    [Fact]
    public void Bodyguard_StartsWithinOneMove_OfProtectedTarget()
    {
        for (long seed = 1; seed <= 20; seed++)
        {
            var g = FourBots(seed);
            foreach (var b in g.Players.Where(p => p.Role == RoleId.Bodyguard))
            {
                var npc = g.ProtectedOfCase(b.CaseId);
                Assert.True(CellPos.Manhattan(b.Pos, npc.Pos) <= 3,
                    $"seed={seed} 保镖与目标距离={CellPos.Manhattan(b.Pos, npc.Pos)} 超过移动范围");
            }
        }
    }

    [Fact]
    public void ProtectedNpc_SpawnsFurtherThanFive_FromExtraction()
    {
        for (long seed = 1; seed <= 20; seed++)
        {
            var g = FourBots(seed);
            foreach (var n in g.ProtectedNpcs)
                Assert.True(CellPos.Manhattan(n.Pos, g.Extraction) > 5,
                    $"seed={seed} 贵宾距撤离点={CellPos.Manhattan(n.Pos, g.Extraction)} 不满足>5");
        }
    }

    [Fact]
    public void Extraction_Visibility_Gated()
    {
        var g = FourBots(1);
        // 第1回合：所有人不可见（含保镖）
        var b = g.Players.First(p => p.Role == RoleId.Bodyguard);
        Assert.Equal(1, g.Round);
        Assert.False(g.BuildView(b).ExtractionVisible);
        Assert.Equal(-1, g.BuildView(b).ProtectedCountdown);

        // 第5回合：保镖可见
        typeof(Game).GetProperty("Round")!.GetSetMethod(true)!.Invoke(g, new object[] { 5 });
        Assert.True(g.BuildView(b).ExtractionVisible);
        var k = g.Players.First(p => p.Role == RoleId.Killer);
        Assert.False(g.BuildView(k).ExtractionVisible);

        // 身份暴露：对全体开放
        g.ProtectedNpcs[0].Exposed = true;
        Assert.True(g.BuildView(k).ExtractionVisible);

        // 第10回合：无条件开放
        g.ProtectedNpcs[0].Exposed = false;
        typeof(Game).GetProperty("Round")!.GetSetMethod(true)!.Invoke(g, new object[] { 10 });
        Assert.True(g.BuildView(k).ExtractionVisible);
    }

    [Fact]
    public void Codes_AreRandomizedFromPool_AndUnique()
    {
        var firstCodes = new HashSet<string>();
        for (long seed = 100; seed <= 108; seed++)
        {
            var g = FourBots(seed);
            var codes = g.Actors.Select(a => a.Code).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count()); // 代号唯一
            Assert.DoesNotContain(codes, c => c.StartsWith("路人"));
            firstCodes.Add(g.Players[0].Code);
        }
        Assert.True(firstCodes.Count > 1, "代号序列应为随机，而非固定顺序");
    }
}

public class FlowTests
{
    private static CellPos SafeDest(Game g, CellPos from)
    {
        var cands = new[]
        {
            new CellPos(from.X, from.Y + 2),
            new CellPos(from.X + 1, from.Y),
            new CellPos(from.X - 1, from.Y),
            new CellPos(from.X, Math.Max(0, from.Y - 2)),
        };
        return cands.First(c => g.InMap(c));
    }

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
        var dest = SafeDest(g, p.Pos);
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
        var dest = SafeDest(g, p.Pos);
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

    [Fact]
    public void HealCard_CanHealSelf_WithoutTarget()
    {
        var g = GameWithOneHuman();
        g.Continue();
        Assert.Equal(AwaitKind.FreeAction, g.Await!.Kind);
        var p = g.Player(0);
        p.Hand.Add(new CardInstance { Id = 9999001, DefId = "heal" });
        var maxHp = GameConfig.Default().Role(p.Role.ToString().ToLowerInvariant()).Hp;
        p.Hp = Math.Max(1, maxHp - 1);
        var before = p.Hp;
        g.SubmitFree(0, new FreeCmd("card", CardId: 9999001));
        Assert.True(p.Hp > before, "治疗包应能治疗自己");
        Assert.False(p.Hand.Any(h => h.Id == 9999001), "治疗包应被消耗");
    }

    [Fact]
    public void Bodyguard_Cover_RedirectsNpcBattle_ToGuard()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 3;
        var g = new Game(cfg, 55, new[]
        {
            new SeatIn("A", true), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
        var b = g.Players.First(p => p.Role == RoleId.Bodyguard);
        var k = g.Players.First(p => p.Role == RoleId.Killer);
        var n = g.ProtectedNpcs[0];
        var cell = new CellPos(1, 1);
        b.Pos = cell;
        n.Pos = cell;
        k.Pos = new CellPos(1, 2);
        k.Hand.Add(new CardInstance { Id = 555001, DefId = "gun_temp" });
        b.CoverTargetId = n.Id;
        g.Continue();

        Assert.Contains(g.Archive, e => e.Op == "cover" && e.ActorId == b.Id); // 掩护指令被记录
        Assert.Contains(g.Archive, e => e.Op == "battle" && e.ActorId == k.Id && e.TargetCode == b.Code); // 战斗被转移到保镖
    }

    [Fact]
    public void Bodyguard_Injured_MedkitHintsIncludeNearbyCells()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 13;
        cfg.Map.Height = 13;
        cfg.Map.DecoyNpcCount = 2;
        var g = new Game(cfg, 56, new[]
        {
            new SeatIn("A", false), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
        var b = g.Players.First(p => p.Role == RoleId.Bodyguard);
        // 找一个有医疗包的位置
        var medCell = (CellPos?)null;
        for (int x = 0; x < 13 && medCell == null; x++)
            for (int y = 0; y < 13; y++)
                if (g.ItemsAt(new CellPos(x, y)).Any(i => i.Kind == ItemKind.Medkit))
                { medCell = new CellPos(x, y); break; }
        Assert.NotNull(medCell);
        b.Pos = medCell.Value;
        b.Hp = 1; // 受伤
        var v = g.BuildView(b);
        Assert.NotEmpty(v.MedkitHints);
        Assert.Contains(v.MedkitHints, h => h == $"[{medCell.Value.X},{medCell.Value.Y}]");

        b.Hp = GameConfig.Default().Role("bodyguard").Hp; // 满血不提示
        var v2 = g.BuildView(b);
        Assert.Empty(v2.MedkitHints);
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