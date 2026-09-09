namespace SomeGame.Core;

public readonly record struct CellPos(int X, int Y)
{
    public static int Manhattan(CellPos a, CellPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    public static IEnumerable<CellPos> OrthoAround(CellPos c) =>
        new[]
        {
            new CellPos(c.X + 1, c.Y), new CellPos(c.X - 1, c.Y),
            new CellPos(c.X, c.Y + 1), new CellPos(c.X, c.Y - 1),
        };
    public override string ToString() => $"[{X},{Y}]";
}

public enum RoleId
{
    Bodyguard,
    Killer,
    Thief,
    Madman,
}

public enum ActorKind
{
    Player,
    ProtectedNpc,
    DecoyNpc,
}

public enum Stage
{
    Idle,
    Free,
    Check,
    Battle,
    Terminal,
}

public enum ItemKind
{
    None,
    Chest,
    Medkit,
    Cover,
}

public enum CardEffect
{
    None,
    Gun,
    Knife,
    Dodge,
    Shield,
    Stealth,
    Heal,
    Aim,
    RapidFire,
    Smoke,
    Energy,
    Stim,
    AmmoPack,
    BladePack,
    Bug,
    Drone,
    Disguise,
    Mimic,
    Track,
    Dye,
    Molotov,
    Glue,
}

[Flags]
public enum CardPhase
{
    None = 0,
    Free = 1,
    Check = 2,
    Battle = 4,
    Any = Free | Check | Battle,
}

public enum CardCategory
{
    Attack,
    Defense,
    Utility,
    Identity,
}

public enum TargetKind
{
    None,
    Self,
    Actor,          // any role in same cell (player or npc)
    PlayerOrNpc,    // actor optionally across range
    Cell,
    Item,
    SelfCellItem,
}

public static class Defs
{
    public static int Manhattan(int ax, int ay, int bx, int by) => Math.Abs(ax - bx) + Math.Abs(ay - by);
}
