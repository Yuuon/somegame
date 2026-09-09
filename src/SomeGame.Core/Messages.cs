namespace SomeGame.Core;

public enum AwaitKind
{
    None,
    FreeAction,
    CheckAction,
    CheckConfirm,
    BattleAction,
    BattleDefense,
}

public sealed class AwaitingInfo
{
    public AwaitKind Kind { get; set; }
    public int SeatIndex { get; set; }
    public string Prompt { get; set; } = "";
    public long PendingCardId { get; set; }
}

public sealed class ActionEvt
{
    public long Seq { get; set; }
    public int ActorId { get; set; }
    public string Code { get; set; } = "";
    public CellPos Pos { get; set; }
    public string Op { get; set; } = "";
    public string Verb0 { get; set; } = "";
    public string? Verb1 { get; set; }
    public string? Verb2 { get; set; }
    public string? TargetCode { get; set; }
    public bool Stealth { get; set; }
    public string OpText { get; set; } = "";
    public bool FakeMimic { get; set; }
}

public sealed class ViewMessage
{
    private readonly ActionEvt _e;
    public ViewMessage(ActionEvt e) => _e = e;

    public string? TextFor(int tier, bool viewerDead = false)
    {
        if (tier <= 0)
        {
            var who = _e.FakeMimic ? _e.TargetCode ?? _e.Code : _e.Code;
            var verb = _e.FakeMimic ? _e.OpText : _e.Verb0;
            if (_e.Stealth && !viewerDead && _e.ActorId != -2) return $"有人在{_e.Pos}做了什么";
            return $"{who}{verb}";
        }
        if (tier == 1)
        {
            var who = _e.FakeMimic ? _e.TargetCode ?? _e.Code : _e.Code;
            var verb = _e.FakeMimic ? _e.OpText : _e.Verb1;
            if (verb == null) return null;
            if (_e.Stealth) return null;
            return $"{who}：{verb}";
        }
        if (_e.Stealth) return null;
        return _e.Verb2;
    }
}

// ---------- 客户端输出 ----------
public sealed class LogOut
{
    public LogOut(long seq, string text, int tier, int? x, int? y, string kind, bool stealth)
    {
        Seq = seq; Text = text; Tier = tier; X = x; Y = y; Kind = kind; Stealth = stealth;
    }
    public string Type => "log";
    public long Seq { get; }
    public string Text { get; }
    public int Tier { get; }
    public int? X { get; }
    public int? Y { get; }
    public string Kind { get; }
    public bool Stealth { get; }
}

public sealed class PhaseOut
{
    public string Type => "phase";
    public Stage Stage { get; init; }
    public string Text { get; init; } = "";
    public int Round { get; init; }
}

public sealed class ObjectiveOut
{
    public string Type => "objective";
    public string Role { get; init; } = "";
    public string Color { get; init; } = "";
    public string Text { get; init; } = "";
}

public sealed class GameOverOut
{
    public string Type => "gameover";
    public string Reason { get; init; } = "";
    public List<SeatResultOut> Results { get; init; } = new();
    public List<string> Reveal { get; init; } = new();
}

public sealed class SeatResultOut
{
    public int Seat { get; init; }
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public string Color { get; init; } = "";
    public bool Win { get; init; }
    public bool Bot { get; init; }
}

// ---------- 视图快照 ----------
public sealed class ViewOut
{
    public string Type => "view";
    public int Seat { get; init; }
    public Stage Stage { get; init; }
    public int Round { get; init; }
    public int Hp { get; init; }
    public int Ap { get; init; }
    public bool YourTurn { get; init; }
    public bool Awaiting { get; init; }
    public string AwaitKind { get; init; } = "";
    public string AwaitPrompt { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int ExtractionX { get; init; }
    public int ExtractionY { get; init; }
    public string Role { get; init; } = "";
    public string RoleColor { get; init; } = "";
    public string Objective { get; init; } = "";
    public List<HandCardOut> Hand { get; init; } = new();
    public List<string> Effects { get; init; } = new();
    public bool InBattle { get; init; }
    public string BattlePrompt { get; init; } = "";
    public List<CellOut> Cells { get; init; } = new();
    public int ProtectedCountdown { get; init; }
}

public sealed class HandCardOut
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string DefId { get; init; } = "";
    public bool Temp { get; init; }
    public bool Usable { get; init; }
}

public sealed class CellOut
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Tier { get; init; }
    public List<string> Occupants { get; init; } = new();
    public List<string> Items { get; init; } = new();
    public List<ItemRefOut> Interactables { get; init; } = new();
    public bool Burning { get; init; }
    public bool Smoky { get; init; }
}

public sealed class ItemRefOut
{
    public long Id { get; init; }
    public string Label { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool HasCard { get; init; }
}

public sealed class CheckResultOut
{
    public string Type => "checkresult";
    public string TargetCode { get; init; } = "";
    public string Identity { get; init; } = "";
    public bool Enemy { get; init; }
    public bool InRange => RangeOk;
    public bool RangeOk { get; init; }
    public bool CanBattle { get; init; }
    public string Note { get; init; } = "";
}
