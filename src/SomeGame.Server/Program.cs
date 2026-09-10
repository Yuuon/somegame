using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SomeGame.Core;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("SOMEGAME_PORT") ?? "5123";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var jsonOpt = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

var hub = new Hub(app, jsonOpt);
app.UseWebSockets();
app.Map("/ws", hub.HandleAsync);

app.MapGet("/api/health", () => Results.Ok(new { ok = true, rooms = hub.RoomCount }));
app.Run();

internal sealed class Hub
{
    private readonly WebApplication _app;
    private readonly JsonSerializerOptions _opt;
    private readonly Dictionary<string, Room> _rooms = new();
    private readonly List<Client> _clients = new();
    private readonly object _lock = new();
    private long _roomSeq;

    public int RoomCount { get { lock (_lock) return _rooms.Count; } }

    public Hub(WebApplication app, JsonSerializerOptions opt)
    {
        _app = app;
        _opt = opt;
        _ = Task.Run(Ticker);
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var client = new Client(ws);
        lock (_lock) _clients.Add(client);
        var buf = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(buf, CancellationToken.None);
                    ms.Write(buf, 0, res.Count);
                } while (!res.EndOfMessage);

                if (res.MessageType == WebSocketMessageType.Close) break;
                var text = Encoding.UTF8.GetString(ms.ToArray());
                HandleMessage(client, text);
            }
        }
        catch (WebSocketException) { }
        finally
        {
            lock (_lock)
            {
                _clients.Remove(client);
                if (client.RoomId != null && _rooms.TryGetValue(client.RoomId, out var room))
                    OnDisconnect(room, client);
            }
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }

    private void HandleMessage(Client client, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var type = root.TryGetProperty("t", out var t) ? t.GetString() : "";

            lock (_lock)
            {
                switch (type)
                {
                    case "create":
                        CreateRoom(client, root);
                        break;
                    case "join":
                        JoinRoom(client, root);
                        break;
                    case "start":
                        StartRoom(client);
                        break;
                    case "cmd":
                        HandleCmd(client, root);
                        break;
                }
            }
        }
        catch (Exception)
        {
            // 解析/处理异常不中断连接，忽略坏消息
        }
    }

    private void CreateRoom(Client client, JsonElement root)
    {
        var name = Str(root, "playerName") ?? "房主";
        var players = Int(root, "players") ?? 4;
        if (players is not (4 or 8)) players = 4;
        var roomId = "R" + (_roomSeq++);
        var room = new Room(roomId, players, name);
        _rooms[roomId] = room;
        client.Name = name;
        client.RoomId = roomId;
        client.Seat = 0;
        room.HostConn = client;
        room.Slots[0] = new Slot(name, client, false);
        _ = Send(client, new LobbyOut
        {
            RoomId = roomId,
            Players = players,
            Seats = room.Slots.Select((s, i) => new SeatOut(i, s?.Name ?? "", s?.Bot == true)).ToList(),
            IsHost = true,
        });
    }

    private void JoinRoom(Client client, JsonElement root)
    {
        var roomId = Str(root, "roomId");
        if (roomId == null || !_rooms.TryGetValue(roomId, out var room)) { _ = Send(client, new InfoOut("房间不存在")); return; }
        if (room.Game != null) { _ = Send(client, new InfoOut("对局已开始")); return; }
        var name = Str(root, "playerName") ?? "玩家";
        var idx = Array.FindIndex(room.Slots, s => s == null || (s.Bot && s.Conn == null));
        if (idx < 0) { _ = Send(client, new InfoOut("房间已满")); return; }
        client.Name = name;
        client.RoomId = roomId;
        client.Seat = idx;
        room.Slots[idx] = new Slot(name, client, false);
        BroadcastLobby(room);
    }

    private void StartRoom(Client client)
    {
        if (client.RoomId == null || !_rooms.TryGetValue(client.RoomId, out var room)) return;
        if (room.Game != null) return;
        if (room.HostConn != null && room.HostConn != client) return; // 房主在线时仅房主可开
        if (room.Slots.Count(s => s is { Conn: not null }) == 0) return;

        var seats = new SeatIn[room.Players];
        for (int i = 0; i < room.Players; i++)
        {
            var s = room.Slots[i];
            if (s != null && s.Conn != null) seats[i] = new SeatIn(s.Name, false);
            else
            {
                var name = s?.Name ?? $"机器人{i + 1}";
                room.Slots[i] = new Slot(name, null, true);
                seats[i] = new SeatIn(name, true);
            }
        }

        var cfg = GameConfig.Default();
        var env = Environment.GetEnvironmentVariable("SOMEGAME_DECISION_MS");
        if (int.TryParse(env, out var dm) && dm >= 0) cfg.TimersMs.DecisionMs = dm;
        var game = new Game(cfg, DateTime.UtcNow.Ticks, seats);
        room.Game = game;
        room.DecisionMs = cfg.TimersMs.DecisionMs;
        game.Continue();
        room.ResetDeadline();
        FlushRoom(room);
    }

    private void HandleCmd(Client client, JsonElement root)
    {
        if (client.RoomId == null || !_rooms.TryGetValue(client.RoomId, out var room)) return;
        var game = room.Game;
        if (game == null) return;
        if (client.Seat == null) return;
        var seat = client.Seat.Value;
        var await = game.Await;
        if (await == null || await.SeatIndex != seat) return;

        switch (await.Kind)
        {
            case AwaitKind.FreeAction:
                game.SubmitFree(seat, BuildFreeCmd(game, root));
                break;
            case AwaitKind.CheckAction:
                game.SubmitCheck(seat, BuildCheckCmd(game, root));
                break;
            case AwaitKind.CheckConfirm:
                game.SubmitCheck(seat, new CheckCmd(Skip: false, StartBattle: Bool(root, "startBattle")));
                break;
            case AwaitKind.BattleAction:
                game.SubmitBattleAction(seat, new BattleCmd(Str(root, "action") == "play" ? "play" : "pass", Long(root, "cardId")));
                break;
            case AwaitKind.BattleDefense:
                game.SubmitDefense(seat, new DefenseCmd(Bool(root, "dodge"), Long(root, "cardId")));
                break;
        }
        room.ResetDeadline();
        FlushRoom(room);
    }

    private FreeCmd BuildFreeCmd(Game g, JsonElement root)
    {
        var op = Str(root, "op") ?? "";
        var cardId = Long(root, "cardId");
        var itemId = Long(root, "itemId");
        int? targetActor = null;
        var targetCode = Str(root, "target");
        if (targetCode != null)
        {
            var actor = g.Actors.FirstOrDefault(x => !x.Dead && x.Code == targetCode);
            if (actor != null) targetActor = actor.Id;
        }
        return new FreeCmd(op, cardId, targetActor, itemId, Int(root, "x") ?? 0, Int(root, "y") ?? 0);
    }

    private CheckCmd BuildCheckCmd(Game g, JsonElement root)
    {
        var op = Str(root, "op") ?? "";
        if (op == "skip") return new CheckCmd(Skip: true);
        if (op == "card") return new CheckCmd(Skip: false, CardId: Long(root, "cardId"), X: Int(root, "x") ?? 0, Y: Int(root, "y") ?? 0);
        var targetCode = Str(root, "target");
        if (targetCode != null)
        {
            var actor = g.Actors.FirstOrDefault(x => !x.Dead && x.Code == targetCode);
            if (actor != null) return new CheckCmd(Skip: false, TargetActorId: actor.Id);
        }
        return new CheckCmd(Skip: true);
    }

    private async Task Ticker()
    {
        while (true)
        {
            await Task.Delay(150);
            Room[] rooms;
            lock (_lock) rooms = _rooms.Values.ToArray();
            foreach (var room in rooms)
            {
                // 与客户端命令、断线处理共用同一把锁，避免对同一对局并发写入
                lock (_lock)
                {
                    var g = room.Game;
                    if (g == null || g.Terminal) continue;
                    if (g.Await == null)
                    {
                        g.Continue();
                        room.ResetDeadline();
                        FlushRoom(room);
                        continue;
                    }
                    var seat = g.Await.SeatIndex;
                    var slot = room.Slots[seat];
                    if (slot is { Conn: null })
                    {
                        AutoAct(room, g);
                        FlushRoom(room);
                        continue;
                    }
                    if (DateTime.UtcNow > room.Deadline)
                    {
                        AutoAct(room, g);
                        FlushRoom(room);
                    }
                }
            }
        }
    }

    private void AutoAct(Room room, Game g)
    {
        var seat = g.Await!.SeatIndex;
        switch (g.Await.Kind)
        {
            case AwaitKind.FreeAction:
                g.SubmitFree(seat, new FreeCmd("finish"));
                break;
            case AwaitKind.CheckAction:
                g.SubmitCheck(seat, new CheckCmd(Skip: true));
                break;
            case AwaitKind.CheckConfirm:
                g.SubmitCheck(seat, new CheckCmd(Skip: false, StartBattle: false));
                break;
            case AwaitKind.BattleAction:
                g.SubmitBattleAction(seat, new BattleCmd("pass"));
                break;
            case AwaitKind.BattleDefense:
                g.SubmitDefense(seat, new DefenseCmd(Dodge: false));
                break;
        }
        room.ResetDeadline();
    }

    private void FlushRoom(Room room)
    {
        var g = room.Game;
        if (g == null) return;
        for (int i = 0; i < room.Slots.Length; i++)
        {
            var slot = room.Slots[i];
            if (slot?.Conn is not { } c) continue;
            var msgs = g.DrainOutbox(i);
            foreach (var m in msgs)
            {
                if (m is ViewOut v) room.LastView[i] = v;
                _ = Send(c, m);
            }
        }
    }

    private void BroadcastLobby(Room room)
    {
        foreach (var s in room.Slots)
        {
            if (s?.Conn is not { } c) continue;
            _ = Send(c, new LobbyOut
            {
                RoomId = room.Id,
                Players = room.Players,
                Seats = room.Slots.Select((x, i) => new SeatOut(i, x?.Name ?? "", x?.Bot == true)).ToList(),
                IsHost = c == room.HostConn,
            });
        }
    }

    private void OnDisconnect(Room room, Client client)
    {
        if (client.Seat == null) return;
        if (client == room.HostConn) room.HostConn = null; // 房主离开后其他人可接管开局
        var slot = room.Slots[client.Seat.Value];
        if (slot == null) return;
        if (room.Game != null)
        {
            slot.Conn = null;
            slot.Bot = true;
            var g = room.Game;
            if (g.Await?.SeatIndex == client.Seat.Value)
            {
                AutoAct(room, g);
                FlushRoom(room);
            }
        }
        else
        {
            room.Slots[client.Seat.Value] = null;
            BroadcastLobby(room);
        }
        client.RoomId = null;
        client.Seat = null;
    }

    private async Task Send(Client c, object msg)
    {
        try
        {
            if (c.Ws.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(msg, _opt);
            var bytes = Encoding.UTF8.GetBytes(json);
            await c.Ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (WebSocketException) { }
    }

    private static string? Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    private static long? Long(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
    private static bool Bool(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
}

internal sealed class Client
{
    public Client(WebSocket ws) => Ws = ws;
    public WebSocket Ws { get; }
    public string Name { get; set; } = "";
    public string? RoomId { get; set; }
    public int? Seat { get; set; }
}

internal sealed class Slot
{
    public Slot(string name, Client? conn, bool bot)
    {
        Name = name; Conn = conn; Bot = bot;
    }
    public string Name { get; }
    public Client? Conn { get; set; }
    public bool Bot { get; set; }
}

internal sealed class Room
{
    public Room(string id, int players, string hostName)
    {
        Id = id; Players = players; HostName = hostName;
        Slots = new Slot?[players];
        Deadline = DateTime.UtcNow;
        LastView = new ViewOut[players];
    }
    public string Id { get; }
    public int Players { get; }
    public string HostName { get; }
    public Client? HostConn { get; set; }
    public Slot?[] Slots { get; }
    public Game? Game { get; set; }
    public DateTime Deadline { get; set; }
    public int DecisionMs { get; set; } = 60000;
    public ViewOut[] LastView { get; }
    public void ResetDeadline() => Deadline = DateTime.UtcNow.AddMilliseconds(DecisionMs);
}

internal sealed class LobbyOut
{
    public string Type => "lobby";
    public string RoomId { get; init; } = "";
    public int Players { get; init; }
    public List<SeatOut> Seats { get; init; } = new();
    public bool IsHost { get; init; }
}

internal sealed class InfoOut
{
    public InfoOut(string text) => Text = text;
    public string Type => "info";
    public string Text { get; }
}

internal sealed record SeatOut(int Seat, string Name, bool Bot);