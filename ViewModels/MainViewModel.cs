using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using MabinogiCraftOptimizer.Models;
using MabinogiCraftOptimizer.Services;

namespace MabinogiCraftOptimizer.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly RecipeService _recipeService;
    private readonly InventoryService _inventoryService;
    private readonly NexonApiService _nexonApiService;
    private readonly OptimizationService _optimizationService;

    // 캐싱된 경매장 시세 맵 [아이템명: 개당단가]
    private readonly Dictionary<string, long> _cachedAuctionPrices = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<Recipe> RecipeList { get; } = new();
    public ICollectionView RecipeView { get; }

    private Recipe? _selectedRecipe;
    public Recipe? SelectedRecipe
    {
        get => _selectedRecipe;
        set
        {
            if (SetField(ref _selectedRecipe, value))
            {
                UpdateMaterials();
            }
        }
    }

    public ObservableCollection<MaterialItemViewModel> RequiredMaterials { get; } = new();
    public ObservableCollection<MaterialItemViewModel> LeafMaterials { get; } = new();
    public ObservableCollection<CraftTreeNodeViewModel> CraftTreeNodes { get; } = new();

    private MaterialItemViewModel? _selectedMaterial;
    public MaterialItemViewModel? SelectedMaterial
    {
        get => _selectedMaterial;
        set => SetField(ref _selectedMaterial, value);
    }

    private int _selectedTabIndex = 0;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    // 완제품 경매장 시세
    private string _finishedItemPriceText = "경매장 가격 : 미갱신";
    public string FinishedItemPriceText
    {
        get => _finishedItemPriceText;
        set => SetField(ref _finishedItemPriceText, value);
    }

    private long? _finishedItemAuctionPrice;
    public long? FinishedItemAuctionPrice
    {
        get => _finishedItemAuctionPrice;
        set
        {
            if (SetField(ref _finishedItemAuctionPrice, value))
            {
                OnPropertyChanged(nameof(FinishedItemAuctionPriceInputText));
            }
        }
    }

    public string FinishedItemAuctionPriceInputText
    {
        get => FinishedItemAuctionPrice.HasValue ? FinishedItemAuctionPrice.Value.ToString() : "";
        set
        {
            string clean = value?.Replace(",", "").Trim() ?? "";
            if (long.TryParse(clean, out long p) && p >= 0)
            {
                FinishedItemAuctionPrice = p;
                FinishedItemPriceText = $"{p:N0} G";
                if (SelectedRecipe != null)
                {
                    _cachedAuctionPrices[SelectedRecipe.ItemName] = p;
                    _nexonApiService.SaveManualPrice(SelectedRecipe.ItemName, p);
                }
            }
            else
            {
                FinishedItemAuctionPrice = null;
                FinishedItemPriceText = "경매장 가격 : 미갱신";
                if (SelectedRecipe != null)
                {
                    _cachedAuctionPrices.Remove(SelectedRecipe.ItemName);
                    _nexonApiService.RemoveManualPrice(SelectedRecipe.ItemName);
                }
            }
            OnPropertyChanged(nameof(FinishedItemAuctionPrice));
            OnPropertyChanged(nameof(FinishedItemAuctionPriceInputText));
            UpdateBuyVsCraftDecision();
        }
    }

    // 2. 최적화 제작 최소 비용
    private string _optimizedCostText = "-";
    public string OptimizedCostText
    {
        get => _optimizedCostText;
        set => SetField(ref _optimizedCostText, value);
    }

    private long? _totalOptimizedCostValue;
    public long? TotalOptimizedCostValue
    {
        get => _totalOptimizedCostValue;
        set => SetField(ref _totalOptimizedCostValue, value);
    }

    // 구매 vs 제작 판정
    private string _buyVsCraftDecisionText = "-";
    public string BuyVsCraftDecisionText
    {
        get => _buyVsCraftDecisionText;
        set => SetField(ref _buyVsCraftDecisionText, value);
    }

    // 구슬 1개당 Gold 가치
    public long OrbPrice
    {
        get => _optimizationService.OrbUnitGoldValue;
        set
        {
            if (_optimizationService.OrbUnitGoldValue != value)
            {
                _optimizationService.SetOrbUnitGoldValue(value);
                OnPropertyChanged(nameof(OrbPrice));

                foreach (var mat in RequiredMaterials) mat.RecalculateAllCosts();
                foreach (var leaf in LeafMaterials) leaf.RecalculateAllCosts();
                RunOptimization();
            }
        }
    }

    // 총 필요 구슬 수
    private long _totalRequiredOrbs;
    public long TotalRequiredOrbs
    {
        get => _totalRequiredOrbs;
        set
        {
            if (SetField(ref _totalRequiredOrbs, value))
            {
                OnPropertyChanged(nameof(TotalRequiredOrbsText));
            }
        }
    }

    public string TotalRequiredOrbsText => TotalRequiredOrbs > 0 ? $"{TotalRequiredOrbs:N0}개" : "0개 (구슬 교환 불필요)";

    private string _apiStatusMessage = "NEXON API 연결 준비 완료";
    public string ApiStatusMessage
    {
        get => _apiStatusMessage;
        set => SetField(ref _apiStatusMessage, value);
    }

    private string _lastRefreshedTimeText = "시세 상태: 미갱신";
    public string LastRefreshedTimeText
    {
        get => _lastRefreshedTimeText;
        set => SetField(ref _lastRefreshedTimeText, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsNotLoading));
            }
        }
    }

    public bool IsNotLoading => !IsLoading;

    public RecipeService RecipeService => _recipeService;
    public OptimizationService OptimizationService => _optimizationService;

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetField(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(EditButtonText));
                OnPropertyChanged(nameof(OrbEditButtonBackground));
                OnPropertyChanged(nameof(EditViewVisibility));
                OnPropertyChanged(nameof(EditInputVisibility));

                foreach (var mat in RequiredMaterials) mat.IsEditMode = value;
                foreach (var leaf in LeafMaterials) leaf.IsEditMode = value;
                foreach (var node in CraftTreeNodes) node.IsEditMode = value;
            }
        }
    }

    public string EditButtonText => IsEditMode ? "저장" : "수정";
    public string OrbEditButtonBackground => IsEditMode ? "#239A72" : "#D9B63C";

    public Visibility EditViewVisibility => IsEditMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditInputVisibility => IsEditMode ? Visibility.Visible : Visibility.Collapsed;

    public void ToggleEditMode()
    {
        if (IsEditMode)
        {
            IsEditMode = false;
            _optimizationService.SaveAcquisitionMethods();
            _nexonApiService.SaveCacheToFile();
            RunOptimization();
        }
        else
        {
            IsEditMode = true;
        }
    }

    public void ToggleOrbEditMode() => ToggleEditMode();

    public MainViewModel()
    {
        _recipeService = new RecipeService();
        _optimizationService = new OptimizationService();
        _nexonApiService = new NexonApiService();
        _inventoryService = new InventoryService();

        RecipeView = CollectionViewSource.GetDefaultView(RecipeList);
        RecipeView.GroupDescriptions.Add(new PropertyGroupDescription("CategoryDisplay"));
        RecipeView.SortDescriptions.Add(new SortDescription("Category", ListSortDirection.Descending));

        CheckApiKeyStatus();
        ReloadAllRecipes();
    }

    private void CheckApiKeyStatus()
    {
        string? apiKey = _nexonApiService.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ApiStatusMessage = "⚠️ .env 파일 또는 NEXON_API_KEY 환경변수가 설정되지 않았습니다.";
        }
        else
        {
            ApiStatusMessage = "✅ NEXON API 연결 준비 완료";
        }
    }

    public void ReloadAllRecipes(string? selectItemName = null)
    {
        string? currentSelectedName = selectItemName ?? SelectedRecipe?.ItemName;
        var recipes = _recipeService.LoadAllRecipes();
        RecipeList.Clear();
        foreach (var recipe in recipes)
        {
            // Material은 제작 대상 ComboBox에 미표시 (Weapon, CraftMaterial만 표시)
            if (!string.Equals(recipe.Category, RecipeService.CategoryMaterial, StringComparison.OrdinalIgnoreCase))
            {
                RecipeList.Add(recipe);
            }
        }

        RecipeView?.Refresh();

        if (!string.IsNullOrEmpty(currentSelectedName))
        {
            var found = RecipeList.FirstOrDefault(r => string.Equals(r.ItemName, currentSelectedName, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                SelectedRecipe = found;
            }
            else if (RecipeList.Count > 0)
            {
                SelectedRecipe = RecipeList[0];
            }
        }
        else if (RecipeList.Count > 0)
        {
            SelectedRecipe = RecipeList[0];
        }

        RunOptimization();
    }

    /// <summary>
    /// 레시피 선택 변경 시 재료 행 구성 및 캐시 시세 복원
    /// </summary>
    private void UpdateMaterials()
    {
        RequiredMaterials.Clear();

        var persistentPrices = _nexonApiService.GetAllCachedPrices();
        foreach (var kvp in persistentPrices)
        {
            _cachedAuctionPrices[kvp.Key] = kvp.Value;
        }

        if (SelectedRecipe?.Materials != null)
        {
            var allRecipes = _recipeService.LoadAllRecipes();

            foreach (var mat in SelectedRecipe.Materials)
            {
                int ownedQty = _inventoryService.GetQuantity(mat.Name);
                bool hasSubRecipe = allRecipes.Any(r => string.Equals(r.ItemName, mat.Name, StringComparison.OrdinalIgnoreCase));

                var itemVm = new MaterialItemViewModel(
                    mat.Name,
                    mat.Quantity,
                    ownedQty,
                    _optimizationService,
                    hasSubRecipe,
                    OnMaterialOwnedQuantityChanged,
                    RunOptimization,
                    OnMaterialAuctionPriceChanged
                );

                var cached = _nexonApiService.GetCachedEntry(mat.Name);
                if (cached != null)
                {
                    _cachedAuctionPrices[mat.Name] = cached.LowestUnitPrice;
                    itemVm.SetAuctionPrice(cached.LowestUnitPrice, cached.LastUpdated);
                }

                RequiredMaterials.Add(itemVm);
                itemVm.IsEditMode = IsEditMode;
            }
        }

        SelectedMaterial = RequiredMaterials.FirstOrDefault();

        // 완제품 시세 복원
        string targetItem = GetSearchableItemName(SelectedRecipe?.ItemName ?? string.Empty);
        var finCached = _nexonApiService.GetCachedEntry(targetItem);
        if (finCached != null)
        {
            FinishedItemAuctionPrice = finCached.LowestUnitPrice;
            FinishedItemPriceText = $"{finCached.LowestUnitPrice:N0} G";
            _cachedAuctionPrices[targetItem] = finCached.LowestUnitPrice;
        }
        else
        {
            FinishedItemAuctionPrice = null;
            FinishedItemPriceText = "경매장 가격 : 미갱신";
        }

        RunOptimization();
    }

    private void OnMaterialAuctionPriceChanged(string itemName, long? newPrice)
    {
        if (newPrice.HasValue)
        {
            _cachedAuctionPrices[itemName] = newPrice.Value;
            _nexonApiService.SaveManualPrice(itemName, newPrice.Value);
        }
        else
        {
            _cachedAuctionPrices.Remove(itemName);
            _nexonApiService.RemoveManualPrice(itemName);
        }

        // RequiredMaterials와 LeafMaterials 양방향 동기화
        var reqItem = RequiredMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (reqItem != null && reqItem.AuctionUnitPrice != newPrice)
        {
            if (newPrice.HasValue) reqItem.SetAuctionPrice(newPrice.Value);
            else reqItem.SetAuctionError("가격 정보 없음");
        }

        var leafItem = LeafMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (leafItem != null && leafItem.AuctionUnitPrice != newPrice)
        {
            if (newPrice.HasValue) leafItem.SetAuctionPrice(newPrice.Value);
            else leafItem.SetAuctionError("가격 정보 없음");
        }
    }

    private void OnMaterialOwnedQuantityChanged(string itemName, int newQuantity)
    {
        _inventoryService.SetQuantity(itemName, newQuantity);

        // RequiredMaterials에 존재하는 항목이면 수량 동기화
        var reqItem = RequiredMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (reqItem != null && reqItem.OwnedQuantity != newQuantity)
        {
            reqItem.OwnedQuantity = newQuantity;
        }

        // LeafMaterials에 존재하는 항목이면 수량 동기화
        var leafItem = LeafMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (leafItem != null && leafItem.OwnedQuantity != newQuantity)
        {
            leafItem.OwnedQuantity = newQuantity;
        }
    }

    public static string GetSearchableItemName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;
        var clean = Regex.Replace(rawName, @"\[.*?\]", "").Trim();
        int parenIdx = clean.IndexOf('(');
        if (parenIdx > 0) clean = clean.Substring(0, parenIdx).Trim();
        return string.IsNullOrWhiteSpace(clean) ? rawName : clean;
    }

    /// <summary>
    /// 다단계 제작 최소 비용을 계산하고, [필요 재료] 탭과 [제작 트리] 탭의 뷰 모델을 갱신합니다.
    /// </summary>
    public void RunOptimization()
    {
        if (SelectedRecipe == null || RequiredMaterials.Count == 0)
        {
            OptimizedCostText = "-";
            BuyVsCraftDecisionText = "-";
            TotalOptimizedCostValue = null;
            LeafMaterials.Clear();
            CraftTreeNodes.Clear();
            return;
        }

        var allRecipes = _recipeService.LoadAllRecipes();

        // 전역 인벤토리 복사본을 가져와 재귀 최적화 실행 (트리/리프에서 변경된 값 100% 반영)
        var workingInv = _inventoryService.GetAllQuantities();

        long totalOptimized = 0;
        bool allFeasible = true;
        var directResults = new List<OptimizationResult>();

        foreach (var itemVm in RequiredMaterials)
        {
            var (optResult, updatedInv) = _optimizationService.OptimizeItemCost(
                itemVm.Name,
                itemVm.RequiredQuantity,
                workingInv,
                _cachedAuctionPrices,
                allRecipes,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            );

            workingInv = updatedInv;
            itemVm.OptimizationResult = optResult;
            directResults.Add(optResult);

            if (optResult.IsFeasible)
            {
                totalOptimized += optResult.TotalCost;
            }
            else
            {
                allFeasible = false;
            }
        }

        if (allFeasible)
        {
            TotalOptimizedCostValue = totalOptimized;
            OptimizedCostText = $"{totalOptimized:N0} G";
        }
        else
        {
            TotalOptimizedCostValue = null;
            OptimizedCostText = "경매장 가격 : 미갱신";
        }

        // 완제품 경매가 vs 직접 제작 비용 비교
        UpdateBuyVsCraftDecision();

        // 1. [제작 트리] 탭 갱신
        UpdateCraftTree(directResults, totalOptimized, allFeasible);

        // 2. [필요 재료] 탭 (Leaf 재료 체크리스트) 갱신
        UpdateLeafMaterials(directResults);

        // 3. 최적 경로 기준 총 필요 구슬 갯수 집계
        TotalRequiredOrbs = CalculateTotalOrbs(directResults);
    }

    private long CalculateTotalOrbs(List<OptimizationResult> results)
    {
        long sum = 0;
        void Traverse(OptimizationResult res)
        {
            if (res.MissingQuantity > 0 && string.Equals(res.SelectedMethod, "Orb", StringComparison.OrdinalIgnoreCase))
            {
                var methods = _optimizationService.GetMethodsForItem(res.ItemName);
                var orbMethod = methods.Find(m => string.Equals(m.Type, "Orb", StringComparison.OrdinalIgnoreCase));
                if (orbMethod?.RequiredCurrency.HasValue == true)
                {
                    sum += (long)orbMethod.RequiredCurrency.Value * res.MissingQuantity;
                }
            }
            if (res.Children != null)
            {
                foreach (var c in res.Children)
                {
                    Traverse(c);
                }
            }
        }
        foreach (var r in results)
        {
            Traverse(r);
        }
        return sum;
    }

    /// <summary>
    /// [제작 트리] 탭의 계층 노드 컬렉션 인플레이스 갱신 (트리 노드별 보유량 입력 및 포커스 유지)
    /// </summary>
    private void UpdateCraftTree(List<OptimizationResult> directResults, long totalCost, bool isFeasible)
    {
        if (SelectedRecipe == null) return;

        var rootOpt = new OptimizationResult
        {
            ItemName = SelectedRecipe.ItemName,
            RequiredQuantity = 1,
            OwnedConsumed = 0,
            MissingQuantity = 1,
            SelectedMethod = "Craft",
            TotalCost = totalCost,
            IsFeasible = isFeasible,
            Children = directResults
        };

        if (CraftTreeNodes.Count > 0 && string.Equals(CraftTreeNodes[0].ItemName, SelectedRecipe.ItemName, StringComparison.OrdinalIgnoreCase))
        {
            CraftTreeNodes[0].IsEditMode = IsEditMode;
            CraftTreeNodes[0].UpdateInPlace(
                rootOpt,
                name => _inventoryService.GetQuantity(name),
                OnMaterialOwnedQuantityChanged,
                RunOptimization
            );
        }
        else
        {
            CraftTreeNodes.Clear();
            int topOwned = _inventoryService.GetQuantity(SelectedRecipe.ItemName);
            var rootNode = new CraftTreeNodeViewModel(
                rootOpt, 
                topOwned, 
                isRoot: true, // 최상위 완제품 노드 지정
                OnMaterialOwnedQuantityChanged, 
                RunOptimization
            )
            {
                IsEditMode = IsEditMode
            };
            rootNode.UpdateInPlace(
                rootOpt,
                name => _inventoryService.GetQuantity(name),
                OnMaterialOwnedQuantityChanged,
                RunOptimization
            );
            CraftTreeNodes.Add(rootNode);
        }
    }

    /// <summary>
    /// [필요 재료] 탭의 최종 Leaf 목록 갱신 (이미 확보된 재료도 포함)
    /// </summary>
    private void UpdateLeafMaterials(List<OptimizationResult> directResults)
    {
        var rawLeaves = new List<OptimizationResult>();
        foreach (var res in directResults)
        {
            CollectLeafResults(res, rawLeaves);
        }

        // Leaf 재료들을 아이템명 기준으로 그룹화하여 합산
        var groupedLeaves = rawLeaves
            .GroupBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingMap = LeafMaterials.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var newNames = new HashSet<string>(groupedLeaves.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

        // 1. 더 이상 Leaf에 속하지 않는 항목 제거
        for (int i = LeafMaterials.Count - 1; i >= 0; i--)
        {
            if (!newNames.Contains(LeafMaterials[i].Name))
            {
                LeafMaterials.RemoveAt(i);
            }
        }

        // 2. 항목별 인플레이스(in-place) 갱신 또는 신규 추가
        foreach (var group in groupedLeaves)
        {
            string itemName = group.Key;
            int totalReq = group.Sum(x => x.RequiredQuantity);
            int totalOwnedConsumed = group.Sum(x => x.OwnedConsumed);
            int totalMissing = group.Sum(x => x.MissingQuantity);
            long totalCost = group.Sum(x => x.TotalCost);
            bool isFeasible = group.All(x => x.IsFeasible);

            // 주요 선택된 획득 방법 결정
            string selectedMethod = totalMissing == 0 
                ? "Inventory" 
                : (group.FirstOrDefault(x => x.MissingQuantity > 0 && x.SelectedMethod != "Inventory")?.SelectedMethod ?? "Inventory");

            var opt = new OptimizationResult
            {
                ItemName = itemName,
                RequiredQuantity = totalReq,
                OwnedConsumed = totalOwnedConsumed,
                MissingQuantity = totalMissing,
                SelectedMethod = selectedMethod,
                TotalCost = totalCost,
                IsFeasible = isFeasible
            };

            if (existingMap.TryGetValue(itemName, out var existingVm))
            {
                // 기존 ViewModel 재사용 -> UI TextBox 포커스 및 커서 위치 유지!
                existingVm.RequiredQuantity = totalReq;
                existingVm.OptimizationResult = opt;
                existingVm.NotifyOrbChanges();
                existingVm.IsEditMode = IsEditMode;
            }
            else
            {
                // 신규 재료 항목 추가
                int userOwned = _inventoryService.GetQuantity(itemName);
                bool hasSub = _optimizationService.HasCraftMethod(itemName);

                var leafVm = new MaterialItemViewModel(
                    itemName,
                    totalReq,
                    userOwned,
                    _optimizationService,
                    hasSub,
                    OnMaterialOwnedQuantityChanged,
                    RunOptimization,
                    OnMaterialAuctionPriceChanged
                );

                var cached = _nexonApiService.GetCachedEntry(itemName);
                if (cached != null)
                {
                    leafVm.SetAuctionPrice(cached.LowestUnitPrice, cached.LastUpdated);
                }
                else if (_cachedAuctionPrices.TryGetValue(itemName, out long p))
                {
                    leafVm.SetAuctionPrice(p);
                }

                leafVm.OptimizationResult = opt;
                leafVm.IsEditMode = IsEditMode;
                LeafMaterials.Add(leafVm);
            }
        }
    }

    /// <summary>
    /// OptimizationResult 트리에서 Craft 노드를 건너뛰고 Leaf 노드들을 재귀 수집
    /// </summary>
    private void CollectLeafResults(OptimizationResult node, List<OptimizationResult> accumulator)
    {
        if (string.Equals(node.SelectedMethod, "Craft", StringComparison.OrdinalIgnoreCase) && 
            node.Children != null && 
            node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                CollectLeafResults(child, accumulator);
            }
        }
        else
        {
            accumulator.Add(node);
        }
    }

    private void UpdateBuyVsCraftDecision()
    {
        if (TotalOptimizedCostValue.HasValue && FinishedItemAuctionPrice.HasValue)
        {
            long craftCost = TotalOptimizedCostValue.Value;
            long buyPrice = FinishedItemAuctionPrice.Value;

            if (craftCost < buyPrice)
            {
                BuyVsCraftDecisionText = "직접 제작";
            }
            else if (craftCost > buyPrice)
            {
                BuyVsCraftDecisionText = "경매장 구매";
            }
            else
            {
                BuyVsCraftDecisionText = "구매/제작 비용 동일";
            }
        }
        else if (FinishedItemAuctionPrice.HasValue)
        {
            BuyVsCraftDecisionText = "재료 시세 미확인";
        }
        else if (TotalOptimizedCostValue.HasValue)
        {
            BuyVsCraftDecisionText = "경매장 가격 : 미갱신";
        }
        else
        {
            BuyVsCraftDecisionText = "-";
        }
    }

    /// <summary>
    /// [시세 일괄 갱신]:
    /// - forceRefresh=false (일반 갱신): 10분 TTL 미만 유효 캐시는 API를 호출하지 않고 재사용
    /// - forceRefresh=true (강제 갱신): 10분 TTL을 무시하고 전체 최신 시세 재조회
    /// 완제품 및 제작 트리의 모든 재료를 단일 Unique Items 목록으로 수집하여 중복 API 요청을 완전 제거합니다.
    /// </summary>
    public async Task RefreshAllPricesAsync(bool forceRefresh = false)
    {
        if (SelectedRecipe == null) return;

        IsLoading = true;
        ApiStatusMessage = forceRefresh ? "경매장 실시간 시세 강제 갱신 중..." : "경매장 실시간 시세 갱신 중 (10분 캐시 활용)...";

        var allRecipes = _recipeService.LoadAllRecipes();

        // 1. 완제품 및 하위 제작 재료 수집 (전체 요청 수 vs Unique 중복 제거 수 카운트)
        var allRequestedItems = new List<string>();
        var uniqueItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string finishedItemName = GetSearchableItemName(SelectedRecipe.ItemName);
        if (_optimizationService.HasAuctionMethod(finishedItemName))
        {
            allRequestedItems.Add(finishedItemName);
            uniqueItems.Add(finishedItemName);
        }

        void CollectItem(string rawName)
        {
            string name = GetSearchableItemName(rawName);
            if (!_optimizationService.HasAuctionMethod(name)) return;

            allRequestedItems.Add(name);
            if (uniqueItems.Add(name))
            {
                var rcp = allRecipes.FirstOrDefault(r => string.Equals(r.ItemName, name, StringComparison.OrdinalIgnoreCase));
                if (rcp != null)
                {
                    foreach (var m in rcp.Materials)
                    {
                        CollectItem(m.Name);
                    }
                }
            }
        }

        foreach (var mat in SelectedRecipe.Materials)
        {
            CollectItem(mat.Name);
        }

        // 2. 이번 갱신 세션의 HTTP 요청 카운터 초기화
        NexonApiService.ResetHttpRequestCounter();

        int cacheHits = 0;
        int expiredCount = 0;
        int notCachedCount = 0;
        int apiLookups = 0;

        foreach (var itemName in uniqueItems)
        {
            var cached = _nexonApiService.GetCachedEntry(itemName);
            if (cached == null)
            {
                notCachedCount++;
            }
            else if (DateTime.Now - cached.LastUpdated >= NexonApiService.AuctionCacheDuration)
            {
                expiredCount++;
            }
            else
            {
                cacheHits++;
            }

            int beforeCount = NexonApiService.HttpRequestCounter;
            var result = await _nexonApiService.GetAuctionDataAsync(itemName, forceRefresh: forceRefresh);
            int afterCount = NexonApiService.HttpRequestCounter;
            bool madeHttp = afterCount > beforeCount;
            if (madeHttp)
            {
                apiLookups++;
            }

            // 완제품인 경우 상단 완제품 시세 갱신
            if (string.Equals(itemName, finishedItemName, StringComparison.OrdinalIgnoreCase))
            {
                if (result.Success && result.Items.Count > 0)
                {
                    long minPrice = result.Items.Min(x => x.AuctionPricePerUnit);
                    FinishedItemAuctionPrice = minPrice;
                    _cachedAuctionPrices[finishedItemName] = minPrice;
                    FinishedItemPriceText = $"{minPrice:N0} G";
                }
                else if (cached != null)
                {
                    FinishedItemAuctionPrice = cached.LowestUnitPrice;
                    _cachedAuctionPrices[finishedItemName] = cached.LowestUnitPrice;
                    FinishedItemPriceText = $"{cached.LowestUnitPrice:N0} G";
                }
                else
                {
                    FinishedItemAuctionPrice = null;
                    FinishedItemPriceText = "경매장 가격 : 미갱신";
                }
            }

            // 재료 행 (RequiredMaterials, LeafMaterials) 시세 갱신
            if (result.Success && result.Items.Count > 0)
            {
                long minPrice = result.Items.Min(x => x.AuctionPricePerUnit);
                _cachedAuctionPrices[itemName] = minPrice;
                UpdateRowsWithPrice(itemName, minPrice, result.LastUpdated);
            }
            else if (cached != null)
            {
                _cachedAuctionPrices[itemName] = cached.LowestUnitPrice;
                UpdateRowsWithPrice(itemName, cached.LowestUnitPrice, cached.LastUpdated);
            }
            else
            {
                UpdateRowsWithError(itemName, result.Success ? "매물 없음" : "가격 정보 없음");
            }

            // UI 실시간 최적화 반영
            RunOptimization();

            // 실제 HTTP 요청이 발생했을 때만 API Rate Limit 준수를 위해 지연
            if (madeHttp)
            {
                await Task.Delay(220);
            }
        }

        RunOptimization();

        // 3. 요구된 [PRICE REFRESH] 형식의 상세 통계 Debug 로그 출력
        string refreshLog = $@"
[PRICE REFRESH]
Requested Items: {allRequestedItems.Count}
Unique Items: {uniqueItems.Count}
Cache Hits: {cacheHits}
Expired: {expiredCount}
Not Cached: {notCachedCount}
API Price Lookups: {apiLookups}
Actual HTTP Requests: {NexonApiService.HttpRequestCounter}
Refresh Mode: {(forceRefresh ? "Force" : "Normal")}
";
        System.Diagnostics.Debug.WriteLine(refreshLog);
        Console.WriteLine(refreshLog);

        LastRefreshedTimeText = $"시세 상태: {DateTime.Now:HH:mm:ss}";
        IsLoading = false;
        ApiStatusMessage = $"✅ 시세 갱신 완료 (대상: {uniqueItems.Count}종, HTTP: {NexonApiService.HttpRequestCounter}회, 모드: {(forceRefresh ? "강제" : "일반")})";
    }

    private void UpdateRowsWithPrice(string itemName, long minPrice, DateTime? lastUpdated)
    {
        var matRow = RequiredMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        matRow?.SetAuctionPrice(minPrice, lastUpdated);

        var leafRow = LeafMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        leafRow?.SetAuctionPrice(minPrice, lastUpdated);
    }

    private void UpdateRowsWithError(string itemName, string errorMessage)
    {
        var matRow = RequiredMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        matRow?.SetAuctionError(errorMessage);

        var leafRow = LeafMaterials.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        leafRow?.SetAuctionError(errorMessage);
    }

}
