using System.Text.Json;

namespace SomeGame.Core;

public partial class Game
{
    public GameConfig Cfg { get; }
    public long Seed { get; }
    public IRng Rng { get; }

    public int Width { get; private init; }
    public int Height { get; private init; }
    public CellPos Extraction { get; private init; }

    public int Round { get; private set; } = 1;
    public Stage Stage { get; private set; } = Stage.Idle;
    public bool Terminal { get; private set; }
    public string? TerminalReason { get; private set; }
    public List<int> Winners { get; } = new();

    public List<ActorNode> Actors { get; } = new();
    public List<PlayerActor> Players { get; } = new();
    public List<NpcActor> ProtectedNpcs { get; } = new();
    public List<NpcActor> Decoys { get; } = new();
    public List<NpcActor> AllNpcs => ProtectedNpcs.Concat(Decoys).ToList();

    public string[] RoleNamesAtStart { get; }

    private readonly Dictionary<CellPos, List<Item>> _items = new();
    private readonly Dictionary<CellPos, int> _smoke = new();
    private readonly Dictionary<CellPos, int> _burn = new();
    private readonly List<string> _deck = new();
    private readonly Queue<string> _discard = new();
    private long _cardSeq;
    private long _itemSeq;
    private int _nextActorId = 1;

    public readonly List<ActionEvt> Archive = new();
    private readonly Dictionary<int, List<object>> _outbox = new();

    public AwaitingInfo? Await { get; private set; }
    public BattleState? Battle { get; private set; }

    public Game(GameConfig cfg, long seed, SeatIn[] seats, bool shuffleRoles = true)
    {
        Cfg = cfg;
        Seed = seed;
        Rng = Rand.Create(seed);
        Width = cfg.Map.Width;
        Height = cfg.Map.Height;
        Extraction = cfg.Map.ExtractionAtCenter
            ? new CellPos(Width / 2, Height / 2)
            : new CellPos(Width - 1, Height - 1);

        var plan = cfg.PlanFor(seats.Length);
        if (plan.TotalSeats != seats.Length)
            throw new ArgumentException($"seat plan for {seats.Length} players not defined");

        var roleTokens = new List<(RoleId Role, int Case)>();
        foreach (var c in plan.Cases)
            foreach (var r in c.Roles)
                roleTokens.Add((Enum.Parse<RoleId>(r, true), c.CaseIndex));
        foreach (var m in plan.Madmen)
            roleTokens.Add((Enum.Parse<RoleId>(m, true), -1));

        var assignments = roleTokens.ToArray();
        if (shuffleRoles) Shuffle(assignments);

        RoleNamesAtStart = roleTokens.Select(t => t.Role.ToString()).ToArray();

        var decoyCount = Math.Max(cfg.Map.DecoyNpcCount, LatticeCount(Width, Height));
        var codes = BuildCodes(seats.Length + decoyCount + plan.Cases.Length);
        int codeIdx = 0;
        for (int i = 0; i < seats.Length; i++)
        {
            var (role, caseIdx) = assignments[i];
            var def = cfg.Role(role.ToString().ToLowerInvariant());
            var p = new PlayerActor
            {
                Id = _nextActorId++,
                Kind = ActorKind.Player,
                Code = codes[codeIdx++],
                Pos = new CellPos(0, 1 + i),
                Hp = def.Hp,
                SeatIndex = i,
                Role = role,
                CaseId = caseIdx,
                PlayerName = seats[i].Name,
                IsBot = seats[i].IsBot,
            };
            Players.Add(p);
            Actors.Add(p);
            _outbox[i] = new List<object>();
        }

        BuildDeck();
        PlaceMedkitsAndCovers();
        PlaceEmptyChests();

        foreach (var c in plan.Cases)
        {
            // 受保护贵宾身份对外掩盖：使用中立代号
            var npc = SpawnProtectedNpc(c.CaseIndex, c.Color, codes[codeIdx++]);
            ProtectedNpcs.Add(npc);
            Actors.Add(npc);
        }

        // 平民NPC：先按每3格网格放置以保证密度，再随机填充剩余
        int lattice = LatticeCount(Width, Height);
        int cols = (Width + 2) / 3;
        for (int d = 0; d < decoyCount; d++)
        {
            CellPos? forced = null;
            if (d < lattice)
            {
                var c = new CellPos(3 * (d % cols), 3 * (d / cols));
                if (InMap(c) && !Actors.Any(a => !a.Dead && a.Pos == c) && c != Extraction)
                    forced = c;
            }
            var npc = SpawnDecoy(codes[codeIdx++], forced);
            Decoys.Add(npc);
            Actors.Add(npc);
        }

        RepositionBodyguardsNearTarget();

        DealHands();
        PlaceDeckRemainderToChests();
        AnnounceAll($"对局开始：{seats.Length} 名玩家，第 {Round} 回合。撤离点位于 {Extraction}。");
        foreach (var p in Players)
        {
            var color = p.CaseId >= 0 ? PlanColor(p.CaseId) : "";
            PushOut(p.SeatIndex, new ObjectiveOut
            {
                Role = p.Role.ToString().ToLowerInvariant(),
                Color = color,
                Text = ObjectiveText(p, color),
            });
        }
        EnterFree();
    }

    public int SeatCount => Players.Count;

    // ---------- 公共状态访问 ----------
    public PlayerActor Player(int seat) => Players[seat];
    public ActorNode ActorById(int id) => Actors.First(a => a.Id == id);
    public NpcActor ProtectedOfCase(int caseId) => ProtectedNpcs.First(n => n.CaseId == caseId);
    public string PlanColor(int caseId) => Cfg.PlanFor(SeatCount).Cases.First(c => c.CaseIndex == caseId).Color;

    public static bool IsEnemyOf(GameConfig cfg, RoleId me, RoleId other)
        => cfg.EnemiesOf(me.ToString().ToLowerInvariant()).Contains(other.ToString().ToLowerInvariant());

    // ---------- 退出/输入通道 ----------
    public IReadOnlyList<object> DrainOutbox(int seat)
    {
        if (!_outbox.TryGetValue(seat, out var l)) return Array.Empty<object>();
        var c = l.ToList();
        l.Clear();
        return c;
    }

    public void AnnounceAll(object msg) => PushAll(msg);
    private void PushAll(object msg) { foreach (var k in _outbox.Keys) _outbox[k].Add(msg); }
    private void PushOut(int seat, object msg) { if (_outbox.TryGetValue(seat, out var l)) l.Add(msg); }
    private void PushOutOthers(int actorId, object msg)
    {
        foreach (var p in Players)
            if (p.Id != actorId && _outbox.ContainsKey(p.SeatIndex)) _outbox[p.SeatIndex].Add(msg);
    }

    // ---------- 工具 ----------
    private void Shuffle<T>(T[] a)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = Rng.Next(0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    private void ShuffleList<T>(List<T> a)
    {
        for (int i = a.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    private List<string> BuildCodes(int n)
    {
        var pool = NamePool.ToArray();
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = Rng.Next(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        var result = new List<string>(n);
        for (int i = 0; i < n; i++)
            result.Add(i < pool.Length ? pool[i] : $"行者{i + 1:D2}");
        return result;
    }

    private static int LatticeCount(int w, int h) => ((w + 2) / 3) * ((h + 2) / 3);

    private void RepositionBodyguardsNearTarget()
    {
        foreach (var b in Players.Where(p => p.Role == RoleId.Bodyguard && p.CaseId >= 0 && !p.Dead))
        {
            var npc = ProtectedOfCase(b.CaseId);
            var candidates = new List<CellPos>();
            for (int x = npc.Pos.X - 3; x <= npc.Pos.X + 3; x++)
            for (int y = npc.Pos.Y - 3; y <= npc.Pos.Y + 3; y++)
            {
                var c = new CellPos(x, y);
                if (!InMap(c) || c == Extraction) continue;
                var d = CellPos.Manhattan(npc.Pos, c);
                if (d < 1 || d > 3) continue;
                if (Actors.Any(a => !a.Dead && a.Pos == c)) continue;
                candidates.Add(c);
            }
            if (candidates.Count > 0)
                b.Pos = candidates[Rng.Next(0, candidates.Count)];
        }
    }

    private CellPos RandFreeCell()
    {
        for (int t = 0; t < 200; t++)
        {
            var c = new CellPos(Rng.Next(0, Width), Rng.Next(0, Height));
            if (c == Extraction) continue;
            if (Occupants(c).Any()) continue;
            if (!_items.ContainsKey(c)) return c;
        }
        return new CellPos(Rng.Next(0, Width), Rng.Next(0, Height));
    }

    public List<ActorNode> Occupants(CellPos c) => Actors.Where(a => !a.Dead && a.Pos == c).ToList();
    public List<Item> ItemsAt(CellPos c) => _items.TryGetValue(c, out var l)
        ? l.Where(i => !i.Consumed).ToList()
        : new List<Item>();

    public bool InMap(CellPos c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public static List<CellPos> ShortestPath(Game g, CellPos from, CellPos to)
    {
        if (from == to) return new List<CellPos>();
        var q = new Queue<CellPos>();
        var prev = new Dictionary<CellPos, CellPos>();
        q.Enqueue(from);
        prev[from] = from;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == to) break;
            foreach (var n in CellPos.OrthoAround(cur))
            {
                if (!g.InMap(n) || prev.ContainsKey(n)) continue;
                prev[n] = cur;
                q.Enqueue(n);
            }
        }
        var path = new List<CellPos>();
        var node = to;
        while (prev.TryGetValue(node, out var p) && node != from)
        {
            path.Add(node);
            node = p;
        }
        path.Reverse();
        return path;
    }

    private static readonly string[] NamePool =
    {
        "阿澈", "河洛", "青梧", "南烛", "九黎", "扶苏", "凌波", "闻人",
        "陆沉", "温言", "许游", "顾影", "江离", "云起", "苏辞", "白榆",
        "萧然", "沈砚", "楚云", "程门", "林深", "时雨", "谢桥", "沈星",
        "苏星", "洛昭", "风雅", "千帆", "归鹤", "听澜", "望舒", "栖迟",
        "未央", "惊鸿", "云岫", "月白", "鹿鸣", "星野", "山止", "川行",
        "兰舟", "青简", "松间", "竹喧", "莲舟", "雪见", "霜降", "霁月",
        "清欢", "微凉", "半盏", "长风", "寄安", "策马", "观棋", "拾穗",
        "闻笛", "泛舟", "折桂", "抱朴", "守拙", "若谷", "怀瑾", "握瑜",
        "知微", "见著", "沉璧", "渡影",
    };

    // 远处情报：对视野外的玩家广播"异常举动"的大致方向（不暴露身份与精确位置）
    public void BroadcastVague(CellPos at, string text)
    {
        var region = CoarseRegion(at);
        foreach (var q in Players)
        {
            if (q.Dead || q.Pos == at) continue;
            if (CellPos.Manhattan(q.Pos, at) <= 2) continue;
            PushOut(q.SeatIndex, new LogOut(-1, $"【远处情报】{text}（{region}附近）", 2, at.X, at.Y, "vague", false));
        }
    }

    // 平民NPC恐慌：距离2以内的平民进入恐慌并逃窜
    public void PanicFleeAround(CellPos center)
    {
        foreach (var n in Decoys.Where(d => !d.Dead).ToList())
        {
            if (CellPos.Manhattan(n.Pos, center) > 2) continue;
            n.PanicTurns = 2;
            n.PanicSource = center;
            var nb = CellPos.OrthoAround(n.Pos).Where(InMap)
                .Where(c => !Actors.Any(a => !a.Dead && a.Pos == c)).ToList();
            if (nb.Count > 0)
            {
                var old = n.Pos;
                n.Pos = nb[Rng.Next(0, nb.Count)];
                Log(ActionEvent(n.Id, "move", "惊恐逃窜", null, "有人慌乱跑过", null, false, old));
            }
        }
    }

    private string RandomTempCardDef()
    {
        var ids = Cfg.Deck.Select(d => d.CardId).ToList();
        return ids[Rng.Next(0, ids.Count)];
    }

    // ---------- 日志事件 ----------
    public void Log(ActionEvt e)
    {
        e.Seq = (long)Archive.Count + 1;
        Archive.Add(e);
        var vm = new ViewMessage(e);
        foreach (var p in Players)
        {
            if (p.IsObserver)
            {
                var txt = e.ActorId == p.Id ? SelfText(e) : vm.TextFor(0, viewerDead: true);
                if (txt != null) PushOut(p.SeatIndex, new LogOut(e.Seq, txt, 0, e.Pos.X, e.Pos.Y, e.OpText, e.Stealth));
                continue;
            }
            if (e.ActorId == p.Id)
            {
                var self = SelfText(e);
                if (self != null) PushOut(p.SeatIndex, new LogOut(e.Seq, self, 0, e.Pos.X, e.Pos.Y, e.OpText, e.Stealth));
                continue;
            }
            var tier = CellPos.Manhattan(p.Pos, e.Pos);
            if (tier > 2) continue;
            var t = vm.TextFor(tier);
            if (t == null) continue;
            PushOut(p.SeatIndex, new LogOut(e.Seq, t, tier, e.Pos.X, e.Pos.Y, e.OpText, e.Stealth));
        }

        // 窃听器 / 无人机远程监听
        foreach (var w in _bugWatches.Where(w => w.Rounds > 0 && w.Cell == e.Pos).ToList())
        {
            var owner = Players.FirstOrDefault(p => p.SeatIndex == w.Seat && !p.IsObserver);
            if (owner == null || owner.Id == e.ActorId) continue;
            var txt = w.Tier == 0 ? vm.TextFor(0) : vm.TextFor(1);
            if (txt == null) continue;
            PushOut(owner.SeatIndex, new LogOut(e.Seq, $"（监听 {w.Cell}）{txt}", w.Tier, e.Pos.X, e.Pos.Y, e.OpText, e.Stealth));
        }
    }

    private static string? SelfText(ActionEvt e)
    {
        if (e.ActorId < 0) return null;
        if (e.Verb0 == null) return null;
        return "你" + e.Verb0 + (e.Stealth ? "（未引起他人注意）" : "");
    }

    public ActionEvt ActionEvent(int actorId, string op, string verb0, string? verb1, string? verb2, string? targetCode, bool stealthHidden, CellPos at, string opText = "")
    {
        return new ActionEvt
        {
            ActorId = actorId,
            Code = ActorById(actorId).Code,
            Pos = at,
            Op = op,
            Verb0 = verb0,
            Verb1 = verb1,
            Verb2 = verb2,
            TargetCode = targetCode,
            Stealth = stealthHidden,
            OpText = opText,
        };
    }
}

public sealed record SeatIn(string Name, bool IsBot);
