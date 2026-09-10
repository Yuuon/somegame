namespace SomeGame.Core;

public class ActorNode
{
    public int Id { get; init; }
    public ActorKind Kind { get; init; }
    public string Code { get; init; } = "";
    public CellPos Pos { get; set; }
    public int Hp { get; set; }
    public bool Dead { get; set; }
    public string LastOpText { get; set; } = "";
    public bool IsProtected => Kind == ActorKind.ProtectedNpc;
    public List<EffectState> Effects { get; } = new();
}

public sealed class PlayerActor : ActorNode
{
    public int SeatIndex { get; init; }
    public RoleId Role { get; init; }
    public int CaseId { get; init; } = -1;
    public string PlayerName { get; init; } = "";
    public bool IsBot { get; init; }
    public int Ap { get; set; }
    public bool FinishedFree { get; set; }
    public bool MovedThisRound { get; set; }
    public List<CardInstance> Hand { get; } = new();
    public int CarriedCase { get; set; } = -1; // 财宝所属案
    public int CoverTargetId { get; set; } = -1; // 保镖当前掩护的对象（每回合重置）
    public HashSet<long> OpenedItems { get; } = new();   // 本人已开箱（知其空/满）
    public HashSet<long> ReconMaybes { get; } = new();   // 探查标记"可能有东西"的物品
    public bool IsObserver => Dead;
    public bool PubliclyHostile { get; set; }
    public bool InspectedThisRound { get; set; }
}

public sealed class NpcActor : ActorNode
{
    public int CaseId { get; set; } = -1;      // ProtectedNpc: 所属案
    public string CaseColor { get; set; } = ""; // ProtectedNpc 徽记颜色（公开线索）
    public bool ItemIntact { get; set; }        // ProtectedNpc: 财宝是否仍在
    public CellPos? Extraction { get; set; }
    public bool Exposed { get; set; }           // 受保护NPC身份是否已被查验暴露
    public bool Urged { get; set; }             // 本回合被保镖催促移动
    public bool Evacuated { get; set; }         // 已从撤离点撤离
    public int EvacWaitRounds { get; set; }     // 撤离点等待回合数
    public List<string> LastActionLog { get; } = new(); // 装饰用
}

public sealed class CardInstance
{
    public long Id { get; init; }
    public string DefId { get; init; } = "";
    public bool Temp { get; init; }
}

public sealed class EffectState
{
    public CardEffect Effect { get; set; }
    public int TurnsRemaining { get; set; }
    public string Data { get; set; } = "";
    public long? SourceCardId { get; set; }
}

public sealed class Item
{
    public long Id { get; init; }
    public ItemKind Kind { get; init; }
    public CellPos Pos { get; set; }
    public long? CardContentId { get; set; }       // Chest 内含卡牌
    public string CardDefId { get; set; } = "";    // Chest 内含卡牌 def
    public bool TrappedGlue { get; set; }
    public bool Consumed { get; set; }
    public string Label => Kind switch
    {
        ItemKind.Chest => "木箱",
        ItemKind.Medkit => "医疗包",
        ItemKind.Cover => "掩体墙",
        _ => "物品",
    };
}

public sealed class BattleState
{
    public bool Active { get; set; }
    public int InitiatorActorId { get; set; }
    public int DefenderActorId { get; set; }
    public int CurrentActorId { get; set; }
    public int Distance { get; set; }
    public int ConsecutivePasses { get; set; }
    public int RoundNo { get; set; }
    public bool IsVsNpc { get; set; }
    public string? EndReason { get; set; }
}

public readonly record struct CellState(int SmokeRemaining, int BurnRemaining);
