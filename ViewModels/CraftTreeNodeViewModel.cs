using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MabinogiCraftOptimizer.Models;

namespace MabinogiCraftOptimizer.ViewModels;

/// <summary>
/// 제작 트리 노드 ViewModel
/// </summary>
public class CraftTreeNodeViewModel : ViewModelBase
{
    private Action<string, int>? _onQuantityChanged;
    private Action? _onOptimizationTriggered;

    public string ItemName { get; }
    public bool IsRoot { get; }
    public int Depth { get; }

    // 기본 너비 760px, 계층당 19px 들여쓰기 축소
    public double NodeWidth => IsRoot ? double.NaN : Math.Max(760 - (Depth - 1) * 19, 400);

    public Visibility DetailVisibility => IsRoot ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RootBadgeVisibility => IsRoot ? Visibility.Visible : Visibility.Collapsed;

    private int _requiredQuantity;
    public int RequiredQuantity
    {
        get => _requiredQuantity;
        set
        {
            if (SetField(ref _requiredQuantity, value))
            {
                OnPropertyChanged(nameof(MissingQuantity));
                OnPropertyChanged(nameof(QuantitySummaryText));
            }
        }
    }

    private int _ownedQuantity;
    public int OwnedQuantity
    {
        get => _ownedQuantity;
        set
        {
            int safeValue = Math.Max(value, 0);
            _ownedQuantity = safeValue;
            OnPropertyChanged(nameof(OwnedQuantity));
            OnPropertyChanged(nameof(OwnedQuantityDisplay));
            OnPropertyChanged(nameof(MissingQuantity));
            OnPropertyChanged(nameof(QuantitySummaryText));
            OnPropertyChanged(nameof(MethodDisplayText));
            OnPropertyChanged(nameof(CostDisplayText));

            _onQuantityChanged?.Invoke(ItemName, safeValue);
            _onOptimizationTriggered?.Invoke();
        }
    }

    public string OwnedQuantityDisplay => $"{OwnedQuantity}개";

    private string _ownedQuantityText = "0";
    public string OwnedQuantityText
    {
        get => _ownedQuantityText;
        set
        {
            if (SetField(ref _ownedQuantityText, value))
            {
                if (int.TryParse(value, out int parsed))
                {
                    int safeValue = Math.Max(parsed, 0);
                    if (_ownedQuantity != safeValue)
                    {
                        _ownedQuantity = safeValue;
                        OnPropertyChanged(nameof(OwnedQuantity));
                        OnPropertyChanged(nameof(OwnedQuantityDisplay));
                        _onQuantityChanged?.Invoke(ItemName, safeValue);
                    }
                }
            }
        }
    }

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
                foreach (var child in Children)
                {
                    child.IsEditMode = value;
                }
            }
        }
    }

    public Visibility EditViewVisibility => IsEditMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditInputVisibility => IsEditMode ? Visibility.Visible : Visibility.Collapsed;

    public void SyncOwnedQuantityFromInventory(int inventoryQty)
    {
        _ownedQuantity = Math.Max(inventoryQty, 0);
        _ownedQuantityText = _ownedQuantity.ToString();
        OnPropertyChanged(nameof(OwnedQuantity));
        OnPropertyChanged(nameof(OwnedQuantityText));
        OnPropertyChanged(nameof(OwnedQuantityDisplay));
    }

    public void CommitEdit()
    {
        if (int.TryParse(OwnedQuantityText, out int parsed))
        {
            OwnedQuantity = parsed;
        }
    }

    private int _missingQuantity;
    public int MissingQuantity
    {
        get => _missingQuantity;
        set
        {
            if (SetField(ref _missingQuantity, value))
            {
                OnPropertyChanged(nameof(QuantitySummaryText));
                OnPropertyChanged(nameof(MethodDisplayText));
                OnPropertyChanged(nameof(CostDisplayText));
            }
        }
    }

    private string _selectedMethod = "None";
    public string SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (SetField(ref _selectedMethod, value))
            {
                OnPropertyChanged(nameof(IsCraft));
                OnPropertyChanged(nameof(MethodDisplayText));
            }
        }
    }

    private long _totalCost;
    public long TotalCost
    {
        get => _totalCost;
        set
        {
            if (SetField(ref _totalCost, value))
            {
                OnPropertyChanged(nameof(CostDisplayText));
            }
        }
    }

    private bool _isFeasible = true;
    public bool IsFeasible
    {
        get => _isFeasible;
        set
        {
            if (SetField(ref _isFeasible, value))
            {
                OnPropertyChanged(nameof(MethodDisplayText));
                OnPropertyChanged(nameof(CostDisplayText));
            }
        }
    }

    public bool IsCraft => string.Equals(SelectedMethod, "Craft", StringComparison.OrdinalIgnoreCase);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public ObservableCollection<CraftTreeNodeViewModel> Children { get; } = new();

    public CraftTreeNodeViewModel(
        OptimizationResult opt, 
        int userOwnedQuantity, 
        bool isRoot = false,
        Action<string, int>? onQuantityChanged = null, 
        Action? onOptimizationTriggered = null,
        int depth = 0)
    {
        ItemName = opt.ItemName;
        IsRoot = isRoot;
        Depth = depth;
        _isExpanded = isRoot; // 최상위 완제품(Root)만 기본 펼침, 하위 트리는 기본 닫힘
        _requiredQuantity = opt.RequiredQuantity;
        _ownedQuantity = Math.Max(userOwnedQuantity, 0);
        _ownedQuantityText = _ownedQuantity.ToString();
        _missingQuantity = opt.MissingQuantity;
        _selectedMethod = opt.SelectedMethod;
        _totalCost = opt.TotalCost;
        _isFeasible = opt.IsFeasible;
        _onQuantityChanged = onQuantityChanged;
        _onOptimizationTriggered = onOptimizationTriggered;

        if (opt.Children != null && opt.Children.Count > 0)
        {
            foreach (var child in opt.Children)
            {
                Children.Add(new CraftTreeNodeViewModel(child, 0, false, onQuantityChanged, onOptimizationTriggered, depth + 1));
            }
        }
    }

    /// <summary>
    /// 재귀 최적화 결과를 노드에 인플레이스 갱신
    /// </summary>
    public void UpdateInPlace(
        OptimizationResult opt, 
        Func<string, int> getOwnedQtyFunc, 
        Action<string, int> onQuantityChanged, 
        Action onOptimizationTriggered)
    {
        _onQuantityChanged = onQuantityChanged;
        _onOptimizationTriggered = onOptimizationTriggered;

        RequiredQuantity = opt.RequiredQuantity;
        _ownedQuantity = getOwnedQtyFunc(ItemName);
        _ownedQuantityText = _ownedQuantity.ToString();
        OnPropertyChanged(nameof(OwnedQuantity));
        OnPropertyChanged(nameof(OwnedQuantityText));
        OnPropertyChanged(nameof(OwnedQuantityDisplay));

        MissingQuantity = opt.MissingQuantity;
        SelectedMethod = opt.SelectedMethod;
        TotalCost = opt.TotalCost;
        IsFeasible = opt.IsFeasible;

        OnPropertyChanged(nameof(QuantitySummaryText));
        OnPropertyChanged(nameof(MethodDisplayText));
        OnPropertyChanged(nameof(CostDisplayText));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(RootBadgeVisibility));

        // 하위 자식 노드 인플레이스 갱신
        var newChildren = opt.Children ?? new List<OptimizationResult>();

        var newChildNames = new HashSet<string>(newChildren.Select(c => c.ItemName), StringComparer.OrdinalIgnoreCase);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (!newChildNames.Contains(Children[i].ItemName))
            {
                Children.RemoveAt(i);
            }
        }

        foreach (var childOpt in newChildren)
        {
            var existingChild = Children.FirstOrDefault(c => string.Equals(c.ItemName, childOpt.ItemName, StringComparison.OrdinalIgnoreCase));
            if (existingChild != null)
            {
                existingChild.IsEditMode = IsEditMode;
                existingChild.UpdateInPlace(childOpt, getOwnedQtyFunc, onQuantityChanged, onOptimizationTriggered);
            }
            else
            {
                int childOwned = getOwnedQtyFunc(childOpt.ItemName);
                var newChildVm = new CraftTreeNodeViewModel(childOpt, childOwned, false, onQuantityChanged, onOptimizationTriggered, Depth + 1)
                {
                    IsEditMode = IsEditMode
                };
                newChildVm.UpdateInPlace(childOpt, getOwnedQtyFunc, onQuantityChanged, onOptimizationTriggered);
                Children.Add(newChildVm);
            }
        }
    }

    public string MethodDisplayText
    {
        get
        {
            if (MissingQuantity == 0) return "완료";
            if (!IsFeasible) return "조달 불가";

            return SelectedMethod switch
            {
                "Craft" => "직접 제작",
                "Auction" => "경매장 구매",
                "Orb" => "구슬 교환",
                "Npc" => "NPC 상점",
                "Ducat" => "두카트 상점",
                "Inventory" => "완료",
                _ => "조달 불가"
            };
        }
    }

    public string QuantitySummaryText => $"필요 {RequiredQuantity:N0}개 | 부족 {MissingQuantity:N0}개";

    public string CostDisplayText
    {
        get
        {
            if (MissingQuantity == 0) return "0 G";
            if (!IsFeasible) return "가격 정보 없음";
            return $"{TotalCost:N0} G";
        }
    }
}
