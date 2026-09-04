using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MabinogiCraftOptimizer.Models;

/// <summary>
/// 아이템 제작 레시피 정보를 담는 모델 클래스
/// </summary>
public class Recipe
{
    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("outputQuantity")]
    public int OutputQuantity { get; set; } = 1;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "CraftMaterial";

    [JsonPropertyName("materials")]
    public List<Material> Materials { get; set; } = new();

    [JsonIgnore]
    public string CategoryDisplay => string.Equals(Category, "Weapon", System.StringComparison.OrdinalIgnoreCase) ? "무기" : "제작재료";
}
