using System.Collections.Generic;

namespace MabinogiCraftOptimizer.Models;

/// <summary>
/// 단일 아이템의 최적 획득 경로 및 하위 제작 트리 DTO
/// </summary>
public class OptimizationResult
{
    public string ItemName { get; set; } = string.Empty;
    public int RequiredQuantity { get; set; }
    public int OwnedConsumed { get; set; }
    public int MissingQuantity { get; set; }
    public string SelectedMethod { get; set; } = "None"; // Inventory, Auction, Npc, Orb, Craft, None
    public long TotalCost { get; set; }
    public long? DirectAuctionCost { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<OptimizationResult> Children { get; set; } = new();
    public bool IsFeasible { get; set; } = true;
}

/// <summary>
/// 제작 아이템 전체의 최적화 요약 결과
/// </summary>
public class RecipeOptimizationSummary
{
    public string TargetItemName { get; set; } = string.Empty;
    public long TotalOptimizedCost { get; set; }
    public long? TotalAllAuctionCost { get; set; }
    public long? TotalSavings => (TotalAllAuctionCost.HasValue && TotalAllAuctionCost.Value >= TotalOptimizedCost)
        ? TotalAllAuctionCost.Value - TotalOptimizedCost
        : null;

    public List<OptimizationResult> MaterialResults { get; set; } = new();
    public bool IsAllFeasible { get; set; } = true;
}
