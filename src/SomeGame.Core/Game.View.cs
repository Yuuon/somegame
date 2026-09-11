namespace SomeGame.Core;

public partial class Game
{
    private void PushViewAll()
    {
        foreach (var p in Players)
            PushOut(p.SeatIndex, BuildView(p));
    }

    public ViewOut BuildView(PlayerActor p)
    {
        var awaiting = Await?.SeatIndex == p.SeatIndex;
        var inBattle = Battle is { Active: true } &&
                       (p.Id == Battle.InitiatorActorId || p.Id == Battle.DefenderActorId);

        var cells = new List<CellOut>();
        var center = p.Pos;
        // 棋盘始终渲染整张地图：视野外为未探索空白（tier=-1），便于移动/疾走/无人机等远距离选择
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var c = new CellPos(x, y);
            var d = CellPos.Manhattan(center, c);
            int tier = p.IsObserver ? 0 : (d > 2 ? -1 : d);
            var occList = new List<string>();
            var panicked = new List<string>();
            if (tier >= 0)
            {
                var occ = Occupants(c);
                if (tier <= 1 || p.IsObserver)
                    foreach (var a in occ)
                    {
                        occList.Add(a.Code);
                        if (a is NpcActor dn && dn.Kind == ActorKind.DecoyNpc && dn.PanicTurns > 0)
                            panicked.Add(a.Code);
                    }
                else if (occ.Count > 0)
                    occList.Add($"{occ.Count}人");
            }

            var itemList = new List<string>();
            var interactables = new List<ItemRefOut>();
            if (tier == 0)
            {
                foreach (var i in ItemsAt(c))
                {
                    itemList.Add(i.Label);
                    if (i.Kind is ItemKind.Chest or ItemKind.Medkit)
                        interactables.Add(new ItemRefOut
                        {
                            Id = i.Id,
                            Label = ItemLabelFor(p, i),
                            Kind = i.Kind.ToString(),
                        });
                }
            }
            else if (tier == 1 && ItemsAt(c).Count > 0)
                itemList.Add("有物品");

            cells.Add(new CellOut
            {
                X = x, Y = y, Tier = tier,
                Occupants = occList,
                Panicked = panicked,
                Items = itemList,
                Interactables = interactables,
                Burning = tier >= 0 && _burn.TryGetValue(c, out var bb) && bb > 0,
                Smoky = tier >= 0 && _smoke.TryGetValue(c, out var ss) && ss > 0,
            });
        }

        var opponent = inBattle
            ? ActorById(p.Id == Battle!.InitiatorActorId ? Battle.DefenderActorId : Battle.InitiatorActorId)
            : null;
        var battlePrompt = opponent != null
            ? $"战斗 vs「{opponent.Code}」 距离{Battle!.Distance} 对方HP {opponent.Hp}" +
              (p.Id == Battle.CurrentActorId ? " · 轮到你" : " · 等待对手")
            : "";
        var oppMax = opponent switch
        {
            PlayerActor pp => Cfg.Role(pp.Role.ToString().ToLowerInvariant()).Hp,
            NpcActor { Kind: ActorKind.ProtectedNpc } => Cfg.ProtectedNpcHp,
            _ => 1,
        };

        var medkitHints = new List<string>();
        if (p.Role == RoleId.Bodyguard)
        {
            var maxHp = Cfg.Role("bodyguard").Hp;
            if (p.Hp < maxHp)
            {
                for (int x = p.Pos.X - 2; x <= p.Pos.X + 2; x++)
                for (int y = p.Pos.Y - 2; y <= p.Pos.Y + 2; y++)
                {
                    var c = new CellPos(x, y);
                    if (InMap(c) && CellPos.Manhattan(p.Pos, c) <= 2 &&
                        ItemsAt(c).Any(i => i.Kind == ItemKind.Medkit))
                        medkitHints.Add($"[{x},{y}]");
                }
            }
        }

        // 撤离点可见性：前5回合保密；第5回合开放给保镖；暴露即对全体开放；第10回合无条件开放
        bool extractionVisible = Round >= 10 ||
            (Round >= 5 && (p.Role == RoleId.Bodyguard || ProtectedNpcs.Any(n => n.Exposed)));
        string extractionHint = extractionVisible
            ? (p.Role == RoleId.Bodyguard
                ? $"撤离点位于[{Extraction.X},{Extraction.Y}]（距你 {CellPos.Manhattan(p.Pos, Extraction)} 格）"
                : $"撤离点位于{CoarseRegion(Extraction)}（距你 {CellPos.Manhattan(p.Pos, Extraction)} 格）")
            : "撤离点位置未知";

        return new ViewOut
        {
            Seat = p.SeatIndex,
            Stage = Stage,
            Round = Round,
            Hp = p.Hp,
            Ap = p.Ap,
            YourTurn = awaiting,
            Awaiting = Await != null && Await.SeatIndex == p.SeatIndex,
            AwaitKind = awaiting ? Await!.Kind.ToString() : "",
            AwaitPrompt = awaiting ? Await!.Prompt : "",
            X = center.X,
            Y = center.Y,
            MapW = Width,
            MapH = Height,
            MyCode = p.Code,
            // 精确坐标仅在保镖可见时下发；其他角色即使撤离点已公开也只给模糊方位，避免改包作弊
            ExtractionX = extractionVisible && p.Role == RoleId.Bodyguard ? Extraction.X : -1,
            ExtractionY = extractionVisible && p.Role == RoleId.Bodyguard ? Extraction.Y : -1,
            ExtractionVisible = extractionVisible,
            ExtractionExact = extractionVisible && p.Role == RoleId.Bodyguard,
            ExtractionHint = extractionHint,
            Role = RoleDisplay(p),
            RoleKey = p.Role.ToString().ToLowerInvariant(),
            RoleColor = p.CaseId >= 0 ? PlanColor(p.CaseId) : "",
            Objective = ObjectiveText(p, p.CaseId >= 0 ? PlanColor(p.CaseId) : ""),
            Hand = p.Hand.Select(h =>
            {
                var cd = Cfg.Card(h.DefId);
                return new HandCardOut
                {
                    Id = h.Id,
                    Name = cd.Name,
                    DefId = h.DefId,
                    Temp = h.Temp,
                    Usable = IsCardUsable(p, cd, opponent),
                    Cat = cd.Cat.ToString(),
                };
            }).ToList(),
            Effects = p.Effects.Select(e => EffectLabel(e.Effect)).Where(s => s != null).Cast<string>().ToList(),
            MedkitHints = medkitHints,
            Markers = VisibleMarkers(p),
            CrownCodes = ProtectedNpcs.Where(n => n.Exposed).Select(n => n.Code).ToList(),
            InBattle = inBattle,
            BattlePrompt = battlePrompt,
            BattleOpponentCode = opponent?.Code ?? "",
            BattleOpponentHp = opponent?.Hp ?? 0,
            BattleOpponentMaxHp = opponent != null ? oppMax : 0,
            BattleDistance = Battle?.Distance ?? 0,
            Cells = cells,
            ProtectedCountdown = extractionVisible
                ? ProtectedNpcs.Where(n => !n.Dead && (p.CaseId < 0 || n.CaseId == p.CaseId))
                    .Select(n => CellPos.Manhattan(n.Pos, Extraction)).DefaultIfEmpty(0).Min()
                : -1,
        };
    }

    // 木箱标签按玩家掌握信息动态给出：默认可交互物品不显示是否为空
    private string ItemLabelFor(PlayerActor p, Item it)
    {
        if (it.Kind == ItemKind.Chest)
        {
            if (p.OpenedItems.Contains(it.Id))
                return "木箱（空）";
            if (p.ReconMaybes.Contains(it.Id) && (it.CardDefId.Length > 0 || it.TrappedGlue))
                return "木箱（可能有东西）";
            return "木箱";
        }
        return it.Label;
    }

    private string CoarseRegion(CellPos c)
    {
        int cx = Width / 2, cy = Height / 2;
        string xr = c.X < cx ? "西" : c.X > cx ? "东" : "";
        string yr = c.Y < cy ? "北" : c.Y > cy ? "南" : "";
        if (xr.Length == 0 && yr.Length == 0) return "地图中部";
        if (xr.Length == 0) return yr + "部";
        if (yr.Length == 0) return xr + "部";
        return xr + yr + "部";
    }

    private static string? EffectLabel(CardEffect e) => e switch
    {
        CardEffect.Stealth => "隐匿中",
        CardEffect.Aim => "瞄准中",
        CardEffect.RapidFire => "连射待发",
        CardEffect.Stim => "兴奋剂",
        CardEffect.Shield => "防暴盾护体",
        CardEffect.Disguise => "伪装中",
        CardEffect.Mimic => "行动模仿中",
        CardEffect.Dye => "被染色标记",
        _ => null,
    };

    private bool IsCardUsable(PlayerActor p, CardDef cd, ActorNode? opponent)
    {
        if (Stage == Stage.Battle && opponent != null)
        {
            if ((cd.PhaseFlags & CardPhase.Battle) == 0) return false;
            if (cd.Fx is CardEffect.Gun && Battle!.Distance <= 2) return true;
            if (cd.Fx is CardEffect.Knife && Battle!.Distance <= 1) return true;
            if (cd.Fx is CardEffect.Gun or CardEffect.Knife) return false;
            return true;
        }
        if (Stage == Stage.Free) return (cd.PhaseFlags & CardPhase.Free) != 0;
        if (Stage == Stage.Check) return (cd.PhaseFlags & CardPhase.Check) != 0;
        return false;
    }
}
