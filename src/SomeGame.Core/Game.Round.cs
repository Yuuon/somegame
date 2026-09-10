namespace SomeGame.Core;

public partial class Game
{
    private readonly List<BugWatch> _bugWatches = new();

    // ---------- NPC 回合移动 ----------
    private void NpcMoves()
    {
        foreach (var n in Decoys.Where(d => !d.Dead).ToList())
        {
            if (n.Dead || !Rng.Chance(0.45)) continue;
            var nb = CellPos.OrthoAround(n.Pos).Where(InMap)
                .Where(c => !Actors.Any(a => !a.Dead && a.Pos == c)).ToList();
            if (nb.Count == 0) continue;
            var old = n.Pos;
            n.Pos = nb[Rng.Next(0, nb.Count)];
            n.LastOpText = "在闲逛";
            Log(ActionEvent(n.Id, "move", "缓步走过", "移动了", "有人移动了", null, false, old));
        }

        foreach (var n in ProtectedNpcs.Where(p => !p.Dead).ToList())
        {
            if (n.Dead) continue;
            if (n.Pos == Extraction)
            {
                n.LastOpText = "在撤离点等候";
                continue;
            }

            // 危险格先避险（不影响后续移动判断）
            var inDanger = (_smoke.TryGetValue(n.Pos, out var s) && s > 0) || (_burn.TryGetValue(n.Pos, out var b) && b > 0);
            if (inDanger)
            {
                var nb = CellPos.OrthoAround(n.Pos).Where(InMap)
                    .Where(c => !Actors.Any(a => !a.Dead && a.Pos == c)).ToList();
                if (nb.Count > 0)
                {
                    var old = n.Pos;
                    n.Pos = nb[Rng.Next(0, nb.Count)];
                    Log(ActionEvent(n.Id, "move", "因危险慌忙移开", null, "有人慌忙移动", null, false, old));
                    continue;
                }
            }

            // 移动策略：撤离点对贵宾/保镖开放后（第5回合起）
            bool extractionOpen = Round >= 5;
            int towardSteps = 0;
            if (extractionOpen)
            {
                if (n.Exposed) towardSteps = n.Urged ? 2 : 1;
                else if (n.Urged) towardSteps = 1;
            }

            if (towardSteps > 0)
            {
                var path = ShortestPath(this, n.Pos, Extraction);
                int steps = Math.Min(towardSteps, path.Count);
                var old = n.Pos;
                if (steps > 0) n.Pos = path[steps - 1];
                n.LastOpText = "在赶路";
                Log(ActionEvent(n.Id, "move", "向撤离点方向赶路", "移动了", "有人移动了", null, false, old));
                if (n.Pos == Extraction)
                    AnnounceAll(new LogOut(-1, $"「{n.Code}」已抵达撤离点！", 0, null, null, "event", false));
            }
            else
            {
                // 未暴露且未被催促：随机移动一格或原地不动
                if (Rng.Chance(0.5))
                {
                    var nb = CellPos.OrthoAround(n.Pos).Where(InMap)
                        .Where(c => !Actors.Any(a => !a.Dead && a.Pos == c)).ToList();
                    if (nb.Count > 0)
                    {
                        var old = n.Pos;
                        n.Pos = nb[Rng.Next(0, nb.Count)];
                        n.LastOpText = "在徘徊";
                        Log(ActionEvent(n.Id, "move", "缓步徘徊", "移动了", "有人移动了", null, false, old));
                    }
                }
            }
        }

        EvacuateAtExtraction();
    }

    // 撤离点结算：格内无敌对玩家则立即撤离；否则原地等待满三回合
    private void EvacuateAtExtraction()
    {
        foreach (var n in ProtectedNpcs.Where(p => !p.Dead && p.Pos == Extraction && !p.Evacuated).ToList())
        {
            bool hostile = Players.Any(p => !p.Dead && p.Pos == Extraction && p.Role is RoleId.Killer or RoleId.Thief or RoleId.Madman);
            if (!hostile)
            {
                n.Evacuated = true;
                AnnounceAll(new LogOut(-1, $"「{n.Code}」已安全撤离！", 0, null, null, "event", false));
            }
            else
            {
                n.EvacWaitRounds++;
                if (n.EvacWaitRounds >= 3)
                {
                    n.Evacuated = true;
                    AnnounceAll(new LogOut(-1, $"「{n.Code}」在撤离点等待三回合后成功撤离！", 0, null, null, "event", false));
                }
                else
                {
                    AnnounceAll(new LogOut(-1, $"撤离点有敌对玩家，「{n.Code}」被迫等候（{n.EvacWaitRounds}/3）。", 0, null, null, "event", false));
                }
            }
        }
    }

    private void StepCellsTimers()
    {
        foreach (var k in _smoke.Keys.ToList())
        {
            _smoke[k]--;
            if (_smoke[k] <= 0) _smoke.Remove(k);
        }
        foreach (var k in _burn.Keys.ToList())
        {
            _burn[k]--;
            if (_burn[k] <= 0) _burn.Remove(k);
        }
        foreach (var w in _bugWatches.ToList())
        {
            w.Rounds--;
            if (w.Rounds <= 0) _bugWatches.Remove(w);
        }
    }

    private void RefillDiscardToMap()
    {
        var list = _discard.ToList();
        _discard.Clear();
        if (list.Count == 0) return;
        int placed = 0;
        foreach (var id in list)
        {
            var c = RandFreeCellNoItem();
            if (c.X < 0) continue;
            _items.TryAdd(c, new List<Item>());
            _items[c].Add(new Item { Id = ++_itemSeq, Kind = ItemKind.Chest, Pos = c, CardDefId = id });
            placed++;
        }
        if (placed > 0)
            AnnounceAll(new LogOut(-1, $"回收卡池已重新分布到地图（{placed} 张）。", 0, null, null, "event", false));
    }

    // ---------- 结算 ----------
    public (List<int> Winners, string Reason)? CheckSettlement(string ctx)
    {
        var winners = new List<int>();
        var parts = new List<string>();

        foreach (var n in ProtectedNpcs.Where(p => !p.Dead))
        {
            if (n.Evacuated && n.ItemIntact)
            {
                var guards = Players.Where(p => p.Role == RoleId.Bodyguard && p.CaseId == n.CaseId).ToList();
                foreach (var g in guards) if (!winners.Contains(g.SeatIndex)) winners.Add(g.SeatIndex);
                parts.Add($"{n.CaseColor}案贵宾已安全撤离");
            }
        }

        foreach (var t in Players.Where(p => !p.Dead && p.Role == RoleId.Thief && p.CarriedCase >= 0 && p.Pos == Extraction))
        {
            if (!winners.Contains(t.SeatIndex)) winners.Add(t.SeatIndex);
            parts.Add($"{PlanColor(t.CarriedCase)}案财物已被携至撤离点");
        }

        foreach (var n in ProtectedNpcs.Where(p => p.Dead))
        {
            foreach (var k in Players.Where(p => !p.Dead && p.Role == RoleId.Killer && p.CaseId == n.CaseId && p.Pos == Extraction))
            {
                if (!winners.Contains(k.SeatIndex)) winners.Add(k.SeatIndex);
                parts.Add($"{n.CaseColor}案贵宾已被击杀，杀手完成核验");
            }
        }

        var enemies = Players.Where(p => p.Role is RoleId.Bodyguard or RoleId.Killer or RoleId.Thief).ToList();
        if (enemies.All(p => p.Dead) && Players.Any(p => !p.Dead && p.Role == RoleId.Madman))
        {
            foreach (var m in Players.Where(p => p.Role == RoleId.Madman))
                if (!winners.Contains(m.SeatIndex)) winners.Add(m.SeatIndex);
            parts.Add("疯子清除了所有目标");
        }

        if (winners.Count > 0)
            return (winners, $"{ctx}：{string.Join("；", parts)}");
        return null;
    }

    private void Finish(string reason)
    {
        Terminal = true;
        Stage = Stage.Terminal;
        TerminalReason = reason;
        Await = null;
        var settlement = CheckSettlement(reason);
        Winners.Clear();
        if (settlement is { } s) Winners.AddRange(s.Winners);
        var results = Players.Select(p => new SeatResultOut
        {
            Seat = p.SeatIndex,
            Name = p.PlayerName,
            Role = RoleDisplay(p),
            Color = p.CaseId >= 0 ? PlanColor(p.CaseId) : "",
            Win = Winners.Contains(p.SeatIndex),
            Bot = p.IsBot,
        }).ToList();
        var reveal = Players.Select(p => $"{p.PlayerName} = {RoleDisplay(p)}{(p.CaseId >= 0 ? $"（{PlanColor(p.CaseId)}案）" : "")}").ToList();
        PushAll(new GameOverOut { Reason = reason, Results = results, Reveal = reveal });
        foreach (var p in Players)
            PushOut(p.SeatIndex, new LogOut(-1, $"对局结束：{reason}", 0, null, null, "event", false));
    }
}

public sealed class BugWatch
{
    public BugWatch(int seat, CellPos cell, int rounds, int tier = 1)
    {
        Seat = seat; Cell = cell; Rounds = rounds; Tier = tier;
    }
    public int Seat { get; }
    public CellPos Cell { get; }
    public int Rounds { get; set; }
    public int Tier { get; }
}
