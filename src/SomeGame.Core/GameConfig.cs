using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SomeGame.Core;

public sealed class GameConfig
{
    public int Version { get; set; } = 1;
    public MapCfg Map { get; set; } = new();
    public int ProtectedNpcHp { get; set; } = 5;
    public int HandSize { get; set; } = 5;
    public TurnCfg Turn { get; set; } = new();
    public TimerCfg TimersMs { get; set; } = new();
    public RoleDef[] Roles { get; set; } = Array.Empty<RoleDef>();
    public Dictionary<string, string[]> EnemiesByRole { get; set; } = new();
    public SeatPlanCfg[] SeatPlans { get; set; } = Array.Empty<SeatPlanCfg>();
    public CardDef[] Cards { get; set; } = Array.Empty<CardDef>();
    public CardQty[] Deck { get; set; } = Array.Empty<CardQty>();

    public RoleDef Role(string id) => Roles.First(r => r.Id == id);
    public CardDef Card(string id) => Cards.First(c => c.Id == id);
    public SeatPlanCfg PlanFor(int players) => SeatPlans.First(p => p.PlayerCount == players);
    public string[] EnemiesOf(string roleId) =>
        EnemiesByRole.TryGetValue(roleId, out var v) ? v : Array.Empty<string>();

    public static GameConfig Load(JsonDocument doc) => doc.Deserialize<GameConfig>(JsonOpt)!;

    public static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string? _defaultJson;
    public static GameConfig Default()
    {
        // 每次返回全新实例，避免调用方就地修改造成共享状态污染
        if (_defaultJson == null)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SomeGame.Core.Data.default.json")
                          ?? throw new InvalidOperationException("embedded default.json missing");
            using var r = new StreamReader(s);
            _defaultJson = r.ReadToEnd();
        }
        return Load(JsonDocument.Parse(_defaultJson));
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpt);
}

public sealed class MapCfg
{
    public int Width { get; set; } = 15;
    public int Height { get; set; } = 15;
    public int CoverItemCount { get; set; } = 8;
    public int MedkitItemCount { get; set; } = 10;
    public int EmptyChestCount { get; set; } = 10;
    public int DecoyNpcCount { get; set; } = 14;
    public int DroneRange { get; set; } = 10;
    public bool ExtractionAtCenter { get; set; } = true;
}

public sealed class TurnCfg
{
    public int ApPerRound { get; set; } = 3;
    public int MaxRounds { get; set; } = 120;
    public int RefillMapEveryNRounds { get; set; } = 3;
    public int ProtectedStepPerRound { get; set; } = 1;
}

public sealed class TimerCfg
{
    public int DecisionMs { get; set; } = 60000;
}

public sealed class RoleDef
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Hp { get; set; }
    public string? SupplyCard { get; set; }
    public RoleId RoleKind => Enum.Parse<RoleId>(Id, true);
}

public sealed class SeatPlanCfg
{
    public int PlayerCount { get; set; }
    public CasePlanCfg[] Cases { get; set; } = Array.Empty<CasePlanCfg>();
    public string[] Madmen { get; set; } = Array.Empty<string>();
    public int TotalSeats => Cases.Length * 3 + Madmen.Length;
}

public sealed class CasePlanCfg
{
    public int CaseIndex { get; set; }
    public string Color { get; set; } = "";
    public string[] Roles { get; set; } = Array.Empty<string>();
}

public sealed class CardDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Effect { get; set; } = "none";
    public string Category { get; set; } = "utility";
    public string[] Phases { get; set; } = Array.Empty<string>();
    public string Target { get; set; } = "self";
    public int Range { get; set; }
    public int Value { get; set; }
    public int Count { get; set; }
    public int Duration { get; set; }
    public string? GenCard { get; set; }
    public bool Temp { get; set; }
    public bool Hideable { get; set; }

    public CardEffect Fx => Enum.TryParse<CardEffect>(Effect, true, out var e) ? e : CardEffect.None;
    public CardCategory Cat => Enum.TryParse<CardCategory>(Category, true, out var c) ? c : CardCategory.Utility;
    public CardPhase PhaseFlags
    {
        get
        {
            CardPhase f = CardPhase.None;
            foreach (var p in Phases)
                if (Enum.TryParse<CardPhase>(p, true, out var v)) f |= v;
            return f == CardPhase.None ? CardPhase.Any : f;
        }
    }
    public TargetKind Tgt => Enum.TryParse<TargetKind>(Target, true, out var t) ? t : TargetKind.Self;
}

public sealed class CardQty
{
    public string CardId { get; set; } = "";
    public int Count { get; set; }
}
