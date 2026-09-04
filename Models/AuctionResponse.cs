using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MabinogiCraftOptimizer.Models;

/// <summary>
/// NEXON Open API 경매장 응답 루트 DTO
/// </summary>
public class AuctionResponse
{
    [JsonPropertyName("auction_item")]
    public List<AuctionItem> AuctionItems { get; set; } = new();

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}
