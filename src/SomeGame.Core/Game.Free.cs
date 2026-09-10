using System.Text;

namespace SomeGame.Core;

public partial class Game
{
    // ---------- 效果工具 ----------
    public bool HasEffect(PlayerActor a, CardEffect fx) => a.Effects.Any(e => e.Effect == fx);
    public EffectState? GetEffect(PlayerActor a, CardEffect fx) => a.Effects.FirstOrDefault(e => e.Effect == fx);
    private static void AddEffect(PlayerActor a, CardEffect fx, int turns, string data = "")
        => a.Effects.Add(new EffectState { Effect = fx, TurnsRemaining = turns, Data = data });
    private static void AddEffect(NpcActor a, CardEffect fx, int turns, string data = "")
        => a.Effects.Add(new EffectState { Effect = fx, TurnsRemaining = turns, Data = data });
    private static void RemoveEffects(ActorNode a, CardEffect fx)
        => a.Effects.RemoveAll(e => e.Effect == fx);

    public bool HasEffect(ActorNode a, CardEffect fx)
        => a.Effects.Any(e => e.Effect == fx);

    private void ClearRoundEffects(PlayerActor p)
    {
        p.Effects.RemoveAll(e => e.Effect is CardEffect.Disguise or CardEffect.Mimic or CardEffect.Stim or CardEffect.Stealth or CardEffect.Aim or CardEffect.RapidFire);
        p.Effects.RemoveAll(e => e.Effect is CardEffect.Shield or CardEffect.Dye && --e.TurnsRemaining <= 0);
    }

    private static string Dir(CellPos from, CellPos to)
    {
        if (to.X > from.X) return "向东";
        if (to.X < from.X) return "向西";
        if (to.Y > from.Y) return "向南";
        if (to.Y < from.Y) return "向北";
        return "";
    }

    private bool TryStealthFor(ActorNode a, out bool wasHidden)
    {
        wasHidden = false;
        if (a is PlayerActor p && HasEffect(p, CardEffect.Stealth))
        {
            RemoveEffects(p, CardEffect.Stealth);
            wasHidden = true;
        }
        return wasHidden;
    }

    private ActionEvt MakeEvt(PlayerActor actor, string op, string v0, string? v1, string? v2, string? targetCode, bool hidden, CellPos at)
    {
        var e = ActionEvent(actor.Id, op, v0, v1, v2, targetCode, hidden, at);
        ApplyMimic(actor, e);
        return e;
    }

    private void ApplyMimic(PlayerActor actor, ActionEvt e)
    {
        var m = GetEffect(actor, CardEffect.Mimic);
        if (m == null) return;
        if (int.TryParse(m.Data, out var targetId) && ActorById(targetId) is { } t)
        {
            e.FakeMimic = true;
            e.TargetCode = t.Code;
            e.OpText = t.LastOpText.Length > 0 ? t.LastOpText : "在闲逛";
        }
    }

    // ---------- 移动 ----------
    private void DoMove(PlayerActor p, FreeCmd cmd)
    {
        if (p.MovedThisRound) { PushOut(p.SeatIndex, Msg("本回合已经移动过。")); return; }
        if (p.Ap < 1) { PushOut(p.SeatIndex, Msg("行动点不足。")); return; }
        var dest = new CellPos(cmd.X, cmd.Y);
        if (!InMap(dest)) { PushOut(p.SeatIndex, Msg("目标位置不在地图上。")); return; }
        var d = CellPos.Manhattan(p.Pos, dest);
        if (d < 1 || d > 3) { PushOut(p.SeatIndex, Msg("移动距离须为 1-3 格。")); return; }

        var hidden = TryStealthFor(p, out _);
        var start = p.Pos;
        p.MovedThisRound = true;
        p.Ap--;
        p.Pos = dest;
        var dir = Dir(start, dest);
        Log(MakeEvt(p, "move", $"{dir}移动", "移动了", "有人移动了", null, hidden, start));
        LogActorText(p, $"你{dir}移动至 {dest}。");
        DyeTrackMove(p);
        CheckArriveExtraction(p);
    }

    private void DyeTrackMove(PlayerActor mover)
    {
        var e = GetEffect(mover, CardEffect.Dye);
        if (e == null) return;
        if (int.TryParse(e.Data, out var casterSeat))
            PushOut(casterSeat, Msg($"染色目标「{mover.Code}」已移动至 {mover.Pos}。"));
        RemoveEffects(mover, CardEffect.Dye);
    }


    // ---------- 物品交互 ----------
    private void DoOpenChest(PlayerActor p, FreeCmd cmd)
    {
        var items = ItemsAt(p.Pos);
        var item = items.FirstOrDefault(i => i.Kind == ItemKind.Chest && (cmd.ItemId == null || cmd.ItemId == i.Id));
        if (item == null) { PushOut(p.SeatIndex, Msg("此格没有可开启的木箱。")); return; }
        if (p.Ap < 1) { PushOut(p.SeatIndex, Msg("行动点不足。")); return; }
        p.Ap--;
        if (item.TrappedGlue)
        {
            item.TrappedGlue = false;
            Log(MakeEvt(p, "open", "触发了木箱上的陷阱，被万能胶粘住", "碰到陷阱被粘住", "有人在木箱前停住了", null, false, p.Pos));
            LogActorText(p, "木箱上有万能胶陷阱！本回合你无法再行动。");
            p.Ap = 0;
            p.FinishedFree = true;
            return;
        }
        if (item.CardDefId.Length == 0)
        {
            Log(MakeEvt(p, "open", "翻找了一会木箱", "似乎在翻找东西", "有人在摆弄东西", null, false, p.Pos));
            return;
        }
        var cardName = Cfg.Card(item.CardDefId).Name;
        p.Hand.Add(new CardInstance { Id = ++_cardSeq, DefId = item.CardDefId });
        var hidden = HasEffect(p, CardEffect.Stealth) && item.CardDefId != "";
        TryStealthFor(p, out _);
        item.CardDefId = "";
        item.Consumed = true;
        Log(MakeEvt(p, "open", hidden ? "悄悄从木箱取走了什么" : $"从「木箱」中取走了什么", "似乎在从木箱拿东西", "有人在摆弄东西", null, hidden, p.Pos));
        LogActorText(p, $"你获得了技能卡：{cardName}。");
    }

    private void DoMedkit(PlayerActor p)
    {
        var items = ItemsAt(p.Pos);
        var item = items.FirstOrDefault(i => i.Kind == ItemKind.Medkit);
        if (item == null) { PushOut(p.SeatIndex, Msg("此格没有医疗包。")); return; }
        if (p.Ap < 1) { PushOut(p.SeatIndex, Msg("行动点不足。")); return; }
        p.Ap--;
        item.Consumed = true;
        HealActor(p, p);
        Log(MakeEvt(p, "medkit", "使用了医疗包治疗自己", "在处理伤口", "有人在摆弄东西", null, false, p.Pos));
        LogActorText(p, "你恢复 1 点 HP。");
    }

    // ---------- 交谈 ----------
    private void DoTalk(PlayerActor p, FreeCmd cmd)
    {
        if (p.Ap < 1) { PushOut(p.SeatIndex, Msg("行动点不足。")); return; }
if (cmd.ActorId == null) { PushOut(p.SeatIndex, Msg("请选择交谈对象。")); return; }
        var target = ActorById(cmd.ActorId.Value);
        if (target.Id == p.Id) { PushOut(p.SeatIndex, Msg("不能与自己交谈。")); return; }
        if (target.Dead || target.Pos != p.Pos)
        { PushOut(p.SeatIndex, Msg("交谈对象须与你同格。")); return; }
        p.Ap--;
        Log(MakeEvt(p, "talk", $"和「{target.Code}」交谈了一会儿", "在和谁说话", "有人在说话", target.Code, false, p.Pos));
    }

    // ---------- 高价值探查 ----------
    private void InspectResult(PlayerActor p)
    {
        if (p.InspectedThisRound) { PushOut(p.SeatIndex, Msg("本回合已探查过。")); return; }
        p.InspectedThisRound = true;
        var sb = new StringBuilder("附近的高价值物品：");
        bool any = false;
        for (int x = p.Pos.X - 1; x <= p.Pos.X + 1; x++)
            for (int y = p.Pos.Y - 1; y <= p.Pos.Y + 1; y++)
            {
                var c = new CellPos(x, y);
                if (!InMap(c)) continue;
                foreach (var i in ItemsAt(c))
                    if (i.Kind is ItemKind.Chest or ItemKind.Medkit)
                    {
                        sb.Append($" {c}「{i.Label}」");
                        any = true;
                    }
            }
        if (!any) sb.Append(" 无。");
        Log(MakeEvt(p, "inspect", "四下张望观察周围", "在四下张望", "有人在观察环境", null, false, p.Pos));
        PushOut(p.SeatIndex, Msg(sb.ToString()));
    }

// ---------- 掩护（保镖固定指令） ----------
    private void DoCover(PlayerActor p, FreeCmd cmd)
    {
        if (p.Role != RoleId.Bodyguard) { PushOut(p.SeatIndex, Msg("只有保镖能使用掩护。")); return; }
        if (cmd.ActorId == null) { PushOut(p.SeatIndex, Msg("请选择掩护对象。")); return; }
        var target = ActorById(cmd.ActorId.Value);
        if (target.Dead || target.Id == p.Id || target.Pos != p.Pos)
        { PushOut(p.SeatIndex, Msg("掩护对象须与你同格且存活。")); return; }
        p.CoverTargetId = target.Id;
        Log(MakeEvt(p, "cover", $"挺身掩护「{target.Code}」", "似乎在戒备什么", "有人在活动", target.Code, false, p.Pos));
        PushOut(p.SeatIndex, Msg($"你正在掩护「{target.Code}」，其受到的攻击将转移到你身上。"));
    }

    // ---------- 窃取 ----------
    private void DoSteal(PlayerActor p)
    {
        var npc = ProtectedNpcs.FirstOrDefault(n =>
            n.Pos == p.Pos && n.CaseId == p.CaseId && (n.ItemIntact) &&
            (!n.Dead || p.CarriedCase == -1));
        if (npc == null)
        {
            PushOut(p.SeatIndex, Msg("此格没有你目标案的贵宾（或财物已被取走）。"));
            return;
        }
        if (p.CarriedCase >= 0) { PushOut(p.SeatIndex, Msg("你已携带着财物。")); return; }
        if (p.Ap < 1) { PushOut(p.SeatIndex, Msg("行动点不足。")); return; }
        p.Ap--;
        npc.ItemIntact = false;
        p.CarriedCase = npc.CaseId;
        var hidden = TryStealthFor(p, out _);
        Log(MakeEvt(p, "steal", hidden ? "贴近贵宾，悄悄取走了某物" : "从贵宾身上窃取了财物！", "与贵宾十分贴近", "有人在活动", npc.Code, hidden, p.Pos));
        LogActorText(p, $"你窃取了 {npc.CaseColor} 案财物！现在前往撤离点核验。");
        PushOut(p.SeatIndex, Msg("你已窃得财物，需要活着抵达撤离点。"));
    }

    // ---------- 自由阶段使用卡牌 ----------
    private void DoUseCard(PlayerActor p, FreeCmd cmd, bool ignore)
    {
        _ = ignore;
        if (p.Ap < 1) { PushOut(p.SeatIndex, Msg("行动点不足。")); return; }
        var ci = p.Hand.FirstOrDefault(h => h.Id == cmd.CardId);
        if (ci == null) { PushOut(p.SeatIndex, Msg("手中没有这张卡。")); return; }
        var def = Cfg.Card(ci.DefId);
        if ((def.PhaseFlags & CardPhase.Free) == 0)
        {
            PushOut(p.SeatIndex, Msg($"{def.Name} 不能在自由行动阶段使用。"));
            return;
        }
        var hidden = HasEffect(p, CardEffect.Stealth) && def.Hideable;
        p.Ap--;
        ConsumeCard(p, ci);
        ApplyFreeEffect(p, def, cmd, hidden);
    }

    private void ApplyFreeEffect(PlayerActor p, CardDef def, FreeCmd cmd, bool hidden)
    {
        switch (def.Fx)
        {
            case CardEffect.Stealth:
                AddEffect(p, CardEffect.Stealth, 1);
                Log(MakeEvt(p, "card", "隐去身形", null, null, null, true, p.Pos));
                break;
            case CardEffect.Aim:
                AddEffect(p, CardEffect.Aim, 1);
                Log(MakeEvt(p, "card", "开始瞄准", "举起了什么", "有人在活动", null, false, p.Pos));
                break;
            case CardEffect.RapidFire:
                AddEffect(p, CardEffect.RapidFire, 1);
                Log(MakeEvt(p, "card", "调整射击模式", "似乎在准备什么", "有人在活动", null, false, p.Pos));
                break;
            case CardEffect.Stim:
                AddEffect(p, CardEffect.Stim, 1);
                Log(MakeEvt(p, "card", "注射了兴奋剂", "好像在给自己注射什么", "有人在活动", null, false, p.Pos));
                break;
            case CardEffect.Energy:
                p.Ap = Math.Min(Cfg.Turn.ApPerRound, p.Ap + def.Value);
                Log(MakeEvt(p, "card", "喝下能量饮料恢复精力", "在喝什么", "有人在活动", null, hidden, p.Pos));
                break;
            case CardEffect.Heal:
                ApplyHealTarget(p, cmd.ActorId, hidden);
                break;
            case CardEffect.AmmoPack:
                GrantTemp(p, "gun_temp", def.Count);
                Log(MakeEvt(p, "card", "拆开弹药包补充弹药", "在整理枪械", "有人在活动", null, hidden, p.Pos));
                break;
            case CardEffect.BladePack:
                GrantTemp(p, "knife_temp", def.Count);
                Log(MakeEvt(p, "card", "取出管制刀具", "似乎在藏起什么", "有人在活动", null, hidden, p.Pos));
                break;
            case CardEffect.Shield:
                AddEffect(p, CardEffect.Shield, 1);
                Log(MakeEvt(p, "card", "举起了防暴盾", "拿起了什么", "有人在活动", null, false, p.Pos));
                break;
            case CardEffect.Disguise:
                if (p.PubliclyHostile) { p.PubliclyHostile = false; Log(MakeEvt(p, "card", "重新整理伪装，抹去了暴露痕迹", "似乎换了一身装束", "有人在活动", null, hidden, p.Pos)); }
                else { AddEffect(p, CardEffect.Disguise, 1); Log(MakeEvt(p, "card", "戴上了伪装", "似乎在摆弄装束", "有人在活动", null, hidden, p.Pos)); }
                break;
            case CardEffect.Mimic:
                if (cmd.ActorId != null)
                {
                    AddEffect(p, CardEffect.Mimic, 1, cmd.ActorId.Value.ToString());
                    Log(MakeEvt(p, "card", "观察并模仿他人的举动", "在观察某人", "有人在活动", null, hidden, p.Pos));
                }
                break;
            case CardEffect.Bug:
                AddBugWatch(p.SeatIndex, p.Pos, def.Duration > 0 ? def.Duration : 3);
                Log(MakeEvt(p, "card", "在此格安置了窃听器", "似乎在摆弄什么", "有人在活动", null, hidden, p.Pos));
                break;
            case CardEffect.Drone:
                UseDrone(p, cmd);
                break;
            case CardEffect.Dye:
                if (cmd.ActorId != null && ActorById(cmd.ActorId.Value) is PlayerActor t2 && t2.Pos == p.Pos)
                {
                    AddEffect(t2, CardEffect.Dye, 2, p.SeatIndex.ToString());
                    Log(MakeEvt(p, "card", $"朝「{t2.Code}」扔出染色球", "扔出了什么", "有人在活动", t2.Code, hidden, p.Pos));
                }
                break;
            case CardEffect.Smoke:
                CastSmoke(p, hidden);
                break;
            case CardEffect.Molotov:
                CastMolotov(p, cmd, hidden);
                break;
            case CardEffect.Glue:
                CastGlue(p, cmd, hidden);
                break;
            case CardEffect.Gun:
            case CardEffect.Knife:
                PushOut(p.SeatIndex, Msg("攻击卡只能用于战斗。"));
                break;
            default:
                PushOut(p.SeatIndex, Msg("该效果暂未开放。"));
                break;
        }
    }

    private void ApplyHealTarget(PlayerActor healer, int? targetId, bool hidden)
    {
        ActorNode target;
        if (targetId == null) target = healer;
        else
        {
            target = ActorById(targetId.Value);
            if (target.Pos != healer.Pos || target.Dead) { PushOut(healer.SeatIndex, Msg("治疗对象须与你同格且存活。")); return; }
        }
        HealActor(healer, target);
        Log(MakeEvt(healer, "card", $"为「{target.Code}」进行治疗", "似乎在进行救治", "有人在活动", target.Code, hidden, healer.Pos));
        PushOut(healer.SeatIndex, Msg(target.Id == healer.Id ? "你恢复 1 点 HP。" : $"{target.Code} 恢复 1 点 HP。"));
    }

    private void HealActor(PlayerActor healer, ActorNode target)
    {
        var max = target.Kind switch
        {
            ActorKind.Player => Cfg.Role(((PlayerActor)target).Role.ToString().ToLowerInvariant()).Hp,
            ActorKind.ProtectedNpc => Cfg.ProtectedNpcHp,
            _ => target.Hp,
        };
        if (target.Hp < max) target.Hp++;
    }

    private void GrantTemp(PlayerActor p, string defId, int count)
    {
        for (int i = 0; i < count; i++)
            p.Hand.Add(new CardInstance { Id = ++_cardSeq, DefId = defId, Temp = true });
    }

    private void CastSmoke(PlayerActor p, bool hidden)
    {
        _smoke[p.Pos] = 2;
        Log(MakeEvt(p, "card", "掷出烟雾弹，浓烟弥漫此格", "似乎掷出了什么", "有人在活动", null, hidden, p.Pos));
        FleeNpcsFrom(p.Pos, "烟雾");
    }

    private void CastMolotov(PlayerActor p, FreeCmd cmd, bool hidden)
    {
        var cell = new CellPos(cmd.X, cmd.Y);
        if (!InMap(cell) || CellPos.Manhattan(p.Pos, cell) > 2)
        { PushOut(p.SeatIndex, Msg("燃烧瓶须投掷在距离 2 以内。")); return; }
        _burn[cell] = 2;
        Log(MakeEvt(p, "card", $"向 {cell} 投掷燃烧瓶", "投出了什么", "有人在活动", null, hidden, p.Pos));
        FleeNpcsFrom(cell, "火势");
    }

    private void CastGlue(PlayerActor p, FreeCmd cmd, bool hidden)
    {
        var item = ItemsAt(p.Pos).FirstOrDefault(i => cmd.ItemId == null || i.Id == cmd.ItemId);
        if (item == null) { PushOut(p.SeatIndex, Msg("请选择此格中的物品布置陷阱。")); return; }
        item.TrappedGlue = true;
        Log(MakeEvt(p, "card", $"在「{item.Label}」上布置了万能胶陷阱", "似乎在摆弄物品", "有人在摆弄东西", null, hidden, p.Pos));
    }

    private void UseDrone(PlayerActor p, FreeCmd cmd)
    {
        var cell = new CellPos(cmd.X, cmd.Y);
        if (!InMap(cell)) { PushOut(p.SeatIndex, Msg("目标格不在范围内。")); return; }
        var occupants = Occupants(cell).Select(a => a.Code).ToList();
        var items = ItemsAt(cell).Select(i => i.Label).ToList();
        AddBugWatch(p.SeatIndex, cell, 1, 0);
        PushOut(p.SeatIndex, Msg($"无人机探查 {cell}：角色 [{string.Join("、", occupants)}]；物品 [{string.Join("、", items)}]。"));
        Log(MakeEvt(p, "card", "放出无人机探查远处", "似乎在操控什么", "有人在活动", null, false, p.Pos));
    }

    private void FleeNpcsFrom(CellPos cell, string cause)
    {
        foreach (var n in Occupants(cell).OfType<NpcActor>().ToList())
        {
            if (n.Dead) continue;
            var neighbors = CellPos.OrthoAround(cell).Where(InMap).Where(c => !Actors.Any(a => !a.Dead && a.Pos == c)).ToList();
            if (n.Kind == ActorKind.ProtectedNpc && n.Pos == Extraction) continue;
            if (neighbors.Count == 0) continue;
            var dest = neighbors[Rng.Next(0, neighbors.Count)];
            Log(ActionEvent(n.Id, "flee", $"因{cause}慌忙移开", null, "有人在慌乱移动", null, false, cell));
            n.Pos = dest;
        }
    }

    private void AddBugWatch(int seat, CellPos cell, int rounds, int tier = 1)
    {
        _bugWatches.Add(new BugWatch(seat, cell, rounds, tier));
    }

    // ---------- 查验阶段卡牌 ----------
    private bool DoUseCheckCard(PlayerActor p, CardInstance ci, CheckCmd cmd)
    {
        var def = Cfg.Card(ci.DefId);
        if ((def.PhaseFlags & CardPhase.Check) == 0) return false;
        switch (def.Fx)
        {
            case CardEffect.Track:
            {
                var cell = new CellPos(cmd.X, cmd.Y);
                if (!InMap(cell)) return false;
                ConsumeCard(p, ci);
                var found = Occupants(cell).OfType<PlayerActor>()
                    .Where(x => HasEffect(x, CardEffect.Disguise) || HasEffect(x, CardEffect.Mimic)).ToList();
                if (found.Count > 0)
                {
                    foreach (var f in found)
                    {
                        RemoveEffects(f, CardEffect.Disguise);
                        RemoveEffects(f, CardEffect.Mimic);
                        PushOut(p.SeatIndex, Msg($"追踪探查发现「{f.Code}」使用过伪装/模仿，其身份是 {RoleDisplay(f)}。"));
                    }
                }
                else PushOut(p.SeatIndex, Msg("该格没有使用伪装/模仿的玩家。"));
                Log(MakeEvt(p, "card", "释放追踪探查扫描", "似乎在扫描什么", "有人在活动", null, false, p.Pos));
                break;
            }
            case CardEffect.Drone:
            {
                var cell = new CellPos(cmd.X, cmd.Y);
                if (!InMap(cell)) return false;
                ConsumeCard(p, ci);
                var occupants = Occupants(cell).Select(a => a.Code).ToList();
                var items = ItemsAt(cell).Select(i => i.Label).ToList();
                AddBugWatch(p.SeatIndex, cell, 1, 0);
                PushOut(p.SeatIndex, Msg($"无人机探查 {cell}：角色 [{string.Join("、", occupants)}]；物品 [{string.Join("、", items)}]。"));
                Log(MakeEvt(p, "card", "放出无人机探查远处", "似乎在操控什么", "有人在活动", null, false, p.Pos));
                break;
            }
            case CardEffect.Disguise:
                ConsumeCard(p, ci);
                if (p.PubliclyHostile) p.PubliclyHostile = false;
                else AddEffect(p, CardEffect.Disguise, 1);
                Log(MakeEvt(p, "card", "戴上了伪装", "似乎在摆弄装束", "有人在活动", null, true, p.Pos));
                break;
            case CardEffect.Heal:
                ConsumeCard(p, ci);
                ApplyHealTarget(p, cmd.TargetActorId, true);
                break;
            default:
                return false;
        }
        return true;
    }

    private void ConsumeCard(PlayerActor p, CardInstance ci)
    {
        p.Hand.Remove(ci);
        if (!ci.Temp) _discard.Enqueue(ci.DefId);
    }

    private LogOut Msg(string text) => new(-1, text, 0, null, null, "info", false);

    private void LogActorText(PlayerActor p, string text)
        => PushOut(p.SeatIndex, new LogOut(-1, text, 0, p.Pos.X, p.Pos.Y, "self", false));

    public void CheckArriveExtraction(PlayerActor p)
    {
        if (p.Pos != Extraction || p.Dead) return;
        if (CheckSettlement("撤离点核验") is { } fin) { Finish(fin.Reason); return; }
    }
}

