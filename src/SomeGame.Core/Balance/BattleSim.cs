namespace SomeGame.Core.Balance;

/// <summary>基于配置卡池的 1v1 战斗蒙特卡洛模拟，规则与引擎战斗解析保持一致。</summary>
public static class BattleSim
{
    public sealed class Fighters
    {
        public List<string> Hand { get; } = new();
        public int Hp { get; set; }
        public readonly int MaxHp;
        public bool Stim;
        public bool ShieldCharge;
        public bool Aim;
        public bool RapidFire;

        public Fighters(int hp) { Hp = hp; MaxHp = hp; }
    }

    public sealed record Outcome(int Confrontations, int DamageDealt, int Turns, string End);

    public static List<string> BuildDeck(GameConfig cfg)
    {
        var deck = new List<string>();
        foreach (var q in cfg.Deck)
            for (int i = 0; i < q.Count; i++)
                deck.Add(q.CardId);
        return deck;
    }

    public static Fighters DrawFighter(GameConfig cfg, IRng rng, int hp, bool killerSupply, params string[] bonusTemp)
    {
        var deck = BuildDeck(cfg);
        var f = new Fighters(hp);
        for (int i = 0; i < cfg.HandSize && deck.Count > 0; i++)
        {
            var idx = rng.Next(0, deck.Count);
            f.Hand.Add(deck[idx]);
            deck.RemoveAt(idx);
        }
        if (killerSupply) f.Hand.Add("gun_temp");
        foreach (var t in bonusTemp) f.Hand.Add(t);
        return f;
    }

    private static bool UseCard(Fighters f, string defId)
    {
        return f.Hand.Remove(defId);
    }

    /// <summary>模拟一场战斗。distance=1 近身/2 枪战；crowd=true 表示目标格拥挤（命中阈值+1）。</summary>
    public static Outcome Battle(GameConfig cfg, IRng rng, Fighters a, Fighters b, int distance, bool crowd)
    {
        int confrontations = 0;
        int damage = 0;
        int turns = 0;
        int passes = 0;

        // 战斗前：机器人式预激活（兴奋剂/防暴盾/瞄准/连射）
        if (a.Hand.Contains("stim")) { a.Hand.Remove("stim"); a.Stim = true; }
        if (b.Hand.Contains("stim")) { b.Hand.Remove("stim"); b.Stim = true; }
        if (a.Hand.Contains("shield")) { a.Hand.Remove("shield"); a.ShieldCharge = true; }
        if (b.Hand.Contains("shield")) { b.Hand.Remove("shield"); b.ShieldCharge = true; }
        if (a.Hand.Contains("aim")) { a.Hand.Remove("aim"); a.Aim = true; }
        if (b.Hand.Contains("aim")) { b.Hand.Remove("aim"); b.Aim = true; }
        if (a.Hand.Contains("rapidfire")) { a.Hand.Remove("rapidfire"); a.RapidFire = true; }
        if (b.Hand.Contains("rapidfire")) { b.Hand.Remove("rapidfire"); b.RapidFire = true; }

        bool aTurn = true;
        while (a.Hp > 0 && b.Hp > 0 && turns < 200)
        {
            turns++;
            var me = aTurn ? a : b;
            var opp = aTurn ? b : a;
            var attack = PickAttack(me, distance);

            if (attack != null)
            {
                UseCard(me, attack);
                passes = 0;
                confrontations++;

                bool blocked = false;
                if (opp.ShieldCharge) { opp.ShieldCharge = false; blocked = true; }
                else if (opp.Hand.Contains("dodge")) { opp.Hand.Remove("dodge"); blocked = true; }

                if (!blocked)
                {
                    int dmg = ResolveAttack(rng, me, opp, attack, distance, crowd);
                    if (dmg > 0)
                    {
                        opp.Hp -= dmg;
                        damage += dmg;
                    }
                }
            }
            else
            {
                // 低血量且持有烟雾弹 → 撤退（战斗无胜负）
                if (me.Hp <= 2 && me.Hand.Contains("smoke"))
                {
                    UseCard(me, "smoke");
                    return new Outcome(confrontations, damage, turns, "retreat");
                }
                if (me.Hp < me.MaxHp && me.Hand.Contains("heal"))
                {
                    UseCard(me, "heal");
                    me.Hp++;
                    passes = 0;
                }
                else
                {
                    passes++;
                    if (passes >= 2)
                        return new Outcome(confrontations, damage, turns, "stalemate");
                }
            }
            aTurn = !aTurn;
        }

        if (a.Hp <= 0) return new Outcome(confrontations, damage, turns, "a_dead");
        if (b.Hp <= 0) return new Outcome(confrontations, damage, turns, "b_dead");
        return new Outcome(confrontations, damage, turns, "cap");
    }

    private static string? PickAttack(Fighters me, int distance)
    {
        if (distance <= 2)
        {
            if (me.Hand.Contains("gun")) return "gun";
            if (me.Hand.Contains("gun_temp")) return "gun_temp";
        }
        if (distance <= 1)
        {
            if (me.Hand.Contains("knife")) return "knife";
            if (me.Hand.Contains("knife_temp")) return "knife_temp";
        }
        return null;
    }

    private static int ResolveAttack(IRng rng, Fighters me, Fighters defender, string attack, int distance, bool crowd)
    {
        if (attack is "knife" or "knife_temp")
        {
            return me.Stim ? 2 : 1;
        }

        int rolls = me.RapidFire ? 3 : 1;
        if (me.RapidFire) me.RapidFire = false;
        int threshold = 2;
        if (crowd) threshold++;
        threshold++; // 未隐匿
        if (defender.Stim) threshold++;
        if (threshold > 6) threshold = 6;

        bool aim = me.Aim;
        if (aim) me.Aim = false;

        int hits = 0;
        for (int i = 0; i < rolls; i++)
        {
            if (aim) { hits++; continue; }
            int r = rng.Next(1, 7);
            if (r == 6 || (r != 1 && r >= threshold)) hits++;
        }
        return hits;
    }
}