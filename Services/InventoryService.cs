using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MabinogiCraftOptimizer.Services;

/// <summary>
/// 재료 보유 현황(inventory.json) 저장 및 로드 서비스
/// </summary>
public class InventoryService
{
    private readonly string _inventoryFilePath;
    private Dictionary<string, int> _inventoryData = new(StringComparer.OrdinalIgnoreCase);

    public InventoryService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _inventoryFilePath = Path.Combine(baseDir, "Data", "inventory.json");
        LoadInventory();
    }

    /// <summary>
    /// inventory.json 로드
    /// </summary>
    public void LoadInventory()
    {
        try
        {
            if (!File.Exists(_inventoryFilePath))
            {
                _inventoryData = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            string json = File.ReadAllText(_inventoryFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json, options);
            _inventoryData = data != null 
                ? new Dictionary<string, int>(data, StringComparer.OrdinalIgnoreCase) 
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[경고] inventory.json 로드 실패: {ex.Message}");
            _inventoryData = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 특정 아이템 보유 수량 조회 (기본값: 0)
    /// </summary>
    public int GetQuantity(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return 0;
        return _inventoryData.TryGetValue(itemName, out int qty) ? Math.Max(qty, 0) : 0;
    }

    public Dictionary<string, int> GetAllQuantities() => new(_inventoryData, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 아이템 보유 수량 갱신 및 파일 저장
    /// </summary>
    public void SetQuantity(string itemName, int quantity)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;
        _inventoryData[itemName] = Math.Max(quantity, 0);
        SaveInventory();
    }

    /// <summary>
    /// 인벤토리 데이터 파일 저장
    /// </summary>
    private void SaveInventory()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_inventoryFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string json = JsonSerializer.Serialize(_inventoryData, options);
            File.WriteAllText(_inventoryFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[오류] inventory.json 저장 실패: {ex.Message}");
        }
    }
}
