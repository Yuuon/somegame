namespace SomeGame.Core;

public partial class Game
{
    private readonly List<PlayerActor> _freeQueue = new();
    private int _freeCursor;
    private readonly List<PlayerActor> _checkQueue = new();
    private int _checkCursor;
    private PlayerActor? _lastCheckPlayer;
    private int _lastCheckTargetId = -1;

    private void EnterFree()
    {
        Stage = Stage.Free;
        Battle = null;
        Await = null;
        foreach (var p in Players)
        {
            if (p.IsObserver) continue;
            p.Ap = Cfg.Turn.ApPerRound;
            p.FinishedFree = false;
            p.MovedThisRound = false;
            p.InspectedThisRound = false;
            p.CoverTargetId = -1; // 掩护每回合重新指定
            SupplyRoundCard(p);
        }
        _freeQueue.Clear();
        _freeQueue.AddRange(Players.Where(p => !p.IsObserver));
        _freeCursor = 0;
        PushAll(new PhaseOut { Stage = Stage.Free, Round = Round, Text = $"第 {Round} 回合 · 自由行动阶段" });
        PushAll(new LogOut(-1, $"第 {Round} 回合开始，自由行动阶段。", 0, null, null, "phase", false));
        EndFreeWhenAllDone();
    }

    private void SupplyRoundCard(PlayerActor p)
    {
        var def = Cfg.Role(p.Role.ToString().ToLowerInvariant());
        if (def.SupplyCard == null) return;
        var cdef = Cfg.Card(def.SupplyCard);
        p.Hand.Add(new CardInstance { Id = ++_cardSeq, DefId = cdef.Id, Temp = true });
        PushOut(p.SeatIndex, new LogOut(-1, $"你获得了本回合固定补给：{cdef.Name}", 0, null, null, "card", false));
    }

    private void EndFreeWhenAllDone()
    {
        while (_freeCursor < _freeQueue.Count && _freeQueue[_freeCursor].FinishedFree)
            _freeCursor++;
        if (_freeCursor >= _freeQueue.Count) EnterCheck();
    }

    private void EnterCheck()
    {
        Stage = Stage.Check;
        Await = null;
        _checkQueue.Clear();
        _checkQueue.AddRange(Players.Where(p => !p.IsObserver));
        _checkCursor = 0;
        PushAll(new PhaseOut { Stage = Stage.Check, Round = Round, Text = $"第 {Round} 回合 · 身份查验阶段" });
        AdvanceCheckQueue();
    }

    private void AdvanceCheckQueue()
    {
        if (Battle is { Active: true }) return;
        while (_checkCursor < _checkQueue.Count && _checkQueue[_checkCursor].IsObserver)
            _checkCursor++;
        if (_checkCursor >= _checkQueue.Count) { EndRound(); return; }
        if (_checkQueue[_checkCursor].IsBot) return; // 由 CheckStep 处理机器人
        _lastCheckPlayer = _checkQueue[_checkCursor];
        Await = new AwaitingInfo { Kind = AwaitKind.CheckAction, SeatIndex = _lastCheckPlayer.SeatIndex, Prompt = "选择查验目标，或使用查验阶段卡牌，或跳过。" };
    }

    /// <summary>推进直到等待人类玩家输入或对局终结。</summary>
    public void Continue()
    {
        var guard = 0;
        while (!Terminal && Await == null)
        {
            if (++guard > 50000) break;
            if (!AdvanceOneInternal()) break;
        }
        if (Terminal)
        {
            Stage = Stage.Terminal;
            Await = null;
        }
        PushViewAll();
    }

    private bool AdvanceOneInternal()
    {
        return Stage switch
        {
            Stage.Free => FreeStep(),
            Stage.Check => CheckStep(),
            Stage.Battle => BattleStep(),
            _ => false,
        };
    }

    // ---------- 自由行动 ----------
    private bool FreeStep()
    {
        if (_freeCursor >= _freeQueue.Count) { EnterCheck(); return true; }
        var p = _freeQueue[_freeCursor];
        if (p.FinishedFree || p.IsObserver) { _freeCursor++; EndFreeWhenAllDone(); return true; }
        if (p.IsBot)
        {
            var cmd = BotDecideFree(p);
            ExecuteFree(p, cmd);
            return true;
        }
        Await = new AwaitingInfo { Kind = AwaitKind.FreeAction, SeatIndex = p.SeatIndex, Prompt = "执行一项自由行动，或结束本轮行动。" };
        return false;
    }

    public void SubmitFree(int seat, FreeCmd cmd)
    {
        if (Await?.Kind != AwaitKind.FreeAction || Await.SeatIndex != seat) return;
        var p = Player(seat);
        Await = null;
        ExecuteFree(p, cmd);
        Continue();
    }

    private void ExecuteFree(PlayerActor p, FreeCmd cmd)
    {
        switch (cmd.Op)
        {
            case "move": DoMove(p, cmd); break;
            case "open": DoOpenChest(p, cmd); break;
            case "medkit": DoMedkit(p); break;
            case "talk": DoTalk(p, cmd); break;
            case "steal": DoSteal(p); break;
            case "cover": DoCover(p, cmd); break;
            case "card": DoUseCard(p, cmd, false); break;
            case "inspect": InspectResult(p); break;
            case "finish": p.FinishedFree = true; break;
            default: break;
        }
        NextAfterFree(p); // 行动后统一收尾：AP 归零自动结束本轮，否则保持本窗口
    }

    private void NextAfterFree(PlayerActor p)
    {
        if (p.Ap <= 0) p.FinishedFree = true;
        if (p.FinishedFree) { _freeCursor++; EndFreeWhenAllDone(); }
    }

    // ---------- 身份查验 ----------
    private bool CheckStep()
    {
        if (_checkCursor >= _checkQueue.Count) { EndRound(); return true; }
        var p = _checkQueue[_checkCursor];
        if (p.IsObserver) { _checkCursor++; AdvanceCheckQueue(); return true; }
        if (p.IsBot)
        {
            ExecuteBotCheck(p);
            return true;
        }
        if (Await?.Kind == AwaitKind.CheckAction && Await.SeatIndex == p.SeatIndex) return false;
        AdvanceCheckQueue();
        return false;
    }

    public void SubmitCheck(int seat, CheckCmd cmd)
    {
        if (Await == null) return;
        if (Await.Kind == AwaitKind.CheckConfirm && Await.SeatIndex == seat)
        {
            Await = null;
            if (cmd.StartBattle) TryStartBattle(Player(seat), _lastCheckTargetId);
            else NextCheckSeat();
            Continue();
            return;
        }
        if (Await.Kind != AwaitKind.CheckAction || Await.SeatIndex != seat) return;
        var p = Player(seat);
        Await = null;
        ExecuteCheck(p, cmd);
        Continue();
    }

    private void ExecuteCheck(PlayerActor p, CheckCmd cmd)
    {
        if (cmd.CardId is { } cid)
        {
            var ci = p.Hand.FirstOrDefault(h => h.Id == cid);
            if (ci != null) DoUseCheckCard(p, ci, cmd);
            NextCheckSeat();
            return;
        }
        if (cmd.Skip || cmd.TargetActorId == null) { NextCheckSeat(); return; }

        var target = ActorById(cmd.TargetActorId.Value);
        if (target.Dead) { NextCheckSeat(); return; }

        var res = RevealCheck(p, target);
        _lastCheckPlayer = p;
        _lastCheckTargetId = target.Id;
        PushOut(p.SeatIndex, res);

        var d = CellPos.Manhattan(p.Pos, target.Pos);
        var canBattle = d <= 2 && HasRangeAttack(p, d) && CanStartBattleVs(p, target);
        if (!canBattle)
        {
            if (target.Kind == ActorKind.Player && res.Enemy && d > 2)
                PushOut(p.SeatIndex, new LogOut(-1, "对方在可交战范围之外，无法开战。", 0, null, null, "check", false));
            if (!res.Enemy && target.Kind == ActorKind.Player)
                PushOut(p.SeatIndex, new LogOut(-1, "对方并非你的敌对身份。", 0, null, null, "check", false));
            NextCheckSeat();
            return;
        }
        Await = new AwaitingInfo { Kind = AwaitKind.CheckConfirm, SeatIndex = p.SeatIndex, Prompt = "对方是敌对目标，是否发起战斗？" };
    }

    private bool CanStartBattleVs(PlayerActor attacker, ActorNode target)
    {
        if (target.Kind == ActorKind.Player)
            return IsEnemyOf(Cfg, attacker.Role, ((PlayerActor)target).Role);
        if (target is NpcActor n && n.Kind == ActorKind.ProtectedNpc)
            return attacker.Role == RoleId.Killer && attacker.CaseId == n.CaseId;
        return false;
    }

    public CheckResultOut RevealCheck(PlayerActor verifier, ActorNode target)
    {
        var (identity, note) = ResolveIdentity(verifier, target);
        bool enemy = target is PlayerActor tp && IsEnemyOf(Cfg, verifier.Role, tp.Role);
        if (target is NpcActor n && n.Kind == ActorKind.ProtectedNpc && verifier.Role == RoleId.Killer)
            enemy = n.CaseId == verifier.CaseId;
        var d = CellPos.Manhattan(verifier.Pos, target.Pos);
        return new CheckResultOut
        {
            TargetCode = target.Code,
            Identity = identity,
            Enemy = enemy,
            RangeOk = d <= 2,
            CanBattle = enemy && d <= 2 && HasRangeAttack(verifier, d),
            Note = note,
        };
    }

    private (string Identity, string Note) ResolveIdentity(PlayerActor verifier, ActorNode target)
    {
        if (target.Kind != ActorKind.Player)
        {
            var npc = (NpcActor)target;
            if (npc.Kind == ActorKind.ProtectedNpc)
                return (npc.CaseColor + "案贵宾", npc.ItemIntact ? "随身财物尚在。" : "财物已被窃走。");
            return ("平民", "");
        }
        var p = (PlayerActor)target;
        var disguise = p.Effects.FirstOrDefault(e => e.Effect == CardEffect.Disguise);
        if (disguise != null)
        {
            var others = Occupants(p.Pos).Where(a => a.Id != p.Id && !a.Dead).ToList();
            if (others.Count > 0)
            {
                var chosen = others[Rng.Next(0, others.Count)];
                p.Effects.Remove(disguise);
                return (IdentityLabel(chosen), $"对方使用了伪装，你看到的是「{chosen.Code}」的身份");
            }
            p.Effects.Remove(disguise);
        }
        return (RoleDisplay(p), "");
    }

    private string IdentityLabel(ActorNode a)
    {
        if (a.Kind == ActorKind.Player) return RoleDisplay((PlayerActor)a);
        var n = (NpcActor)a;
        return n.Kind == ActorKind.ProtectedNpc ? n.CaseColor + "案贵宾" : "平民";
    }

    private static bool HasRangeAttack(PlayerActor p, int dist)
        => p.Hand.Any(h => h.DefId is "gun" or "gun_temp" || (dist <= 1 && h.DefId is "knife" or "knife_temp"));

    private void NextCheckSeat()
    {
        _checkCursor++;
        AdvanceCheckQueue();
    }

    // ---------- 回合收尾 ----------
    private void EndRound()
    {
        if (Terminal) return;
        foreach (var p in Players) ClearRoundEffects(p);
        NpcMoves();

        if (CheckSettlement("回合结束") is { } fin) { Finish(fin.Reason); return; }

        Round++;
        if (Round > Cfg.Turn.MaxRounds) { Finish("回合上限，未分胜负"); return; }
        StepCellsTimers();
        if (Round % Cfg.Turn.RefillMapEveryNRounds == 0) RefillDiscardToMap();
        EnterFree();
    }
}

public sealed record FreeCmd(string Op, long? CardId = null, int? ActorId = null, long? ItemId = null,
    int X = 0, int Y = 0);
public sealed record CheckCmd(bool Skip, int? TargetActorId = null, bool StartBattle = false,
    long? CardId = null, int X = 0, int Y = 0);
