namespace SomeGame.Core;

public partial class Game
{
    private PendingAttack? _pending;

    private void TryStartBattle(PlayerActor attacker, int targetId)
    {
        if (Battle is { Active: true }) return;
        var target = ActorById(targetId);
        if (target.Dead) { NextCheckSeat(); return; }

        ActorNode opponent = target;
        var d = CellPos.Manhattan(attacker.Pos, target.Pos);

        // 掩护转移：目标受保镖"掩护"指定且保镖同格时，战斗转向保镖
        PlayerActor? guard = null;
        if (target is NpcActor n && n.Kind == ActorKind.ProtectedNpc)
            guard = Players.FirstOrDefault(p => !p.Dead && p.Role == RoleId.Bodyguard &&
                p.CaseId == n.CaseId && p.Pos == n.Pos && p.CoverTargetId == n.Id);
        if (guard == null)
            guard = Players.FirstOrDefault(p => !p.Dead && p.Role == RoleId.Bodyguard &&
                p.CoverTargetId == target.Id && p.Pos == target.Pos);
        if (guard != null)
        {
            opponent = guard;
            Log(ActionEvent(guard.Id, "cover", "挺身掩护目标，挡下了攻击", null, "有人发生冲突", guard.Code, false, target.Pos));
        }

        Battle = new BattleState
        {
            Active = true,
            InitiatorActorId = attacker.Id,
            DefenderActorId = opponent.Id,
            CurrentActorId = attacker.Id,
            Distance = d,
            IsVsNpc = opponent is NpcActor,
            RoundNo = Round,
        };
        attacker.PubliclyHostile = true;
        Stage = Stage.Battle;

        PushOut(attacker.SeatIndex, new LogOut(-1, $"战斗开始：你 vs 「{opponent.Code}」（距离 {d}）。", 0, null, null, "battle", false));
        if (opponent is PlayerActor op)
            PushOut(op.SeatIndex, new LogOut(-1, $"你遭到「{attacker.Code}」的攻击！", 0, null, null, "battle", false));

        var e = ActionEvent(attacker.Id, "battle", "发动了战斗", null, "有人在冲突", opponent.Code, false, attacker.Pos);
        Log(e);
    }

    private bool BattleStep()
    {
        var b = Battle;
        if (b == null || !b.Active)
        {
            AfterBattleEnds();
            return true;
        }
        var current = ActorById(b.CurrentActorId);
        if (current is NpcActor)
        {
            DoBattlePass(current);
            return true;
        }
        var p = (PlayerActor)current;
        if (p.IsBot)
        {
            var cmd = BotDecideBattle(p, b);
            if (cmd is { Action: "play" } && cmd.CardId != null)
                DoBattlePlay(p, (long)cmd.CardId);
            else DoBattlePass(p);
            return true;
        }
        Await = new AwaitingInfo
        {
            Kind = AwaitKind.BattleAction,
            SeatIndex = p.SeatIndex,
            Prompt = b.IsVsNpc && ActorById(b.DefenderActorId) is NpcActor && ActorById(b.CurrentActorId) == ActorById(b.DefenderActorId)
                ? "（对手无牌）轮到你行动"
                : $"战斗轮到你：出牌或放弃（连续两次放弃则战斗中止）。",
        };
        return false;
    }

    public void SubmitBattleAction(int seat, BattleCmd cmd)
    {
        if (Await?.Kind != AwaitKind.BattleAction || Await.SeatIndex != seat) return;
        var p = Player(seat);
        Await = null;
        if (cmd.Action == "play" && cmd.CardId != null) DoBattlePlay(p, cmd.CardId.Value);
        else DoBattlePass(p);
        Continue();
    }

    private void DoBattlePass(ActorNode actor)
    {
        var b = Battle!;
        b.ConsecutivePasses++;
        if (b.ConsecutivePasses >= 2)
        {
            EndBattle("双方均无动作，战斗中止");
            return;
        }
        FlipCurrent();
    }

    private void FlipCurrent()
    {
        var b = Battle!;
        b.CurrentActorId = b.CurrentActorId == b.InitiatorActorId ? b.DefenderActorId : b.InitiatorActorId;
    }

    private void DoBattlePlay(PlayerActor p, long cardId)
    {
        var b = Battle!;
        var ci = p.Hand.FirstOrDefault(h => h.Id == cardId);
        if (ci == null) { DoBattlePass(p); return; }
        var def = Cfg.Card(ci.DefId);
        if ((def.PhaseFlags & CardPhase.Battle) == 0) { DoBattlePass(p); return; }

        var opponent = ActorById(b.CurrentActorId == b.InitiatorActorId ? b.DefenderActorId : b.InitiatorActorId);

        switch (def.Fx)
        {
            case CardEffect.Gun:
                if (b.Distance > 2) { DoBattlePass(p); return; }
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                DeclareAttack(p, opponent, ci.DefId, ranged: true);
                return;
            case CardEffect.Knife:
                if (b.Distance > 1) { DoBattlePass(p); return; }
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                DeclareAttack(p, opponent, ci.DefId, ranged: false);
                return;
            case CardEffect.Shield:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                AddEffect(p, CardEffect.Shield, 1);
                PushOut(p.SeatIndex, new LogOut(-1, "你举起防暴盾。", 0, null, null, "battle", false));
                FlipCurrent();
                return;
            case CardEffect.Stim:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                AddEffect(p, CardEffect.Stim, 1);
                PushOut(p.SeatIndex, new LogOut(-1, "你注射了兴奋剂（下一刀伤害 +1）。", 0, null, null, "battle", false));
                FlipCurrent();
                return;
            case CardEffect.Heal:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                HealActor(p, p);
                PushOut(p.SeatIndex, new LogOut(-1, "你在战斗中治疗自己 1 点 HP。", 0, null, null, "battle", false));
                FlipCurrent();
                return;
            case CardEffect.Aim:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                AddEffect(p, CardEffect.Aim, 1);
                FlipCurrent();
                return;
            case CardEffect.RapidFire:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                AddEffect(p, CardEffect.RapidFire, 1);
                FlipCurrent();
                return;
            case CardEffect.Smoke:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                EndBattle($"「{p.Code}」掷出烟雾弹撤退，战斗中断");
                _smoke[p.Pos] = 2;
                return;
            case CardEffect.Stealth:
                b.ConsecutivePasses = 0;
                ConsumeCard(p, ci);
                AddEffect(p, CardEffect.Stealth, 1);
                FlipCurrent();
                return;
            default:
                DoBattlePass(p);
                return;
        }
    }

    private void DeclareAttack(PlayerActor attacker, ActorNode defender, string cardDefId, bool ranged)
    {
        var b = Battle!;
        _pending = new PendingAttack(attacker, defender, ranged, cardDefId);

        var aim = HasEffect(attacker, CardEffect.Aim);
        if (aim) RemoveEffects(attacker, CardEffect.Aim);

        if (defender is PlayerActor d)
        {
            // 对手持瞄准等效果被攻击会失效（被命中即取消）
            if (HasEffect(d, CardEffect.Aim)) RemoveEffects(d, CardEffect.Aim);

            if (HasEffect(d, CardEffect.Shield))
            {
                RemoveEffects(d, CardEffect.Shield);
                PushOut(d.SeatIndex, new LogOut(-1, "防暴盾完全挡住了这次攻击！", 0, null, null, "battle", false));
                PushOut(attacker.SeatIndex, new LogOut(-1, $"「{d.Code}」用防暴盾挡住了攻击。", 0, null, null, "battle", false));
                b.ConsecutivePasses = 0;
                _pending = null;
                if (!d.Dead) FlipCurrent();
                return;
            }

            var hasDodge = d.Hand.Any(h => Cfg.Card(h.DefId).Fx == CardEffect.Dodge);
            if (hasDodge)
            {
                if (d.IsBot)
                {
                    var dodge = d.Hand.First(h => Cfg.Card(h.DefId).Fx == CardEffect.Dodge);
                    UseDodgeReaction(d, dodge);
                    return;
                }
                Await = new AwaitingInfo
                {
                    Kind = AwaitKind.BattleDefense,
                    SeatIndex = d.SeatIndex,
                    Prompt = "你遭到攻击，是否使用「闪避」？",
                };
                return;
            }
        }
        ResolvePending();
    }

    public void SubmitDefense(int seat, DefenseCmd cmd)
    {
        if (Await?.Kind != AwaitKind.BattleDefense || Await.SeatIndex != seat) return;
        var d = Player(seat);
        Await = null;
        if (cmd.Dodge && cmd.CardId != null)
        {
            var ci = d.Hand.FirstOrDefault(h => h.Id == cmd.CardId);
            if (ci != null && Cfg.Card(ci.DefId).Fx == CardEffect.Dodge) UseDodgeReaction(d, ci);
            else ResolvePending();
        }
        else ResolvePending();
        Continue();
    }

    private void UseDodgeReaction(PlayerActor d, CardInstance dodge)
    {
        var attacker = _pending!.Attacker;
        ConsumeCard(d, dodge);
        PushOut(d.SeatIndex, new LogOut(-1, "你闪避了这次攻击！", 0, null, null, "battle", false));
        PushOut(attacker.SeatIndex, new LogOut(-1, $"「{d.Code}」闪避了你的攻击。", 0, null, null, "battle", false));
        _pending = null;
        if (!d.Dead) FlipCurrent();
    }

    private void ResolvePending()
    {
        var pa = _pending!;
        _pending = null;
        var attacker = pa.Attacker;
        var defender = pa.Defender;
        var b = Battle!;

        if (pa.Ranged)
        {
            int rolls = HasEffect(attacker, CardEffect.RapidFire) ? 3 : 1;
            if (rolls == 3) RemoveEffects(attacker, CardEffect.RapidFire);
            bool aim = HasEffect(attacker, CardEffect.Aim);
            if (aim) RemoveEffects(attacker, CardEffect.Aim);
            int threshold = GunThreshold(attacker, defender);
            int hits = 0;
            var desc = new List<string>();
            for (int i = 0; i < rolls; i++)
            {
                if (aim || defender.Dead)
                {
                    hits++;
                    desc.Add("命中");
                    continue;
                }
                int r = Rng.Next(1, 7);
                bool success = r == 6 || (r != 1 && r >= threshold);
                if (success) { hits++; desc.Add($"掷{r}命中"); }
                else desc.Add($"掷{r}落空");
            }
            PushOut(attacker.SeatIndex, new LogOut(-1, $"枪械攻击（需求 {threshold}）：{string.Join("，", desc)}。", 0, null, null, "battle", false));
            if (defender is PlayerActor dp)
                PushOut(dp.SeatIndex, new LogOut(-1, $"你被枪械攻击（需求 {threshold}）：{string.Join("，", desc)}。", 0, null, null, "battle", false));
            DealDamage(attacker, defender, hits);
        }
        else
        {
            int dmg = 1;
            if (HasEffect(attacker, CardEffect.Stim)) dmg++;
            PushOut(attacker.SeatIndex, new LogOut(-1, $"刀械攻击，造成 {dmg} 点伤害。", 0, null, null, "battle", false));
            if (defender is PlayerActor dp)
                PushOut(dp.SeatIndex, new LogOut(-1, $"你被刀械攻击，造成 {dmg} 点伤害。", 0, null, null, "battle", false));
            DealDamage(attacker, defender, dmg);
        }
    }

    private int GunThreshold(PlayerActor attacker, ActorNode defender)
    {
        int t = 2;
        if (Occupants(defender.Pos).Count > 1) t++;
        if (ItemsAt(defender.Pos).Any(i => i.Kind == ItemKind.Cover)) t++;
        if (!HasEffect(attacker, CardEffect.Stealth)) t++;
        if (defender is PlayerActor pd && HasEffect(pd, CardEffect.Stim)) t++;
        return Math.Clamp(t, 2, 6);
    }

    private void DealDamage(PlayerActor attacker, ActorNode defender, int damage)
    {
        var b = Battle!;
        if (damage <= 0 || defender.Dead)
        {
            if (!defender.Dead) FlipCurrent();
            return;
        }
        defender.Hp -= damage;
        if (defender.Hp <= 0)
        {
            defender.Hp = 0;
            defender.Dead = true;
            Log(ActionEvent(attacker.Id, "kill", $"击杀了「{defender.Code}」", null, "有人倒下", defender.Code, false, defender.Pos));
            AnnounceAll(new LogOut(-1, $"「{defender.Code}」死亡！", 0, defender.Pos.X, defender.Pos.Y, "event", false));
            EndBattle($"「{defender.Code}」被击杀");
            return;
        }
        PushOut(attacker.SeatIndex, new LogOut(-1, $"「{defender.Code}」剩余 HP {defender.Hp}。", 0, null, null, "battle", false));
        if (defender is PlayerActor dp)
            PushOut(dp.SeatIndex, new LogOut(-1, $"你受到 {damage} 点伤害，剩余 HP {defender.Hp}。", 0, null, null, "battle", false));
        FlipCurrent();
    }

    private void EndBattle(string reason)
    {
        var b = Battle!;
        b.Active = false;
        b.EndReason = reason;
        Battle = null;
        PushAll(new LogOut(-1, $"战斗结束：{reason}", 0, null, null, "battle", false));
        AfterBattleEnds();
    }

    private void AfterBattleEnds()
    {
        Stage = Stage.Check;
        Await = null;
        if (CheckSettlement("战斗结算") is { } fin) { Finish(fin.Reason); return; }
        NextCheckSeat();
    }
}

public sealed record BattleCmd(string Action, long? CardId = null);
public sealed record DefenseCmd(bool Dodge, long? CardId = null);

internal sealed class PendingAttack
{
    public PendingAttack(PlayerActor attacker, ActorNode defender, bool ranged, string cardDefId)
    {
        Attacker = attacker; Defender = defender; Ranged = ranged; CardDefId = cardDefId;
    }
    public PlayerActor Attacker { get; }
    public ActorNode Defender { get; }
    public bool Ranged { get; }
    public string CardDefId { get; }
}
