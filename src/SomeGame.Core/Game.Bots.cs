namespace SomeGame.Core;

public partial class Game
{
    private FreeCmd BotDecideFree(PlayerActor p)
    {
        var target = BotFreeTarget(p);
        bool hasAttackCard = p.Hand.Any(h => Cfg.Card(h.DefId).Fx is CardEffect.Gun or CardEffect.Knife);
        var maxHp = Cfg.Role(p.Role.ToString().ToLowerInvariant()).Hp;

        // 受重伤且持治疗卡 → 使用
        var healCard = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Heal);
        if (healCard != null && p.Hp <= maxHp - 2 && p.Ap >= 1) return new FreeCmd("card", CardId: healCard.Id);

        // 危急时优先拾取医疗包
        var med = ItemsAt(p.Pos).FirstOrDefault(i => i.Kind == ItemKind.Medkit);
        if (med != null && p.Hp <= maxHp - 1 && p.Ap >= 1) return new FreeCmd("medkit");

        // 与目标同格
        if (target != null && target.Pos == p.Pos)
        {
            if (p.Role == RoleId.Bodyguard && target is NpcActor { Kind: ActorKind.ProtectedNpc } npc &&
                p.CoverTargetId != npc.Id)
                return new FreeCmd("cover", ActorId: npc.Id);
            // 第5回合后催促贵宾移动（未暴露时靠催促推进）
            if (p.Role == RoleId.Bodyguard && target is NpcActor { Kind: ActorKind.ProtectedNpc } npc2 &&
                !npc2.Exposed && Round >= 5 && !npc2.Urged)
                return new FreeCmd("talk", ActorId: npc2.Id);
            if (p.Role == RoleId.Thief && p.CarriedCase < 0 && !p.MovedThisRound && p.Ap >= 1)
            {
                var vipNpc = target as NpcActor;
                if (vipNpc != null && (p.VerifiedVipCaseId == vipNpc.CaseId || vipNpc.Exposed))
                {
                    var stealthCard = p.Hand.FirstOrDefault(h => Cfg.Card(h.DefId).Fx == CardEffect.Stealth);
                    if (stealthCard != null && !HasEffect(p, CardEffect.Stealth))
                        return new FreeCmd("card", CardId: stealthCard.Id);
                    return new FreeCmd("steal");
                }
                // 未验出身份：不可窃取，等待查验阶段验证
            }
            var chest = ItemsAt(p.Pos).FirstOrDefault(i => i.Kind == ItemKind.Chest && i.CardDefId.Length > 0);
            if (chest != null && p.Ap >= 1 && !hasAttackCard) return new FreeCmd("open", ItemId: chest.Id);
            return new FreeCmd("finish");
        }

        // 补充行动：同格有木箱且缺卡 → 开箱；同格有平民 → 有概率交谈获取情报/卡
        if (p.Ap >= 1)
        {
            var chest2 = ItemsAt(p.Pos).FirstOrDefault(i => i.Kind == ItemKind.Chest && i.CardDefId.Length > 0);
            if (chest2 != null && (!hasAttackCard || Rng.Chance(0.3)))
                return new FreeCmd("open", ItemId: chest2.Id);
            var civilian = Occupants(p.Pos).OfType<NpcActor>()
                .FirstOrDefault(n => n.Kind == ActorKind.DecoyNpc && !n.Dead && n.Id != p.Id);
            if (civilian != null && Rng.Chance(0.6))
                return new FreeCmd("talk", ActorId: civilian.Id);
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
        Metrics.TotalPlayerCheckActions++;
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
            var enemy = Players.Where(x => !x.Dead && x.Id != p.Id && x.Role != RoleId.Madman)
                .OrderBy(x => CellPos.Manhattan(p.Pos, x.Pos)).FirstOrDefault();
            if (enemy != null && CellPos.Manhattan(p.Pos, enemy.Pos) <= 2) target = enemy;
            else
            {
                // 无敌人可及时可伤害平民NPC
                target = Decoys.Where(x => !x.Dead && CellPos.Manhattan(p.Pos, x.Pos) <= 2)
                    .OrderBy(x => CellPos.Manhattan(p.Pos, x.Pos)).FirstOrDefault();
            }
        }
        else if (p.Role == RoleId.Thief)
        {
            // 小偷查验出贵宾身份后才能窃取
            var vip = ProtectedNpcs.FirstOrDefault(n => n.CaseId == p.CaseId && !n.Dead);
            if (vip != null && p.VerifiedVipCaseId != vip.CaseId && !vip.Exposed &&
                CellPos.Manhattan(p.Pos, vip.Pos) <= 1)
            {
                var res = RevealCheck(p, vip);
                _ = res;
            }
            NextCheckSeat();
            return;
        }
        else { NextCheckSeat(); return; }

        if (target == null) { NextCheckSeat(); return; }
        var d = CellPos.Manhattan(p.Pos, target.Pos);
        // 杀手需先在场（距离<=1）查验确认贵宾身份，隔空无法验证
        if (target is NpcActor n && n.Kind == ActorKind.ProtectedNpc && p.CaseId == n.CaseId &&
            p.VerifiedVipCaseId != n.CaseId && !n.Exposed)
        {
            if (d <= 1)
            {
                var res = RevealCheck(p, target);
                _ = res;
            }
            else { NextCheckSeat(); return; } // 太远无法验证，先靠近
        }
        if (d <= 2 && CanStartBattleVs(p, target) && HasRangeAttack(p, d))
        {
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
