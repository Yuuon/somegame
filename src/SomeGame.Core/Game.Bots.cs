namespace SomeGame.Core;

public partial class Game
{
    private FreeCmd BotDecideFree(PlayerActor p)
    {
        var target = BotFreeTarget(p);
        bool hasAttackCard = p.Hand.Any(h => Cfg.Card(h.DefId).Fx is CardEffect.Gun or CardEffect.Knife);

        // 危急时优先治疗
        var med = ItemsAt(p.Pos).FirstOrDefault(i => i.Kind == ItemKind.Medkit);
        if (med != null && p.Hp <= 2 && p.Ap >= 1) return new FreeCmd("medkit");

        // 与目标同格
        if (target != null && target.Pos == p.Pos)
        {
            if (p.Role == RoleId.Thief && p.CarriedCase < 0 && !p.MovedThisRound && p.Ap >= 1)
            {
                var stealthCard = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Stealth);
                if (stealthCard != null && !HasEffect(p, CardEffect.Stealth))
                    return new FreeCmd("card", CardId: stealthCard.Id);
                return new FreeCmd("steal");
            }
            var chest = ItemsAt(p.Pos).FirstOrDefault(i => i.Kind == ItemKind.Chest && i.CardDefId.Length > 0);
            if (chest != null && p.Ap >= 1 && !hasAttackCard) return new FreeCmd("open", ItemId: chest.Id);
            return new FreeCmd("finish");
        }

        if (target != null && !p.MovedThisRound && p.Ap >= 1)
        {
            var path = ShortestPath(this, p.Pos, target.Pos);
            if (path.Count > 0)
            {
                var steps = Math.Min(path.Count, 3);
                var dest = path[steps - 1];
                return new FreeCmd("move", X: dest.X, Y: dest.Y);
            }
        }
        if (!p.MovedThisRound && p.Ap >= 1)
        {
            // 漫无目的时朝撤离点方向移动，增加接触 NPC 的概率
            var path = ShortestPath(this, p.Pos, Extraction);
            if (path.Count > 0)
            {
                var steps = Math.Min(path.Count, 3);
                var dest = path[steps - 1];
                return new FreeCmd("move", X: dest.X, Y: dest.Y);
            }
        }
        return new FreeCmd("finish");
    }

    private ActorNode? BotFreeTarget(PlayerActor p)
    {
        switch (p.Role)
        {
            case RoleId.Bodyguard:
                return ProtectedNpcs.FirstOrDefault(n => n.CaseId == p.CaseId && !n.Dead);
            case RoleId.Killer:
                if (ProtectedNpcs.FirstOrDefault(n => n.CaseId == p.CaseId && !n.Dead) is { } k)
                    return k;
                return Players.Where(x => x.Id != p.Id && !x.Dead && x.Role == RoleId.Bodyguard)
                    .OrderBy(x => CellPos.Manhattan(p.Pos, x.Pos)).FirstOrDefault();
            case RoleId.Thief:
                if (p.CarriedCase >= 0) return null;
                return ProtectedNpcs.FirstOrDefault(n => n.CaseId == p.CaseId);
            case RoleId.Madman:
                return Players.Where(x => x.Id != p.Id && !x.Dead && x.Role != RoleId.Madman)
                    .OrderBy(x => CellPos.Manhattan(p.Pos, x.Pos)).FirstOrDefault();
            default:
                return null;
        }
    }

    private void ExecuteBotCheck(PlayerActor p)
    {
        ActorNode? target = null;
        if (p.Role == RoleId.Killer)
        {
            var npc = ProtectedNpcs.FirstOrDefault(n => n.CaseId == p.CaseId && !n.Dead);
            if (npc != null) target = npc;
            else target = Players.Where(x => !x.Dead && x.Role == RoleId.Bodyguard)
                .OrderBy(x => CellPos.Manhattan(p.Pos, x.Pos)).FirstOrDefault();
        }
        else if (p.Role == RoleId.Madman)
        {
            target = Players.Where(x => !x.Dead && x.Id != p.Id && x.Role != RoleId.Madman)
                .OrderBy(x => CellPos.Manhattan(p.Pos, x.Pos)).FirstOrDefault();
        }
        else { NextCheckSeat(); return; }

        if (target == null) { NextCheckSeat(); return; }
        var d = CellPos.Manhattan(p.Pos, target.Pos);
        if (d <= 2 && CanStartBattleVs(p, target) && HasRangeAttack(p, d))
        {
            if (target is NpcActor n && n.Kind == ActorKind.ProtectedNpc && p.CaseId == n.CaseId && !n.Dead)
            {
                TryStartBattle(p, target.Id);
                return;
            }
            // 先走查验（不向 bot 推送结果），随后开战
            var res = RevealCheck(p, target);
            _ = res;
            TryStartBattle(p, target.Id);
            return;
        }
        NextCheckSeat();
    }

    private BattleCmd BotDecideBattle(PlayerActor p, BattleState b)
    {
        var opponent = ActorById(b.CurrentActorId == b.InitiatorActorId ? b.DefenderActorId : b.InitiatorActorId);
        if (opponent.Dead) return new BattleCmd("pass");
        var gun = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Gun && b.Distance <= 2);
        if (gun != null) return new BattleCmd("play", gun.Id);
        var knife = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Knife && b.Distance <= 1);
        if (knife != null) return new BattleCmd("play", knife.Id);
        if (p.Hp < Cfg.Role(p.Role.ToString().ToLowerInvariant()).Hp)
        {
            var heal = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Heal);
            if (heal != null) return new BattleCmd("play", heal.Id);
        }
        var smoke = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Smoke);
        if (smoke != null && opponent is PlayerActor && p.Hp <= 2) return new BattleCmd("play", smoke.Id);
        return new BattleCmd("pass");
    }
}
