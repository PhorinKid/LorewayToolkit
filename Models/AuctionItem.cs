using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MabinogiCraftOptimizer.Models;

/// <summary>
/// NEXON Open API 마비노기 경매장 단일 매물 DTO
/// </summary>
public class AuctionItem
{
    [JsonPropertyName("item_name")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("auction_price_per_unit")]
    public long AuctionPricePerUnit { get; set; }
}

/// <summary>
/// 메모리 캐시에 저장되는 시세 엔트리 (최저단가 및 마지막 조회시각 포함)
/// </summary>
public class AuctionCacheEntry
{
    public string ItemName { get; set; } = string.Empty;
    public long LowestUnitPrice { get; set; }
    public int TotalItemCount { get; set; }
    public List<AuctionItem> RawItems { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

/// <summary>
/// 경매장 단일 조회 결과 정보 (캐시 여부 및 만료 여부, 튜플 Deconstruct 지원)
/// </summary>
public class AuctionFetchResult
{
    public bool Success { get; set; }
    public List<AuctionItem> Items { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public bool FromCache { get; set; }
    public bool IsExpired { get; set; }

    public void Deconstruct(out bool success, out List<AuctionItem> items, out string message, out DateTime? lastUpdated)
    {
        success = Success;
        items = Items;
        message = Message;
        lastUpdated = LastUpdated;
    }

    public void Deconstruct(out bool success, out List<AuctionItem> items, out string message, out DateTime? lastUpdated, out bool fromCache, out bool isExpired)
    {
        success = Success;
        items = Items;
        message = Message;
        lastUpdated = LastUpdated;
        fromCache = FromCache;
        isExpired = IsExpired;
    }
}
