using System.Text.Json.Serialization;

namespace MabinogiCraftOptimizer.Models;

/// <summary>
/// 단일 재료 정보 모델
/// </summary>
public class Material
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
