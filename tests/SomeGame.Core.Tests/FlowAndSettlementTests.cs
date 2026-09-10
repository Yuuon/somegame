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