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
            var c = RandFreeCellNoItem();
            _items.TryAdd(c, new List<Item>());
            _items[c].Add(new Item { Id = ++_itemSeq, Kind = ItemKind.Chest, Pos = c, CardDefId = id });
        }
        _deck.Clear();
    }

    private void PlaceMedkitsAndCovers()
    {
        for (int i = 0; i < Cfg.Map.MedkitItemCount; i++)
        {
            var c = RandFreeCellNoItem();
            _items.TryAdd(c, new List<Item>());
            _items[c].Add(new Item { Id = ++_itemSeq, Kind = ItemKind.Medkit, Pos = c });
        }
        for (int i = 0; i < Cfg.Map.CoverItemCount; i++)
        {
            var c = RandFreeCellNoItem();
            _items.TryAdd(c, new List<Item>());
            _items[c].Add(new Item { Id = ++_itemSeq, Kind = ItemKind.Cover, Pos = c });
        }
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

    private CellPos PlaceAtDistance(int d)
    {
        for (int t = 0; t < 300; t++)
        {
            var c = new CellPos(Rng.Next(0, Width), Rng.Next(0, Height));
            if (CellPos.Manhattan(c, Extraction) == d && !_items.ContainsKey(c)) return c;
        }
        return RandFreeCellNoItem();
    }

    private NpcActor SpawnProtectedNpc(int caseIdx, string color)
    {
        var pos = PlaceAtDistance(Rng.Next(4, 8));
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
            LastOpText = "在赶路",
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
