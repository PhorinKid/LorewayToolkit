using System;
using System.Collections.Generic;
using System.Windows;
using MabinogiCraftOptimizer.Models;
using MabinogiCraftOptimizer.Services;

namespace MabinogiCraftOptimizer.ViewModels;

public class MaterialItemViewModel : ViewModelBase
{
    private readonly Action<string, int>? _onQuantityChanged;
    private readonly Action? _onOptimizationTriggered;
    private readonly Action<string, long?>? _onAuctionPriceChanged;
    public string Name { get; }
    private readonly OptimizationService _optimizationService;

    private int _requiredQuantity;
    public int RequiredQuantity
    {
        get => _requiredQuantity;
        set
        {
            if (SetField(ref _requiredQuantity, value))
            {
                OnPropertyChanged(nameof(RequiredQuantityDisplay));
                OnPropertyChanged(nameof(MissingQuantity));
                OnPropertyChanged(nameof(MissingQuantityDisplay));
                OnPropertyChanged(nameof(OrbCostText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(AuctionTotalCost));
                OnPropertyChanged(nameof(RecommendedMethodWithCostText));
            }
        }
    }

    public string RequiredQuantityDisplay => $"{RequiredQuantity:N0}개";

    private int _ownedQuantity;
    public int OwnedQuantity
    {
        get => _ownedQuantity;
        set
        {
            int safeValue = Math.Max(value, 0);
            _ownedQuantity = safeValue;
            OnPropertyChanged(nameof(OwnedQuantity));
            OnPropertyChanged(nameof(OwnedQuantityText));
            OnPropertyChanged(nameof(OwnedQuantityDisplay));
            OnPropertyChanged(nameof(MissingQuantity));
            OnPropertyChanged(nameof(MissingQuantityDisplay));
            OnPropertyChanged(nameof(OrbCostText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(AuctionTotalCost));
            OnPropertyChanged(nameof(FinalCostText));
            OnPropertyChanged(nameof(RecommendedMethodWithCostText));
            RecalculateAllCosts();
            _onQuantityChanged?.Invoke(Name, safeValue);
            _onOptimizationTriggered?.Invoke();
        }
    }

    public string OwnedQuantityDisplay => $"{OwnedQuantity:N0}개";

    public string OwnedQuantityText
    {
        get => _ownedQuantity.ToString();
        set
        {
            int parsed = 0;
            if (int.TryParse(value, out int v))
            {
                parsed = Math.Max(v, 0);
            }
            OwnedQuantity = parsed;
        }
    }

    public int MissingQuantity => Math.Max(RequiredQuantity - OwnedQuantity, 0);
    public string MissingQuantityDisplay => $"{MissingQuantity:N0}개";

    public string StatusText
    {
        get
        {
            if (MissingQuantity == 0) return "완료";
            if (OptimizationResult != null && !OptimizationResult.IsFeasible) return "조달 불가";
            return "필요";
        }
    }

    // 경매장 단가
    private long? _auctionUnitPrice;
    public long? AuctionUnitPrice => _auctionUnitPrice;

    private bool _isAuctionAvailable;
    public bool IsAuctionAvailable => _isAuctionAvailable;

    private bool _isLoading;
    public bool IsLoading => _isLoading;

    private string? _errorMessage;
    public string? ErrorMessage => _errorMessage;

    public bool CanEditAuctionPrice => _optimizationService.HasAuctionMethod(Name);

    public string AuctionUnitPriceInputText
    {
        get
        {
            if (!_optimizationService.HasAuctionMethod(Name)) return "-";
            if (_isLoading) return "조회 중...";
            if (_auctionUnitPrice.HasValue) return _auctionUnitPrice.Value.ToString();
            return "";
        }
        set
        {
            if (!_optimizationService.HasAuctionMethod(Name)) return;

            string clean = value?.Replace(",", "").Trim() ?? "";
            if (long.TryParse(clean, out long parsed) && parsed >= 0)
            {
                _auctionUnitPrice = parsed;
                _isAuctionAvailable = true;
                _errorMessage = null;
                _onAuctionPriceChanged?.Invoke(Name, parsed);
            }
            else
            {
                _auctionUnitPrice = null;
                _isAuctionAvailable = false;
                _errorMessage = null;
                _onAuctionPriceChanged?.Invoke(Name, null);
            }

            NotifyAuctionChanges();
            _onOptimizationTriggered?.Invoke();
        }
    }

    public string AuctionUnitPriceText
    {
        get
        {
            if (!_optimizationService.HasAuctionMethod(Name)) return "미지원";
            if (_isLoading) return "조회 중...";
            if (!string.IsNullOrEmpty(_errorMessage)) return _errorMessage;
            if (AuctionUnitPrice.HasValue) return $"{AuctionUnitPrice.Value:N0} G";
            return "시세 미확인";
        }
    }

    public long? AuctionTotalCost
    {
        get
        {
            if (MissingQuantity == 0) return 0;
            if (IsAuctionAvailable && AuctionUnitPrice.HasValue)
            {
                return (long)MissingQuantity * AuctionUnitPrice.Value;
            }
            return null;
        }
    }

    // 구슬
    public (long? TotalCost, long? RequiredOrbs) OrbInfo => _optimizationService.CalculateOrbCost(Name, MissingQuantity);

    public long? OrbTotalCost => OrbInfo.TotalCost;

    public int? UnitRequiredOrbs
    {
        get
        {
            var methods = _optimizationService.GetMethodsForItem(Name);
            var orbMethod = methods.Find(m => string.Equals(m.Type, "Orb", StringComparison.OrdinalIgnoreCase));
            return (orbMethod != null && orbMethod.RequiredCurrency.HasValue && orbMethod.RequiredCurrency.Value > 0)
                ? orbMethod.RequiredCurrency.Value
                : null;
        }
    }

    public string OrbQuantityInputText
    {
        get => UnitRequiredOrbs.HasValue ? UnitRequiredOrbs.Value.ToString() : "";
        set
        {
            string clean = value?.Trim() ?? "";
            if (int.TryParse(clean, out int qty) && qty > 0)
            {
                if (UnitRequiredOrbs != qty)
                {
                    _optimizationService.SetOrbCost(Name, qty);
                    NotifyOrbChanges();
                    _onOptimizationTriggered?.Invoke();
                }
            }
            else
            {
                if (UnitRequiredOrbs.HasValue)
                {
                    _optimizationService.SetOrbCost(Name, null);
                    NotifyOrbChanges();
                    _onOptimizationTriggered?.Invoke();
                }
            }
            OnPropertyChanged(nameof(OrbQuantityInputText));
        }
    }

    public string OrbGoldEquivalentText
    {
        get
        {
            if (UnitRequiredOrbs.HasValue)
            {
                long unitGoldCost = (long)UnitRequiredOrbs.Value * _optimizationService.OrbUnitGoldValue;
                return $"({unitGoldCost:N0} G)";
            }
            return "-";
        }
    }

    public string OrbCostText
    {
        get
        {
            if (UnitRequiredOrbs.HasValue)
            {
                long unitGoldCost = (long)UnitRequiredOrbs.Value * _optimizationService.OrbUnitGoldValue;
                return $"{UnitRequiredOrbs.Value:N0}개\n({unitGoldCost:N0} G)";
            }
            return "-";
        }
    }

    public string OrbUnitText => UnitRequiredOrbs.HasValue ? $"{UnitRequiredOrbs.Value:N0}개" : "-";

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetField(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(EditViewVisibility));
                OnPropertyChanged(nameof(EditInputVisibility));
                OnPropertyChanged(nameof(OrbViewVisibility));
                OnPropertyChanged(nameof(OrbEditVisibility));
            }
        }
    }

    public Visibility EditViewVisibility => IsEditMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditInputVisibility => IsEditMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OrbViewVisibility => IsEditMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OrbEditVisibility => IsEditMode ? Visibility.Visible : Visibility.Collapsed;

    public void NotifyOrbChanges()
    {
        OnPropertyChanged(nameof(UnitRequiredOrbs));
        OnPropertyChanged(nameof(OrbQuantityInputText));
        OnPropertyChanged(nameof(OrbUnitText));
        OnPropertyChanged(nameof(OrbGoldEquivalentText));
        OnPropertyChanged(nameof(OrbCostText));
        OnPropertyChanged(nameof(OrbInfo));
        OnPropertyChanged(nameof(OrbTotalCost));
        RecalculateAllCosts();
    }

    // 기타 획득처
    private bool _hasSubRecipe;
    public bool HasSubRecipe
    {
        get => _hasSubRecipe;
        set
        {
            if (SetField(ref _hasSubRecipe, value))
            {
                OnPropertyChanged(nameof(OtherMethodsText));
            }
        }
    }

    public string OtherMethodsText
    {
        get
        {
            var parts = new List<string>();
            var methods = _optimizationService.GetMethodsForItem(Name);

            // NPC
            var npcMethod = methods.Find(m => string.Equals(m.Type, "Npc", StringComparison.OrdinalIgnoreCase));
            if (npcMethod?.UnitGoldCost != null && npcMethod.UnitGoldCost.Value > 0)
            {
                if (!string.IsNullOrWhiteSpace(npcMethod.NpcName))
                {
                    parts.Add($"NPC: {npcMethod.NpcName} / {npcMethod.UnitGoldCost.Value:N0} G");
                }
                else
                {
                    parts.Add($"NPC / {npcMethod.UnitGoldCost.Value:N0} G");
                }
            }

            // 두카트
            var ducatMethod = methods.Find(m => string.Equals(m.Type, "Ducat", StringComparison.OrdinalIgnoreCase));
            if (ducatMethod?.RequiredCurrency != null && ducatMethod.RequiredCurrency.Value > 0)
            {
                if (!string.IsNullOrWhiteSpace(ducatMethod.NpcName))
                {
                    parts.Add($"두카트 {ducatMethod.RequiredCurrency.Value:N0} ({ducatMethod.NpcName})");
                }
                else
                {
                    parts.Add($"두카트 {ducatMethod.RequiredCurrency.Value:N0}");
                }
            }

            // 제작
            bool hasCraft = _optimizationService.HasCraftMethod(Name);
            if (hasCraft)
            {
                parts.Add(HasSubRecipe ? "하위 레시피 제작 가능" : "제작 가능");
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : "-";
        }
    }

    // 최적 추천
    private OptimizationResult? _optimizationResult;
    public OptimizationResult? OptimizationResult
    {
        get => _optimizationResult;
        set
        {
            if (SetField(ref _optimizationResult, value))
            {
                OnPropertyChanged(nameof(RecommendedMethodText));
                OnPropertyChanged(nameof(RecommendedMethodWithCostText));
                OnPropertyChanged(nameof(FinalCostText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(OrbCostText));
            }
        }
    }

    public string RecommendedMethodText
    {
        get
        {
            if (MissingQuantity == 0) return "완료";
            if (OptimizationResult == null) return "-";
            if (!OptimizationResult.IsFeasible) return "조달 불가";

            return OptimizationResult.SelectedMethod switch
            {
                "Inventory" => "완료",
                "Auction" => "경매장",
                "Npc" => "NPC",
                "Ducat" => "두카트",
                "Orb" => "구슬",
                "Craft" => "직접 제작",
                _ => "조달 불가"
            };
        }
    }

    public string RecommendedMethodWithCostText
    {
        get
        {
            if (MissingQuantity == 0)
            {
                return "완료";
            }
            if (OptimizationResult == null)
            {
                return "-\n-";
            }
            if (!OptimizationResult.IsFeasible)
            {
                return "조달 불가\n가격 정보 없음";
            }

            string methodText = OptimizationResult.SelectedMethod switch
            {
                "Auction" => "경매장",
                "Orb" => "구슬",
                "Npc" => "NPC",
                "Ducat" => "두카트",
                "Craft" => "직접 제작",
                "Inventory" => "완료",
                _ => "조달 불가"
            };

            if (methodText == "완료")
            {
                return "완료";
            }

            string costText = $"{OptimizationResult.TotalCost:N0} G";
            return $"{methodText}\n{costText}";
        }
    }

    // 최종 비용
    public string FinalCostText
    {
        get
        {
            if (MissingQuantity == 0) return "0 G";
            if (OptimizationResult == null || !OptimizationResult.IsFeasible) return "-";
            return $"{OptimizationResult.TotalCost:N0} G";
        }
    }

    public MaterialItemViewModel(
        string name, 
        int requiredQuantity, 
        int initialOwnedQuantity, 
        OptimizationService optimizationService,
        bool hasSubRecipe = false,
        Action<string, int>? onQuantityChanged = null,
        Action? onOptimizationTriggered = null,
        Action<string, long?>? onAuctionPriceChanged = null)
    {
        Name = name;
        RequiredQuantity = requiredQuantity;
        _ownedQuantity = Math.Max(initialOwnedQuantity, 0);
        _optimizationService = optimizationService;
        _hasSubRecipe = hasSubRecipe;
        _onQuantityChanged = onQuantityChanged;
        _onOptimizationTriggered = onOptimizationTriggered;
        _onAuctionPriceChanged = onAuctionPriceChanged;
    }

    private DateTime? _lastUpdated;
    public DateTime? LastUpdated => _lastUpdated;

    public void SetAuctionPrice(long unitPrice, DateTime? lastUpdated = null)
    {
        _auctionUnitPrice = unitPrice;
        _lastUpdated = lastUpdated ?? DateTime.Now;
        _isAuctionAvailable = true;
        _errorMessage = null;
        _isLoading = false;
        NotifyAuctionChanges();
    }

    public void SetAuctionLoading()
    {
        _isLoading = true;
        _errorMessage = null;
        NotifyAuctionChanges();
    }

    public void SetAuctionError(string message)
    {
        _auctionUnitPrice = null;
        _isAuctionAvailable = false;
        _isLoading = false;
        _errorMessage = message;
        NotifyAuctionChanges();
    }

    public void RecalculateAllCosts()
    {
        OnPropertyChanged(nameof(OrbInfo));
        OnPropertyChanged(nameof(OrbTotalCost));
        OnPropertyChanged(nameof(OrbCostText));
        OnPropertyChanged(nameof(AuctionTotalCost));
        OnPropertyChanged(nameof(RecommendedMethodText));
        OnPropertyChanged(nameof(RecommendedMethodWithCostText));
        OnPropertyChanged(nameof(FinalCostText));
        OnPropertyChanged(nameof(StatusText));
    }

    private void NotifyAuctionChanges()
    {
        OnPropertyChanged(nameof(AuctionUnitPrice));
        OnPropertyChanged(nameof(AuctionUnitPriceInputText));
        OnPropertyChanged(nameof(AuctionUnitPriceText));
        OnPropertyChanged(nameof(IsAuctionAvailable));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(AuctionTotalCost));
        OnPropertyChanged(nameof(FinalCostText));
        OnPropertyChanged(nameof(RecommendedMethodWithCostText));
        RecalculateAllCosts();
    }
}
