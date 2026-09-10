using System.Text;

namespace SomeGame.Core.Balance;

public static class Report
{
    private static readonly string[] CombatCards = { "gun", "knife", "dodge", "shield", "stim", "aim", "rapidfire" };

    public static string ProduceMarkdown(GameConfig cfg, int battleSeeds, int stressSeeds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 卡池与对局数值报告");
        sb.AppendLine();
        sb.AppendLine($"> 由 `tools/SomeGame.Balance` 自动生成 · 卡池 {battleSeeds} 次 1v1 模拟 · 8人双案局 {stressSeeds} 局");
        sb.AppendLine();

        // ---------- 卡池构成 ----------
        var deck = BattleSim.BuildDeck(cfg);
        var total = deck.Count;
        int combat = 0;
        var counts = deck.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        foreach (var c in CombatCards) if (counts.TryGetValue(c, out var n)) combat += n;
        double combatFrac = (double)combat / total * 100;

        sb.AppendLine("## 1. 卡池构成");
        sb.AppendLine();
        sb.AppendLine($"- 总卡数：**{total}**（目标 90-120）");
        sb.AppendLine($"- 攻防类（枪械/刀械/闪避/防暴盾/兴奋剂/瞄准/连射）：**{combat}**（{combatFrac:F1}%，目标 55-60%）");
        sb.AppendLine();
        sb.AppendLine("| 卡牌 | 数量 | 类别 |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var g in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
        {
            var name = cfg.Card(g.Key).Name;
            var cat = CombatCards.Contains(g.Key) ? "攻防" : "特殊";
            sb.AppendLine($"| {name} | {g.Value} | {cat} |");
        }
        sb.AppendLine();

        // ---------- 1v1 模拟 ----------
        sb.AppendLine("## 2. 1v1 战斗模拟（目标：平均约 3 次攻防对抗，含临时补给）");
        sb.AppendLine();
        sb.AppendLine($"| 场景 | 次数 | 平均对抗 | 平均伤害 | 平均回合 | 一方阵亡 | 手牌耗尽/中止 | 烟雾撤退 |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (name, dist, crowd, hp, bonus) in new[]
                 {
                     ("枪战·开阔（距离2，无人拥挤）", 2, false, 5, Array.Empty<string>()),
                     ("枪战·杀手/弹药补给+1 临时枪（距离2）", 2, false, 5, new[] { "gun_temp" }),
                     ("枪战·弹药包满配+2 临时枪（距离2）", 2, false, 5, new[] { "gun_temp", "gun_temp" }),
                     ("近身·拥挤（距离1，目标格多人）", 1, true, 5, Array.Empty<string>()),
                     ("近身·管制刀具补给+1 临时刀（距离1）", 1, true, 5, new[] { "knife_temp" }),
                 })
        {
            var rng = Rand.Create(20240910);
            int confront = 0, damage = 0, turns = 0, dead = 0, stale = 0, retreat = 0;
            for (int i = 0; i < battleSeeds; i++)
            {
                var a = BattleSim.DrawFighter(cfg, rng, hp, false, bonus);
                var b = BattleSim.DrawFighter(cfg, rng, hp, false, bonus);
                var o = BattleSim.Battle(cfg, rng, a, b, dist, crowd);
                confront += o.Confrontations;
                damage += o.DamageDealt;
                turns += o.Turns;
                if (o.End is "a_dead" or "b_dead") dead++;
                else if (o.End == "stalemate") stale++;
                else if (o.End == "retreat") retreat++;
            }
            sb.AppendLine($"| {name} | {battleSeeds} | {(double)confront / battleSeeds:F2} | {(double)damage / battleSeeds:F2} | {(double)turns / battleSeeds:F1} | {(double)dead / battleSeeds * 100:F1}% | {(double)stale / battleSeeds * 100:F1}% | {(double)retreat / battleSeeds * 100:F1}% |");
        }
        sb.AppendLine();

        // ---------- 8 人双案局压力 ----------
        sb.AppendLine("## 3. 8 人双案局机器人对局（压力验证）");
        sb.AppendLine();
        var stress = RunStress(cfg, stressSeeds);
        sb.AppendLine($"- 局数：{stress.Seeds} · 全部收敛：{stress.Converged}/{stress.Seeds}（{stress.Converged * 100.0 / stress.Seeds:F0}%）");
        sb.AppendLine($"- 平均结束回合：{stress.AvgRounds:F1}（上限 {cfg.Turn.MaxRounds}）");
        sb.AppendLine($"- 平均每局战斗次数：{stress.AvgBattles:F1} · 平均每局死亡：{stress.AvgDeaths:F1}");
        sb.AppendLine();
        sb.AppendLine("| 结局 | 次数 |");
        sb.AppendLine("| --- | --- |");
        foreach (var (r, n) in stress.Reasons.OrderByDescending(x => x.Value))
            sb.AppendLine($"| {r} | {n} |");
        sb.AppendLine();

        // ---------- 对局过程指标 ----------
        sb.AppendLine("## 4. 对局过程指标（找目标耗时 / 行动密度）");
        sb.AppendLine();
        sb.AppendLine("| 局型 | 局数 | 平均回合 | 杀手首次接近贵宾 | 小偷首次接近贵宾 | 保镖首次接近贵宾 | 首次战斗 | 贵宾首次暴露 | 平均战斗数 | 人均自由行动/回合 | 人均查验/回合 |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        AppendProcessMetrics(sb, cfg, "4人局", 4, 12);
        AppendProcessMetrics(sb, cfg, "8人局", 8, 12);
        sb.AppendLine();

        sb.AppendLine("### 4.1 人均行动构成（自由行动操作分布）");
        sb.AppendLine();
        sb.AppendLine("| 局型 | 局数 | 行动总数 | 人均/回合 | 移动 | 交谈 | 开箱 | 探查 | 使用卡 | 医疗包 | 窃取/掩护/疾走 | 结束 |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        AppendActionDist(sb, cfg, "4人局", 4, 12);
        AppendActionDist(sb, cfg, "8人局", 8, 12);
        sb.AppendLine();

        return sb.ToString();
    }

    private static void AppendActionDist(System.Text.StringBuilder sb, GameConfig cfg, string label, int count, int seeds)
    {
        var seats = Enumerable.Range(0, count).Select(i => new SeatIn($"{label}{i}", true)).ToArray();
        var agg = new Dictionary<string, long>();
        long total = 0, rounds = 0;
        for (int s = 0; s < seeds; s++)
        {
            var g = new Game(cfg, 9000 + s, seats);
            g.Continue();
            rounds += g.Round;
            foreach (var (k, v) in g.Metrics.ActionCounts) agg[k] = v + (agg.TryGetValue(k, out var x) ? x : 0);
        }
        total = agg.Values.Sum();
        double per = total / Math.Max(1, rounds * count);
        long move = agg.GetValueOrDefault("move");
        long open = agg.GetValueOrDefault("open");
        long talk = agg.GetValueOrDefault("talk");
        long inspect = agg.GetValueOrDefault("inspect");
        long card = agg.GetValueOrDefault("card");
        long medkit = agg.GetValueOrDefault("medkit");
        long special = agg.GetValueOrDefault("steal") + agg.GetValueOrDefault("cover");
        long finish = agg.GetValueOrDefault("finish");
        sb.AppendLine($"| {label} | {seeds} | {total} | {per:F1} | {move} | {talk} | {open} | {inspect} | {card} | {medkit} | {special} | {finish} |");
    }

    private static void AppendProcessMetrics(System.Text.StringBuilder sb, GameConfig cfg, string label, int count, int seeds)
    {
        var seats = Enumerable.Range(0, count).Select(i => new SeatIn($"{label}{i}", true)).ToArray();
        double rounds = 0, battles = 0, freeActs = 0, checks = 0, firstBattle = 0, expose = 0;
        int killerN = 0, thiefN = 0, bodyN = 0;
        double killer = 0, thief = 0, body = 0;
        for (int s = 0; s < seeds; s++)
        {
            var g = new Game(cfg, 7000 + s, seats);
            g.Continue();
            rounds += g.Round;
            battles += g.Metrics.TotalBattles;
            freeActs += g.Metrics.TotalPlayerFreeActions;
            checks += g.Metrics.TotalPlayerCheckActions;
            if (g.Metrics.FirstKillerNearVipRound > 0) { killer += g.Metrics.FirstKillerNearVipRound; killerN++; }
            if (g.Metrics.FirstThiefNearVipRound > 0) { thief += g.Metrics.FirstThiefNearVipRound; thiefN++; }
            if (g.Metrics.FirstBodyguardNearVipRound > 0) { body += g.Metrics.FirstBodyguardNearVipRound; bodyN++; }
            if (g.Metrics.FirstBattleRound > 0) firstBattle += g.Metrics.FirstBattleRound;
            if (g.Metrics.FirstVipExposeRound > 0) expose += g.Metrics.FirstVipExposeRound;
        }
        sb.AppendLine($"| {label} | {seeds} | {rounds / seeds:F1} | {killer / (killerN > 0 ? killerN : 1):F1} | {thief / (thiefN > 0 ? thiefN : 1):F1} | {body / (bodyN > 0 ? bodyN : 1):F1} | {firstBattle / seeds:F1} | {expose / seeds:F1} | {battles / seeds:F1} | {freeActs / Math.Max(1, rounds * count):F1} | {checks / Math.Max(1, rounds * count):F1} |");
    }

    public sealed record StressReport(int Seeds, int Converged, double AvgRounds, double AvgBattles, double AvgDeaths, Dictionary<string, int> Reasons);

    public static StressReport RunStress(GameConfig cfg, int seeds)
    {
        int converged = 0;
        long rounds = 0, battles = 0, deaths = 0;
        var reasons = new Dictionary<string, int>();
        for (int s = 0; s < seeds; s++)
        {
            var seats = Enumerable.Range(0, 8).Select(i => new SeatIn($"S{i}", true)).ToArray();
            var g = new Game(cfg, 9000 + s, seats);
            g.Continue();
            if (g.Terminal) converged++;
            rounds += g.Round;
            battles += g.Archive.Count(e => e.Op == "battle");
            deaths += g.Archive.Count(e => e.Op == "kill");
            var reason = g.TerminalReason ?? "未结束";
            reasons[reason] = 1 + (reasons.TryGetValue(reason, out var v) ? v : 0);
        }
        return new StressReport(seeds, converged,
            (double)rounds / seeds, (double)battles / seeds, (double)deaths / seeds, reasons);
    }
}