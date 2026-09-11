using System.Reflection;
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
    public void Board_CoversWholeMap_WithUnexploredCells()
    {
        var g = FourBots(1);
        var v = g.BuildView(g.Player(0));
        Assert.Equal(v.MapW * v.MapH, v.Cells.Count);
        Assert.Contains(v.Cells, c => c.Tier == -1); // 视野外为未探索格
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
        var vb = g.BuildView(b);
        Assert.True(vb.ExtractionVisible);
        Assert.True(vb.ExtractionExact); // 保镖获得精确坐标
        var k = g.Players.First(p => p.Role == RoleId.Killer);
        Assert.False(g.BuildView(k).ExtractionVisible);

        // 身份暴露：对全体开放（非保镖仍模糊）
        g.ProtectedNpcs[0].Exposed = true;
        var vk = g.BuildView(k);
        Assert.True(vk.ExtractionVisible);
        Assert.False(vk.ExtractionExact);

        // 第10回合：无条件开放（非保镖仍模糊）
        g.ProtectedNpcs[0].Exposed = false;
        typeof(Game).GetProperty("Round")!.GetSetMethod(true)!.Invoke(g, new object[] { 10 });
        var vk2 = g.BuildView(k);
        Assert.True(vk2.ExtractionVisible);
        Assert.False(vk2.ExtractionExact);
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

    [Fact]
    public void MedkitPickup_GrantsCard_NotInstantHeal()
    {
        var g = GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        var med = (CellPos?)null;
        for (int x = 0; x < 11 && med == null; x++)
            for (int y = 0; y < 11; y++)
                if (g.ItemsAt(new CellPos(x, y)).Any(i => i.Kind == ItemKind.Medkit))
                { med = new CellPos(x, y); break; }
        Assert.NotNull(med);
        p.Pos = med.Value;
        int before = p.Hp;
        g.SubmitFree(0, new FreeCmd("medkit"));
        Assert.Equal(before, p.Hp);                                  // 不立即治疗
        Assert.Contains(p.Hand, h => h.DefId == "medkit");           // 以卡片入手
    }

    [Fact]
    public void TempCards_AutoDiscarded_AtNextRoundStart()
    {
        var g = GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        p.Hand.Add(new CardInstance { Id = 9901, DefId = "gun_temp", Temp = true, Void = true }); // 虚无卡
        p.Hand.Add(new CardInstance { Id = 9902, DefId = "stealth", Temp = true, Void = false }); // 单消耗临时卡（保留）
        int guard = 0;
        while (!g.Terminal && g.Round < 2 && guard++ < 1000)
        {
            if (g.Stage == Stage.Free && g.Await?.SeatIndex == 0 && g.Await.Kind == AwaitKind.FreeAction)
                g.SubmitFree(0, new FreeCmd("finish"));
            else if (g.Stage == Stage.Check && g.Await?.SeatIndex == 0)
                g.SubmitCheck(0, new CheckCmd(Skip: true));
            else g.Continue();
        }
        Assert.False(p.Hand.Any(h => h.Id == 9901), "虚无卡应在回合结束时自动消耗");
        Assert.True(p.Hand.Any(h => h.Id == 9902), "单消耗临时卡应跨回合保留");
    }

    [Fact]
    public void NotableAction_BroadcastsVagueIntel_ToDistantPlayers()
    {
        var g = GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        Item? chest = null;
        CellPos chestCell = default;
        for (int x = 0; x < 11 && chest == null; x++)
            for (int y = 0; y < 11; y++)
            {
                var it = g.ItemsAt(new CellPos(x, y)).FirstOrDefault(i => i.Kind == ItemKind.Chest && i.CardDefId.Length > 0 && !i.TrappedGlue);
                if (it != null) { chest = it; chestCell = new CellPos(x, y); break; }
            }
        Assert.NotNull(chest);
        p.Pos = chestCell;
        var farBot = g.Players.First(x => x.IsBot);
        for (int x = 0; x < 11; x++)
            for (int y = 0; y < 11; y++)
                if (CellPos.Manhattan(chestCell, new CellPos(x, y)) > 2)
                { farBot.Pos = new CellPos(x, y); break; }
        g.DrainOutbox(farBot.SeatIndex); // 清空旧消息
        g.SubmitFree(0, new FreeCmd("open", ItemId: chest.Id));
        var drained = g.DrainOutbox(farBot.SeatIndex);
        Assert.Contains(drained, m => m is LogOut lo && lo.Kind == "vague");
    }

    [Fact]
    public void DashMove_Consumes2Ap_AllowsDistance5()
    {
        var g = GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        p.Ap = 3;
        CellPos dest = default;
        for (int x = 0; x < 11; x++)
            for (int y = 0; y < 11; y++)
                if (CellPos.Manhattan(p.Pos, new CellPos(x, y)) == 5) { dest = new CellPos(x, y); break; }
        g.SubmitFree(0, new FreeCmd("move", X: dest.X, Y: dest.Y, Dash: true));
        Assert.Equal(dest, p.Pos);
        Assert.Equal(1, p.Ap); // 3 - 2 = 1
        Assert.True(p.MovedThisRound);
    }

    [Fact]
    public void Bodyguard_Supply_TempMedkit_ExtraWhenInjured()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 2;
        var g = new Game(cfg, 7, new[]
        {
            new SeatIn("BG", false), new SeatIn("B1", true), new SeatIn("B2", true), new SeatIn("B3", true),
        }, shuffleRoles: false); // seat0 恒为保镖
        g.Continue();
        var p = g.Player(0);
        Assert.Equal(RoleId.Bodyguard, p.Role);
        Assert.Contains(p.Hand, h => h.DefId == "medkit_temp"); // 基础补给

        p.Hp = 1; // 自己受伤
        g.ProtectedOfCase(0).Hp = 1; // 贵宾受伤
        int guard = 0;
        while (g.Round < 2 && !g.Terminal && guard++ < 1000)
        {
            if (g.Stage == Stage.Free && g.Await?.SeatIndex == 0 && g.Await.Kind == AwaitKind.FreeAction)
                g.SubmitFree(0, new FreeCmd("finish"));
            else if (g.Stage == Stage.Check && g.Await?.SeatIndex == 0)
                g.SubmitCheck(0, new CheckCmd(Skip: true));
            else g.Continue();
        }
        Assert.True(g.Player(0).Hand.Count(h => h.DefId == "medkit_temp") >= 2, "受伤时额外生成一张临时医疗包");
    }

    [Fact]
    public void ExtractionHint_ExactForBodyguard_FuzzyForOthers()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 2;
        var g = new Game(cfg, 8, new[]
        {
            new SeatIn("BG", false), new SeatIn("K", true), new SeatIn("B2", true), new SeatIn("B3", true),
        }, shuffleRoles: false);
        typeof(Game).GetProperty("Round")!.GetSetMethod(true)!.Invoke(g, new object[] { 5 });
        var exact = $"[{g.Extraction.X},{g.Extraction.Y}]";
        Assert.True(g.BuildView(g.Player(0)).ExtractionVisible);
        Assert.Contains(exact, g.BuildView(g.Player(0)).ExtractionHint); // 保镖精确坐标

        var k = g.Player(1); // 杀手（未暴露）
        Assert.False(g.BuildView(k).ExtractionVisible);
        g.ProtectedNpcs[0].Exposed = true;
        Assert.True(g.BuildView(k).ExtractionVisible);
        Assert.DoesNotContain(exact, g.BuildView(k).ExtractionHint); // 他人保持模糊
    }

    [Fact]
    public void Thief_Steal_RequiresVerifyVip()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        var g = new Game(cfg, 5, new[]
        {
            new SeatIn("BG", true), new SeatIn("K", true), new SeatIn("T", false), new SeatIn("M", true),
        }, shuffleRoles: false); // seat2 恒为小偷（人类）
        g.Continue();
        int guard = 0;
        while (g.Stage != Stage.Terminal && (g.Await == null || g.Await.SeatIndex != 2) && guard++ < 300)
            g.Continue();
        Assert.Equal(2, g.Await!.SeatIndex);
        var t = g.Player(2);
        var npc = g.ProtectedOfCase(0);
        npc.Pos = t.Pos;
        t.Ap = 3;
        g.SubmitFree(2, new FreeCmd("steal"));
        Assert.Equal(-1, t.CarriedCase); // 未验出身份无法窃取
        t.VerifiedVipCaseId = 0;
        g.SubmitFree(2, new FreeCmd("steal"));
        Assert.Equal(0, t.CarriedCase); // 验出后可窃取
    }

    [Fact]
    public void PanicFleeAround_MarksCivilians()
    {
        var g = GameWithOneHuman();
        var decoy = g.Decoys.First();
        decoy.Pos = new CellPos(5, 5);
        g.PanicFleeAround(new CellPos(5, 5));
        Assert.True(decoy.PanicTurns > 0);
        Assert.Equal(new CellPos(5, 5), decoy.PanicSource);
    }

    [Fact]
    public void TalkToPanickedCivilian_RevealsCoords()
    {
        var g = GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        var decoy = g.Decoys.First();
        decoy.Pos = p.Pos;
        decoy.PanicTurns = 2;
        decoy.PanicSource = new CellPos(5, 7);
        g.DrainOutbox(0);
        g.SubmitFree(0, new FreeCmd("talk", ActorId: decoy.Id));
        var msgs = g.DrainOutbox(0);
        Assert.Contains(msgs, m => m is LogOut lo && lo.Text.Contains("[5,7]"));
    }

    [Fact]
    public void Vip_UsesNeutralMaskedCode()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 13;
        cfg.Map.Height = 13;
        var g = new Game(cfg, 1, new[]
        {
            new SeatIn("A", true), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
        var npc = g.ProtectedNpcs[0];
        Assert.DoesNotContain("案贵宾", npc.Code); // 对外掩盖身份，使用中立代号
        Assert.Contains(npc.Code, g.ObjectiveText(g.Players.First(p => p.Role == RoleId.Killer), "红"));
    }

    [Fact]
    public void CaseMembers_ReceiveVipRegionHint()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 13;
        cfg.Map.Height = 13;
        cfg.Map.DecoyNpcCount = 3;
        var g = new Game(cfg, 3, new[]
        {
            new SeatIn("A", true), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
        var k = g.Players.First(p => p.Role == RoleId.Killer);
        var npc = g.ProtectedOfCase(k.CaseId);
        var msgs = g.DrainOutbox(k.SeatIndex);
        Assert.Contains(msgs, m => m is LogOut lo && lo.Text.Contains(npc.Code) && lo.Text.Contains("大致在"));
    }

    [Fact]
    public void Metrics_Recorded_AfterGameRuns()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 13;
        cfg.Map.Height = 13;
        cfg.Map.DecoyNpcCount = 3;
        var g = new Game(cfg, 4, new[]
        {
            new SeatIn("A", true), new SeatIn("B", true), new SeatIn("C", true), new SeatIn("D", true),
        });
        g.Continue();
        Assert.True(g.Metrics.TotalPlayerFreeActions > 0);
        Assert.True(g.Metrics.TotalPlayerCheckActions > 0);
        Assert.True(g.Metrics.FirstBattleRound > 0 || g.Metrics.TotalBattles > 0);
        Assert.Contains(g.Metrics.ActionCounts, kv => kv.Key == "move");
    }

    [Fact]
    public void MedkitCard_CanHealOtherActor_InSameCell()
    {
        var g = GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        p.Hand.Add(new CardInstance { Id = 888001, DefId = "medkit" });
        var vip = g.ProtectedNpcs[0];
        vip.Pos = p.Pos;
        vip.Hp = 1;
        g.SubmitFree(0, new FreeCmd("card", CardId: 888001, ActorId: vip.Id));
        Assert.Equal(2, vip.Hp); // 医疗包可治疗同格其他角色
        Assert.False(p.Hand.Any(h => h.Id == 888001)); // 卡被消耗
    }

    [Fact]
    public void VipCheck_RequiresProximity_ToVerify()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        var g = new Game(cfg, 5, new[]
        {
            new SeatIn("BG", true), new SeatIn("K", true), new SeatIn("T", true), new SeatIn("M", true),
        }, shuffleRoles: false);
        var k = g.Player(1); // 杀手（案0）
        var npc = g.ProtectedOfCase(0);
        k.Pos = new CellPos(5, 5);
        npc.Pos = new CellPos(7, 5); // 距离2：隔空无法辨认
        var far = g.RevealCheck(k, npc);
        Assert.Equal("身份难辨", far.Identity);
        Assert.False(far.Enemy);
        Assert.Equal(-1, k.VerifiedVipCaseId);

        npc.Pos = new CellPos(6, 5); // 距离1：在场可验证
        var near = g.RevealCheck(k, npc);
        Assert.Equal("红案贵宾", near.Identity);
        Assert.True(near.Enemy);
        Assert.Equal(0, k.VerifiedVipCaseId);
    }

    [Fact]
    public void Mark_VisibleToSameCaseMembers_Only()
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        var g = new Game(cfg, 6, new[]
        {
            new SeatIn("BG", false), new SeatIn("K", true), new SeatIn("T", true), new SeatIn("M", true),
        }, shuffleRoles: false);
        g.Continue();
        var b = g.Player(0);
        b.Pos = new CellPos(4, 4);
        g.SubmitFree(0, new FreeCmd("mark", Msg: "这里有情况"));
        var k = g.Player(1);
        Assert.Contains(g.VisibleMarkers(k), m => m.X == 4 && m.Y == 4 && m.Text.Contains("这里有情况"));
        var mad = g.Player(3);
        Assert.DoesNotContain(g.VisibleMarkers(mad), m => m.X == 4 && m.Y == 4); // 疯子无同案，不可见
    }

    [Fact]
    public void Combat_Defaults_Configured()
    {
        var cfg = GameConfig.Default();
        Assert.Equal(2, cfg.Combat.GunDamage);
        Assert.Equal(2, cfg.Combat.KnifeDamage);
        Assert.Equal(1, cfg.Combat.StimKnifeBonus);
    }

    internal static Game GameWithOneHuman()
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

public class ReviewFixTests
{
    private static Game Roles4(bool seat0Human, long seed = 11)
    {
        var cfg = GameConfig.Default();
        cfg.Map.Width = 11;
        cfg.Map.Height = 11;
        cfg.Map.DecoyNpcCount = 2;
        return new Game(cfg, seed, new[]
        {
            new SeatIn("BG", !seat0Human), new SeatIn("K", true), new SeatIn("T", true), new SeatIn("M", true),
        }, shuffleRoles: false);
    }

    private static CellPos FarCell(Game g, CellPos from, int minDist)
    {
        for (int x = 0; x < g.Width; x++)
        for (int y = 0; y < g.Height; y++)
            if (CellPos.Manhattan(from, new CellPos(x, y)) >= minDist) return new CellPos(x, y);
        return new CellPos(0, 0);
    }

    // ---------- P0：伪装不泄露真实敌对性 ----------
    [Fact]
    public void Disguise_ShowsFakeIdentity_WithoutLeakingHostility()
    {
        var g = Roles4(true);
        var bg = g.Player(0);
        var killer = g.Player(1);
        var decoy = g.Decoys.First();
        killer.Effects.Add(new EffectState { Effect = CardEffect.Disguise, TurnsRemaining = 1 });
        killer.Pos = new CellPos(6, 6);
        decoy.Pos = new CellPos(6, 6); // 格内只有平民
        bg.Pos = new CellPos(4, 4);
        var res = g.RevealCheck(bg, killer);
        Assert.Equal("平民", res.Identity);
        Assert.False(res.Enemy, "伪装成平民时不得因真实身份为杀手而标记敌对");
        Assert.False(res.CanBattle);
    }

    [Fact]
    public void Disguise_EnemyShown_StillAllowsBattle()
    {
        var g = Roles4(true);
        var bg = g.Player(0);
        var killer = g.Player(1);
        var thief = g.Player(2);
        // 其余角色全部挪走，确保格内仅剩"被伪装者 + 被冒充者"，避免随机平民干扰
        foreach (var a in g.Actors.Where(a => a != killer && a != thief && !a.Dead))
            a.Pos = new CellPos(0, 9);
        killer.Effects.Add(new EffectState { Effect = CardEffect.Disguise, TurnsRemaining = 1 });
        killer.Pos = new CellPos(6, 6);
        thief.Pos = new CellPos(6, 6); // 被冒充为小偷（对保镖敌对）
        bg.Pos = new CellPos(4, 4);
        var res = g.RevealCheck(bg, killer);
        Assert.Equal("小偷", res.Identity);
        Assert.True(res.Enemy, "查验所见身份为敌对时仍应可开战");
    }

    // ---------- P0：撤离点坐标按需下发 ----------
    [Fact]
    public void Extraction_CoordsHidden_ForNonBodyguard()
    {
        var g = Roles4(false, 21);
        var k = g.Player(1);
        var v1 = g.BuildView(k);
        Assert.Equal(-1, v1.ExtractionX);
        Assert.Equal(-1, v1.ExtractionY);
        typeof(Game).GetProperty("Round")!.GetSetMethod(true)!.Invoke(g, new object[] { 10 });
        var v2 = g.BuildView(k);
        Assert.True(v2.ExtractionVisible);
        Assert.Equal(-1, v2.ExtractionX); // 第10回合公开后，非保镖仍不得拿到精确坐标
        var b = g.Player(0);
        typeof(Game).GetProperty("Round")!.GetSetMethod(true)!.Invoke(g, new object[] { 5 });
        var vb = g.BuildView(b);
        Assert.Equal(g.Extraction.X, vb.ExtractionX);
        Assert.Equal(g.Extraction.Y, vb.ExtractionY);
    }

    // ---------- P1：无效出牌不扣 AP/卡 ----------
    [Fact]
    public void Molotov_OutOfRange_DoesNotConsume()
    {
        var g = FlowTests.GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        var card = new CardInstance { Id = 777001, DefId = "molotov" };
        p.Hand.Add(card);
        int ap = p.Ap;
        var far = FarCell(g, p.Pos, 3);
        g.SubmitFree(0, new FreeCmd("card", CardId: card.Id, X: far.X, Y: far.Y));
        Assert.Equal(ap, p.Ap);
        Assert.Contains(p.Hand, h => h.Id == card.Id);
        Assert.Contains(g.DrainOutbox(0).OfType<LogOut>(), lo => lo.Text.Contains("燃烧瓶须投掷"));
    }

    [Fact]
    public void Drone_OutOfRange_DoesNotConsume()
    {
        var g = FlowTests.GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        var card = new CardInstance { Id = 777002, DefId = "drone" };
        p.Hand.Add(card);
        int ap = p.Ap;
        var far = FarCell(g, p.Pos, 11);
        g.SubmitFree(0, new FreeCmd("card", CardId: card.Id, X: far.X, Y: far.Y));
        Assert.Equal(ap, p.Ap);
        Assert.Contains(p.Hand, h => h.Id == card.Id);
    }

    [Fact]
    public void Glue_NoItemInCell_DoesNotConsume()
    {
        var g = FlowTests.GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        CellPos empty = default;
        bool found = false;
        for (int x = 0; x < 11 && !found; x++)
        for (int y = 0; y < 11 && !found; y++)
        {
            var c = new CellPos(x, y);
            if (g.ItemsAt(c).Count == 0 && c != g.Extraction) { empty = c; found = true; }
        }
        Assert.True(found);
        p.Pos = empty;
        var card = new CardInstance { Id = 777003, DefId = "glue" };
        p.Hand.Add(card);
        int ap = p.Ap;
        g.SubmitFree(0, new FreeCmd("card", CardId: card.Id));
        Assert.Equal(ap, p.Ap);
        Assert.Contains(p.Hand, h => h.Id == card.Id);
    }

    [Fact]
    public void Heal_FarTarget_DoesNotConsume()
    {
        var g = FlowTests.GameWithOneHuman();
        g.Continue();
        var p = g.Player(0);
        var farBot = g.Players.First(x => x.IsBot);
        farBot.Pos = FarCell(g, p.Pos, 4);
        var card = new CardInstance { Id = 777004, DefId = "heal" };
        p.Hand.Add(card);
        int ap = p.Ap;
        g.SubmitFree(0, new FreeCmd("card", CardId: card.Id, ActorId: farBot.Id));
        Assert.Equal(ap, p.Ap);
        Assert.Contains(p.Hand, h => h.Id == card.Id);
    }

    // ---------- P1：瞄准/隐匿在攻击结算后消耗 ----------
    [Fact]
    public void AimAndStealth_ConsumedOnAttack_GunHits()
    {
        var g = FlowTests.GameWithOneHuman();
        var p = g.Player(0);
        var d = g.Player(1);
        p.Pos = new CellPos(5, 5);
        d.Pos = new CellPos(7, 5); // 距离2
        d.Hand.Clear(); // 排除闪避等响应卡
        d.Hp = 5; // 机器人角色血量不一，统一为 5 便于断言
        var card = new CardInstance { Id = 555101, DefId = "gun_temp" };
        p.Hand.Add(card);
        p.Effects.Add(new EffectState { Effect = CardEffect.Aim, TurnsRemaining = 1 });
        p.Effects.Add(new EffectState { Effect = CardEffect.Stealth, TurnsRemaining = 1 });
        typeof(Game).GetMethod("TryStartBattle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(g, new object[] { p, d.Id });
        g.Continue();
        Assert.Equal(AwaitKind.BattleAction, g.Await!.Kind);
        g.SubmitBattleAction(0, new BattleCmd("play", 555101));
        Assert.DoesNotContain(p.Effects, e => e.Effect == CardEffect.Aim);
        Assert.DoesNotContain(p.Effects, e => e.Effect == CardEffect.Stealth);
        Assert.Equal(5 - g.Cfg.Combat.GunDamage, d.Hp); // 瞄准必中
    }

    // ---------- P1：标注/掩护每回合上限 ----------
    [Fact]
    public void Mark_LimitedToTwoPerRound()
    {
        var g = Roles4(true);
        g.Continue();
        var p = g.Player(0);
        g.SubmitFree(0, new FreeCmd("mark", Msg: "m1"));
        g.SubmitFree(0, new FreeCmd("mark", Msg: "m2"));
        g.SubmitFree(0, new FreeCmd("mark", Msg: "m3"));
        Assert.Equal(2, g.VisibleMarkers(p).Count);
        Assert.Contains(g.DrainOutbox(0).OfType<LogOut>(), lo => lo.Text.Contains("标注次数已用完"));
    }

    [Fact]
    public void Cover_LimitedToOncePerRound()
    {
        var g = Roles4(true);
        g.Continue();
        var p = g.Player(0);
        var decoy = g.Decoys.First();
        decoy.Pos = p.Pos;
        g.SubmitFree(0, new FreeCmd("cover", ActorId: decoy.Id));
        Assert.True(p.CoverUsedThisRound);
        g.SubmitFree(0, new FreeCmd("cover", ActorId: decoy.Id));
        Assert.Contains(g.DrainOutbox(0).OfType<LogOut>(), lo => lo.Text.Contains("本回合已使用过掩护"));
    }

    // ---------- P1：万能胶可在任意可交互物品上触发 ----------
    private static (Game g, PlayerActor p) HumanGame()
    {
        var g = FlowTests.GameWithOneHuman();
        g.Continue();
        return (g, g.Player(0));
    }

    [Fact]
    public void GlueTrap_OnMedkit_TriggersOnPickup()
    {
        var (g, p) = HumanGame();
        CellPos cell = default;
        Item? item = null;
        for (int x = 0; x < 11 && item == null; x++)
        for (int y = 0; y < 11 && item == null; y++)
        {
            var c = new CellPos(x, y);
            item = g.ItemsAt(c).FirstOrDefault(i => i.Kind == ItemKind.Medkit);
            if (item != null) cell = c;
        }
        Assert.NotNull(item);
        p.Pos = cell;
        item!.TrappedGlue = true;
        item.TrappedById = g.Players.First(x => x.IsBot).Id;
        p.Ap = 3;
        g.SubmitFree(0, new FreeCmd("medkit"));
        Assert.True(p.FinishedFree, "触发陷阱后本回合应结束");
        Assert.Equal(0, p.Ap);
        Assert.False(item.TrappedGlue, "陷阱应被消耗");
        Assert.DoesNotContain(p.Hand, h => h.DefId == "medkit");
    }

    [Fact]
    public void GlueTrap_OnInspect_Triggers()
    {
        var (g, p) = HumanGame();
        CellPos cell = default;
        Item? item = null;
        for (int x = 0; x < 11 && item == null; x++)
        for (int y = 0; y < 11 && item == null; y++)
        {
            var c = new CellPos(x, y);
            item = g.ItemsAt(c).FirstOrDefault(i => i.Kind == ItemKind.Chest);
            if (item != null) cell = c;
        }
        Assert.NotNull(item);
        p.Pos = cell;
        item!.TrappedGlue = true;
        item.TrappedById = g.Players.First(x => x.IsBot).Id;
        p.Ap = 3;
        g.SubmitFree(0, new FreeCmd("inspect"));
        Assert.True(p.FinishedFree, "探查到陷阱应触发粘住");
        Assert.False(item.TrappedGlue);
    }

    [Fact]
    public void GlueTrap_Owner_DoesNotTrigger()
    {
        var (g, p) = HumanGame();
        CellPos cell = default;
        Item? item = null;
        for (int x = 0; x < 11 && item == null; x++)
        for (int y = 0; y < 11 && item == null; y++)
        {
            var c = new CellPos(x, y);
            item = g.ItemsAt(c).FirstOrDefault(i => i.Kind == ItemKind.Chest);
            if (item != null) cell = c;
        }
        Assert.NotNull(item);
        p.Pos = cell;
        item!.TrappedGlue = true;
        item.TrappedById = p.Id; // 布置者本人
        p.Ap = 3;
        g.SubmitFree(0, new FreeCmd("inspect"));
        Assert.False(p.FinishedFree, "布置者本人探查不应触发");
        Assert.True(item.TrappedGlue, "陷阱应保留给他人");
    }

    // ---------- P1：查验阶段用错卡不消耗且保留本轮 ----------
    [Fact]
    public void CheckCard_Invalid_KeepsTurnAndCard()
    {
        var g = FlowTests.GameWithOneHuman();
        while (g.Stage == Stage.Free)
        {
            if (g.Await?.Kind == AwaitKind.FreeAction) g.SubmitFree(0, new FreeCmd("finish"));
            else g.Continue();
        }
        Assert.Equal(Stage.Check, g.Stage);
        var p = g.Player(0);
        var card = new CardInstance { Id = 888002, DefId = "gun" }; // 仅战斗阶段可用
        p.Hand.Add(card);
        int count = p.Hand.Count;
        g.SubmitCheck(0, new CheckCmd(Skip: false, CardId: 888002));
        Assert.Equal(AwaitKind.CheckAction, g.Await!.Kind);
        Assert.Equal(0, g.Await.SeatIndex);
        Assert.Equal(count, p.Hand.Count);
        g.SubmitCheck(0, new CheckCmd(Skip: true));
    }
}