using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using MabinogiCraftOptimizer.Models;

namespace MabinogiCraftOptimizer.Services;

/// <summary>
/// 아이템 획득 방법별 비용 계산 및 다단계 재귀 최적화 엔진 서비스
/// </summary>
public class OptimizationService
{
    private readonly string _methodsFilePath;
    private readonly string _settingsFilePath;
    private readonly RecipeService _recipeService = new();
    private Dictionary<string, ItemAcquisitionInfo> _acquisitionData = new(StringComparer.OrdinalIgnoreCase);

    // 구슬 1개의 기준 Gold 가치 (기본값: 350,000 G, settings.json에 자동 저장/로드)
    public long OrbUnitGoldValue { get; private set; } = 350_000;

    public OptimizationService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _methodsFilePath = Path.Combine(baseDir, "Data", "acquisition-methods.json");
        _settingsFilePath = Path.Combine(baseDir, "Data", "settings.json");

        LoadSettings();
        LoadAcquisitionMethods();
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("orbUnitGoldValue", out var prop))
                {
                    long val = prop.GetInt64();
                    if (val > 0) OrbUnitGoldValue = val;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[경고] settings.json 로드 실패: {ex.Message}");
        }
    }

    public void SetOrbUnitGoldValue(long value)
    {
        long safeVal = Math.Max(value, 0);
        if (OrbUnitGoldValue == safeVal) return;

        OrbUnitGoldValue = safeVal;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settingsObj = new { orbUnitGoldValue = OrbUnitGoldValue };
            string json = JsonSerializer.Serialize(settingsObj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[오류] settings.json 저장 실패: {ex.Message}");
        }
    }

    public void LoadAcquisitionMethods()
    {
        try
        {
            if (!File.Exists(_methodsFilePath))
            {
                _acquisitionData = new Dictionary<string, ItemAcquisitionInfo>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            string json = File.ReadAllText(_methodsFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = JsonSerializer.Deserialize<List<ItemAcquisitionInfo>>(json, options) ?? new List<ItemAcquisitionInfo>();

            _acquisitionData = new Dictionary<string, ItemAcquisitionInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                if (!string.IsNullOrWhiteSpace(item.ItemName))
                {
                    _acquisitionData[item.ItemName] = item;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[오류] acquisition-methods.json 로드 실패: {ex.Message}");
            _acquisitionData = new Dictionary<string, ItemAcquisitionInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public List<AcquisitionMethod> GetMethodsForItem(string itemName)
    {
        if (_acquisitionData.TryGetValue(itemName, out var info))
        {
            return info.Methods;
        }
        return new List<AcquisitionMethod>();
    }

    public long? CalculateNpcCost(string itemName, int missingQuantity)
    {
        if (missingQuantity <= 0) return 0;
        var methods = GetMethodsForItem(itemName);
        var npcMethod = methods.Find(m => string.Equals(m.Type, "Npc", StringComparison.OrdinalIgnoreCase));
        if (npcMethod?.UnitGoldCost != null && npcMethod.UnitGoldCost.Value > 0)
        {
            return (long)missingQuantity * npcMethod.UnitGoldCost.Value;
        }
        return null;
    }

    public (long? TotalCost, long? RequiredOrbs) CalculateOrbCost(string itemName, int missingQuantity)
    {
        if (missingQuantity <= 0) return (0, 0);
        var methods = GetMethodsForItem(itemName);
        var orbMethod = methods.Find(m => string.Equals(m.Type, "Orb", StringComparison.OrdinalIgnoreCase));

        if (orbMethod != null && 
            orbMethod.RequiredCurrency.HasValue && orbMethod.RequiredCurrency.Value > 0 &&
            orbMethod.ReceivedQuantity.HasValue && orbMethod.ReceivedQuantity.Value > 0)
        {
            int bundleCount = (int)Math.Ceiling((double)missingQuantity / orbMethod.ReceivedQuantity.Value);
            long requiredOrbs = (long)bundleCount * orbMethod.RequiredCurrency.Value;
            long totalCost = requiredOrbs * OrbUnitGoldValue;
            return (totalCost, requiredOrbs);
        }
        return (null, null);
    }

    /// <summary>
    /// 사용자가 입력한 구슬 교환 갯수를 acquisitionData에 갱신하고 영구 저장합니다.
    /// </summary>
    public void SetOrbCost(string itemName, int? requiredOrbs, int receivedQuantity = 1)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

        if (!_acquisitionData.TryGetValue(itemName, out var info))
        {
            info = new ItemAcquisitionInfo
            {
                ItemName = itemName,
                Methods = new List<AcquisitionMethod>()
            };
            _acquisitionData[itemName] = info;
        }

        var orbMethod = info.Methods.Find(m => string.Equals(m.Type, "Orb", StringComparison.OrdinalIgnoreCase));
        if (requiredOrbs.HasValue && requiredOrbs.Value > 0)
        {
            if (orbMethod == null)
            {
                orbMethod = new AcquisitionMethod
                {
                    Type = "Orb",
                    RequiredCurrency = requiredOrbs.Value,
                    ReceivedQuantity = receivedQuantity
                };
                info.Methods.Add(orbMethod);
            }
            else
            {
                orbMethod.RequiredCurrency = requiredOrbs.Value;
                orbMethod.ReceivedQuantity = receivedQuantity;
            }
        }
        else
        {
            if (orbMethod != null)
            {
                info.Methods.Remove(orbMethod);
            }
        }

        SaveAcquisitionMethods();
    }

    /// <summary>
    /// 현재 acquisitionData를 acquisition-methods.json에 영구 저장합니다.
    /// </summary>
    public void SaveAcquisitionMethods()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_methodsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var list = _acquisitionData.Values.ToList();
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(list, options);
            File.WriteAllText(_methodsFilePath, json);

            // 프로젝트 루트 Data 폴더에도 동기화 (빌드 시 덮어쓰기 방지)
            string projPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data", "acquisition-methods.json"));
            if (File.Exists(projPath) || Directory.Exists(Path.GetDirectoryName(projPath)))
            {
                try { File.WriteAllText(projPath, json); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[오류] acquisition-methods.json 저장 실패: {ex.Message}");
        }
    }

    public bool HasCraftMethod(string itemName)
    {
        var methods = GetMethodsForItem(itemName);
        if (methods.Exists(m => string.Equals(m.Type, "Craft", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // user-recipes.json 또는 default-recipes.json에 등록된 레시피가 있으면 제작 가능으로 자동 인식
        return _recipeService.IsRecipeExists(itemName);
    }

    public bool HasAuctionMethod(string itemName)
    {
        var methods = GetMethodsForItem(itemName);
        if (methods.Count == 0) return true;
        return !methods.Exists(m => string.Equals(m.Type, "NoAuction", StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================================
    // 다단계 재귀 최적화 핵심 엔진 (Recursive Optimization Engine)
    // =========================================================================

    /// <summary>
    /// 특정 아이템을 requiredQuantity만큼 확보할 때 가장 저렴한 최적 획득 경로를 재귀 계산합니다.
    /// </summary>
    public (OptimizationResult Result, Dictionary<string, int> RemainingInventory) OptimizeItemCost(
        string itemName,
        int requiredQuantity,
        Dictionary<string, int> currentInventory,
        Dictionary<string, long> auctionPriceMap,
        List<Recipe> allRecipes,
        HashSet<string> visitedPath)
    {
        var result = new OptimizationResult
        {
            ItemName = itemName,
            RequiredQuantity = requiredQuantity
        };

        // 1. 보유 재료 차감
        int owned = 0;
        if (currentInventory.TryGetValue(itemName, out int invQty) && invQty > 0)
        {
            owned = Math.Min(invQty, requiredQuantity);
        }

        int missing = Math.Max(requiredQuantity - owned, 0);
        result.OwnedConsumed = owned;
        result.MissingQuantity = missing;

        long? unitAuctionPrice = auctionPriceMap.TryGetValue(itemName, out long p) ? p : null;
        if (unitAuctionPrice.HasValue)
        {
            result.DirectAuctionCost = (long)missing * unitAuctionPrice.Value;
        }

        // 보유량만으로 충족된 경우
        if (missing == 0)
        {
            result.SelectedMethod = "Inventory";
            result.TotalCost = 0;
            result.Description = $"보유 수량으로 전량 충당 ({owned}개 사용)";
            result.IsFeasible = true;

            var remainingInv = new Dictionary<string, int>(currentInventory, StringComparer.OrdinalIgnoreCase);
            remainingInv[itemName] = invQty - owned;

            var refRecipe = allRecipes.FirstOrDefault(rcp => string.Equals(rcp.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
            if (refRecipe != null && refRecipe.Materials.Count > 0 && !visitedPath.Contains(itemName))
            {
                var newVisited = new HashSet<string>(visitedPath, StringComparer.OrdinalIgnoreCase) { itemName };
                foreach (var subMat in refRecipe.Materials)
                {
                    var (subResult, _) = OptimizeItemCost(
                        subMat.Name,
                        0,
                        remainingInv,
                        auctionPriceMap,
                        allRecipes,
                        newVisited
                    );
                    result.Children.Add(subResult);
                }
            }

            return (result, remainingInv);
        }

        // 2. 획득 후보군(경매장, NPC, 구슬, 제작) 탐색
        var candidates = new List<(OptimizationResult OptResult, Dictionary<string, int> PostInventory)>();

        // (A) 경매장 구매
        if (HasAuctionMethod(itemName) && unitAuctionPrice.HasValue)
        {
            long auctionCost = (long)missing * unitAuctionPrice.Value;
            var invAfter = new Dictionary<string, int>(currentInventory, StringComparer.OrdinalIgnoreCase);
            invAfter[itemName] = invQty - owned;

            var r = new OptimizationResult
            {
                ItemName = itemName,
                RequiredQuantity = requiredQuantity,
                OwnedConsumed = owned,
                MissingQuantity = missing,
                SelectedMethod = "Auction",
                TotalCost = auctionCost,
                DirectAuctionCost = auctionCost,
                Description = $"경매장 구매 ({unitAuctionPrice.Value:N0} G × {missing}개)",
                IsFeasible = true
            };
            candidates.Add((r, invAfter));
        }

        // (B) NPC 상점 구매
        long? npcCost = CalculateNpcCost(itemName, missing);
        if (npcCost.HasValue)
        {
            var invAfter = new Dictionary<string, int>(currentInventory, StringComparer.OrdinalIgnoreCase);
            invAfter[itemName] = invQty - owned;

            var methods = GetMethodsForItem(itemName);
            var npcM = methods.Find(m => string.Equals(m.Type, "Npc", StringComparison.OrdinalIgnoreCase));

            var r = new OptimizationResult
            {
                ItemName = itemName,
                RequiredQuantity = requiredQuantity,
                OwnedConsumed = owned,
                MissingQuantity = missing,
                SelectedMethod = "Npc",
                TotalCost = npcCost.Value,
                DirectAuctionCost = result.DirectAuctionCost,
                Description = $"NPC 상점 구매 (개당 {npcM?.UnitGoldCost:N0} G × {missing}개)",
                IsFeasible = true
            };
            candidates.Add((r, invAfter));
        }

        // (C) 구슬 교환
        var (orbCost, reqOrbs) = CalculateOrbCost(itemName, missing);
        if (orbCost.HasValue && reqOrbs.HasValue)
        {
            var invAfter = new Dictionary<string, int>(currentInventory, StringComparer.OrdinalIgnoreCase);
            invAfter[itemName] = invQty - owned;

            var r = new OptimizationResult
            {
                ItemName = itemName,
                RequiredQuantity = requiredQuantity,
                OwnedConsumed = owned,
                MissingQuantity = missing,
                SelectedMethod = "Orb",
                TotalCost = orbCost.Value,
                DirectAuctionCost = result.DirectAuctionCost,
                Description = $"구슬 교환 ({reqOrbs.Value}개 소모, 약 {orbCost.Value:N0} G)",
                IsFeasible = true
            };
            candidates.Add((r, invAfter));
        }

        // (D) 하위 재료 제작(Craft)
        var subRecipe = allRecipes.FirstOrDefault(rcp => string.Equals(rcp.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
        bool canCraft = (subRecipe != null && subRecipe.Materials.Count > 0) || HasCraftMethod(itemName);

        if (canCraft && subRecipe != null && subRecipe.Materials.Count > 0 && !visitedPath.Contains(itemName))
        {
            var newVisited = new HashSet<string>(visitedPath, StringComparer.OrdinalIgnoreCase) { itemName };
            var craftWorkingInv = new Dictionary<string, int>(currentInventory, StringComparer.OrdinalIgnoreCase);
            craftWorkingInv[itemName] = invQty - owned;

            long craftTotalCost = 0;
            bool craftFeasible = true;
            var childResults = new List<OptimizationResult>();

            int outputQty = subRecipe.OutputQuantity > 0 ? subRecipe.OutputQuantity : 1;
            int craftTimes = (int)Math.Ceiling((double)missing / outputQty);

            foreach (var subMat in subRecipe.Materials)
            {
                int subReqTotal = subMat.Quantity * craftTimes;

                var (subResult, updatedInv) = OptimizeItemCost(
                    subMat.Name,
                    subReqTotal,
                    craftWorkingInv,
                    auctionPriceMap,
                    allRecipes,
                    newVisited
                );

                craftWorkingInv = updatedInv;
                childResults.Add(subResult);

                if (!subResult.IsFeasible)
                {
                    craftFeasible = false;
                }
                else
                {
                    craftTotalCost += subResult.TotalCost;
                }
            }

            var r = new OptimizationResult
            {
                ItemName = itemName,
                RequiredQuantity = requiredQuantity,
                OwnedConsumed = owned,
                MissingQuantity = missing,
                SelectedMethod = "Craft",
                TotalCost = craftTotalCost,
                DirectAuctionCost = result.DirectAuctionCost,
                Description = $"하위 재료 {childResults.Count}종 조합 제작",
                Children = childResults,
                IsFeasible = craftFeasible
            };

            candidates.Add((r, craftWorkingInv));
        }

        // 3. 최저 비용 후보 선정 (동일 비용 시 우선순위: 경매장 > NPC > 두카트 > 구슬 > 제작)
        if (candidates.Count > 0)
        {
            var feasibleCandidates = candidates.Where(c => c.OptResult.IsFeasible).ToList();
            if (feasibleCandidates.Count > 0)
            {
                return feasibleCandidates
                    .OrderBy(c => c.OptResult.TotalCost)
                    .ThenBy(c => GetMethodPriority(c.OptResult.SelectedMethod))
                    .First();
            }

            // 유효 가격이 없으면 구조가 있는 Craft 후보 또는 첫 번째 후보 반환
            var craftCandidate = candidates.FirstOrDefault(c => c.OptResult.SelectedMethod == "Craft");
            if (craftCandidate.OptResult != null)
            {
                return craftCandidate;
            }

            return candidates.First();
        }

        // 4. 어떤 방법으로도 획득 불가능한 경우 (가격 데이터 부재 등)
        result.SelectedMethod = "None";
        result.TotalCost = 0;
        result.Description = "획득 비용 계산 불가 (가격/시세 정보 없음)";
        result.IsFeasible = false;

        var defaultInv = new Dictionary<string, int>(currentInventory, StringComparer.OrdinalIgnoreCase);
        defaultInv[itemName] = invQty - owned;
        return (result, defaultInv);
    }

    private static int GetMethodPriority(string method)
    {
        return method switch
        {
            "Inventory" => 0,
            "Auction" => 1,  // 경매장 가격 = 구슬 가격일 때 경매장 우선!
            "Npc" => 2,
            "Ducat" => 3,
            "Orb" => 4,
            "Craft" => 5,
            _ => 99
        };
    }
}
