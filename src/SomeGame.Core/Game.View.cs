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
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var c = new CellPos(x, y);
            var d = CellPos.Manhattan(center, c);
            if (!p.IsObserver && d > 2) continue;
            int tier = p.IsObserver ? 0 : d;
            var occ = Occupants(c);
            var occList = new List<string>();
            if (tier <= 1 || p.IsObserver)
                occList.AddRange(occ.Select(a => a.Code));
            else if (occ.Count > 0)
                occList.Add($"{occ.Count}人");

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
                            Label = i.Label,
                            Kind = i.Kind.ToString(),
                            HasCard = i.CardDefId.Length > 0,
                        });
                }
            }
            else if (tier == 1 && ItemsAt(c).Count > 0)
                itemList.Add("有物品");

            cells.Add(new CellOut
            {
                X = x, Y = y, Tier = tier,
                Occupants = occList,
                Items = itemList,
                Interactables = interactables,
                Burning = _burn.TryGetValue(c, out var bb) && bb > 0,
                Smoky = _smoke.TryGetValue(c, out var ss) && ss > 0,
            });
        }

        var opponent = inBattle
            ? ActorById(p.Id == Battle!.InitiatorActorId ? Battle.DefenderActorId : Battle.InitiatorActorId)
            : null;
        var battlePrompt = opponent != null
            ? $"战斗 vs「{opponent.Code}」 距离{Battle!.Distance} 对方HP {opponent.Hp}" +
              (p.Id == Battle.CurrentActorId ? " · 轮到你" : " · 等待对手")
            : "";

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
            ExtractionX = Extraction.X,
            ExtractionY = Extraction.Y,
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
                };
            }).ToList(),
            Effects = p.Effects.Select(e => EffectLabel(e.Effect)).Where(s => s != null).Cast<string>().ToList(),
            InBattle = inBattle,
            BattlePrompt = battlePrompt,
            Cells = cells,
            ProtectedCountdown = ProtectedNpcs.Where(n => !n.Dead).Select(n => CellPos.Manhattan(n.Pos, Extraction)).DefaultIfEmpty(0).Min(),
        };
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
