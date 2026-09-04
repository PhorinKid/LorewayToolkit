using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MabinogiCraftOptimizer.Models;

/// <summary>
/// 단일 획득 방법 상세 정보 (Auction, Npc, Orb, Ducat, Craft)
/// </summary>
public class AcquisitionMethod
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "Auction", "Npc", "Orb", "Ducat", "Craft"

    [JsonPropertyName("npcName")]
    public string? NpcName { get; set; } // 예: "레넨 상점", "스튜어트 비밀상점"

    [JsonPropertyName("unitGoldCost")]
    public long? UnitGoldCost { get; set; } // NPC 판매 단가

    [JsonPropertyName("requiredCurrency")]
    public int? RequiredCurrency { get; set; } // 1묶음 교환 시 필요한 구슬 수 또는 두카트

    [JsonPropertyName("receivedQuantity")]
    public int? ReceivedQuantity { get; set; } // 1묶음 교환 시 획득하는 아이템 수 (기본 1)
}

/// <summary>
/// 아이템별 지원 획득 방법 목록
/// </summary>
public class ItemAcquisitionInfo
{
    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("methods")]
    public List<AcquisitionMethod> Methods { get; set; } = new();
}
