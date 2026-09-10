namespace SomeGame.Core;

public partial class Game
{
    private void BuildDeck()
    {
        _deck.Clear();
        foreach (var q in Cfg.Deck)
            for (int i = 0; i < q.Count; i++)
                _deck.Add(q.CardId);
        ShuffleList(_deck);
    }

    private void DealHands()
    {
        foreach (var p in Players)
            for (int i = 0; i < Cfg.HandSize; i++)
            {
                if (_deck.Count == 0) break;
                var id = _deck[0];
                _deck.RemoveAt(0);
                p.Hand.Add(new CardInstance { Id = ++_cardSeq, DefId = id });
            }
    }

    private void PlaceDeckRemainderToChests()
    {
        foreach (var id in _deck)
        {
            var c = PickItemCell();
            AddItem(new Item { Id = ++_itemSeq, Kind = ItemKind.Chest, Pos = c, CardDefId = id });
        }
        _deck.Clear();
    }

    private void PlaceMedkitsAndCovers()
    {
        for (int i = 0; i < Cfg.Map.MedkitItemCount; i++)
            AddItem(new Item { Id = ++_itemSeq, Kind = ItemKind.Medkit, Pos = PickItemCell() });
        for (int i = 0; i < Cfg.Map.CoverItemCount; i++)
            AddItem(new Item { Id = ++_itemSeq, Kind = ItemKind.Cover, Pos = PickItemCell() });
    }

    private void PlaceEmptyChests()
    {
        for (int i = 0; i < Cfg.Map.EmptyChestCount; i++)
            AddItem(new Item { Id = ++_itemSeq, Kind = ItemKind.Chest, Pos = PickItemCell() });
    }

    private void AddItem(Item it)
    {
        _items.TryAdd(it.Pos, new List<Item>());
        _items[it.Pos].Add(it);
    }

    // 允许一格多物：部分物品复用已有物品格
    private CellPos PickItemCell()
    {
        if (_items.Count > 0 && Rng.Chance(0.4))
        {
            var cell = _items.Keys.ElementAt(Rng.Next(0, _items.Count));
            if (cell != Extraction) return cell;
        }
        return RandFreeCellNoItem();
    }

    private CellPos RandFreeCellNoItem()
    {
        for (int t = 0; t < 400; t++)
        {
            var c = new CellPos(Rng.Next(0, Width), Rng.Next(0, Height));
            if (c == Extraction) continue;
            if (!_items.ContainsKey(c)) return c;
        }
        return new CellPos(Rng.Next(0, Width), Rng.Next(0, Height));
    }

    private CellPos PlaceProtectedAway()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var c = new CellPos(Rng.Next(0, Width), Rng.Next(0, Height));
            if (CellPos.Manhattan(c, Extraction) >= 6 && !_items.ContainsKey(c)) return c;
        }
        for (int d = 6; d <= Width + Height; d++)
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                var c = new CellPos(x, y);
                if (CellPos.Manhattan(c, Extraction) == d && !_items.ContainsKey(c)) return c;
            }
        return new CellPos(0, 0) == Extraction ? new CellPos(1, 0) : new CellPos(0, 0);
    }

    private NpcActor SpawnProtectedNpc(int caseIdx, string color)
    {
        // 距撤离点必须 >5（第6格及以上），保证撤离时间窗充足
        var pos = PlaceProtectedAway();
        return new NpcActor
        {
            Id = _nextActorId++,
            Kind = ActorKind.ProtectedNpc,
            Code = color + "案贵宾",
            Pos = pos,
            Hp = Cfg.ProtectedNpcHp,
            CaseId = caseIdx,
            CaseColor = color,
            ItemIntact = true,
            Extraction = Extraction,
            LastOpText = "在徘徊",
        };
    }

    private NpcActor SpawnDecoy(string code)
    {
        return new NpcActor
        {
            Id = _nextActorId++,
            Kind = ActorKind.DecoyNpc,
            Code = code,
            Pos = RandFreeCellNoItem(),
            Hp = 1,
            LastOpText = "在闲逛",
        };
    }

    public string ObjectiveText(PlayerActor p, string color)
    {
        return p.Role switch
        {
            RoleId.Bodyguard => $"守护 {color}案贵宾 与其财物：贵宾存活抵达撤离点、且财物未被窃取即胜利。",
            RoleId.Killer => $"击杀 {color}案贵宾，然后本人存活抵达撤离点核验即胜利。",
            RoleId.Thief => $"窃取 {color}案财物，然后本人携物存活抵达撤离点核验即胜利。",
            RoleId.Madman => "清除本局所有保镖、杀手、小偷（全部死亡）即胜利。",
            _ => "",
        };
    }

    public string RoleDisplay(PlayerActor p)
        => Cfg.Role(p.Role.ToString().ToLowerInvariant()).DisplayName;
}
