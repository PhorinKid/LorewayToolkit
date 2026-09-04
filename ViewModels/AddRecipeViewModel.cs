using System;
using System.Collections.ObjectModel;
using System.Linq;
using MabinogiCraftOptimizer.Models;
using MabinogiCraftOptimizer.Services;

namespace MabinogiCraftOptimizer.ViewModels;

public class RecipeMaterialItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int? OrbCount { get; set; }
    public string OrbCountDisplay => OrbCount.HasValue ? $"{OrbCount.Value:N0}개" : "-";
}

public class CategoryOption
{
    public string Display { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 레시피 추가 ViewModel
/// </summary>
public class AddRecipeViewModel : ViewModelBase
{
    private readonly RecipeService _recipeService;
    private readonly OptimizationService _optimizationService;

    public List<CategoryOption> AvailableCategories { get; } = new()
    {
        new CategoryOption { Display = "제작재료", Value = RecipeService.CategoryCraftMaterial },
        new CategoryOption { Display = "무기", Value = RecipeService.CategoryWeapon }
    };

    private CategoryOption _selectedCategory;
    public CategoryOption SelectedCategory
    {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }

    private string _itemName = string.Empty;
    public string ItemName
    {
        get => _itemName;
        set
        {
            if (SetField(ref _itemName, value))
            {
                ClearStatus();
            }
        }
    }

    private string _outputQuantityText = "1";
    public string OutputQuantityText
    {
        get => _outputQuantityText;
        set
        {
            if (SetField(ref _outputQuantityText, value))
            {
                ClearStatus();
            }
        }
    }

    private string _materialNameInput = string.Empty;
    public string MaterialNameInput
    {
        get => _materialNameInput;
        set
        {
            if (SetField(ref _materialNameInput, value))
            {
                ClearStatus();
            }
        }
    }

    private string _materialQuantityInputText = "1";
    public string MaterialQuantityInputText
    {
        get => _materialQuantityInputText;
        set
        {
            if (SetField(ref _materialQuantityInputText, value))
            {
                ClearStatus();
            }
        }
    }

    // 재료의 구슬 교환 갯수 (선택 사항)
    private string _materialOrbCountText = string.Empty;
    public string MaterialOrbCountText
    {
        get => _materialOrbCountText;
        set
        {
            if (SetField(ref _materialOrbCountText, value))
            {
                ClearStatus();
            }
        }
    }

    public ObservableCollection<RecipeMaterialItem> Materials { get; } = new();

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public AddRecipeViewModel(RecipeService recipeService, OptimizationService optimizationService)
    {
        _recipeService = recipeService;
        _optimizationService = optimizationService;
        _selectedCategory = AvailableCategories[0]; // 기본값: 제작재료
    }

    public void ClearStatus()
    {
        ErrorMessage = null;
    }

    /// <summary>
    /// 입력창의 재료를 현재 임시 재료 목록에 추가합니다.
    /// </summary>
    public bool TryAddMaterial()
    {
        string cleanMatName = MaterialNameInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanMatName))
        {
            ErrorMessage = "재료 이름을 입력해주세요.";
            return false;
        }

        if (!int.TryParse(MaterialQuantityInputText?.Trim(), out int qty) || qty <= 0)
        {
            ErrorMessage = "재료 수량은 1 이상의 정수여야 합니다.";
            return false;
        }

        string cleanResultItem = ItemName?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(cleanResultItem) && string.Equals(cleanMatName, cleanResultItem, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "자기 자신을 제작 재료로 등록할 수 없습니다.";
            return false;
        }

        int? orbCount = null;
        if (!string.IsNullOrWhiteSpace(MaterialOrbCountText) && int.TryParse(MaterialOrbCountText.Trim(), out int parsedOrb) && parsedOrb > 0)
        {
            orbCount = parsedOrb;
        }

        // 이미 목록에 등록된 재료라면 수량 합산 및 구슬 갱신
        var existing = Materials.FirstOrDefault(m => string.Equals(m.Name, cleanMatName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Quantity += qty;
            if (orbCount.HasValue) existing.OrbCount = orbCount;
            int idx = Materials.IndexOf(existing);
            Materials[idx] = new RecipeMaterialItem { Name = existing.Name, Quantity = existing.Quantity, OrbCount = existing.OrbCount };
        }
        else
        {
            Materials.Add(new RecipeMaterialItem { Name = cleanMatName, Quantity = qty, OrbCount = orbCount });
        }

        MaterialNameInput = string.Empty;
        MaterialQuantityInputText = "1";
        MaterialOrbCountText = string.Empty;
        ClearStatus();
        return true;
    }

    /// <summary>
    /// 목록에서 선택된 재료를 제거합니다.
    /// </summary>
    public void RemoveMaterial(RecipeMaterialItem mat)
    {
        if (mat != null)
        {
            Materials.Remove(mat);
            ClearStatus();
        }
    }

    /// <summary>
    /// RecipeService를 통해 유효성 검사 및 영구 저장을 수행하고, 입력된 구슬 교환 정보를 등록합니다.
    /// </summary>
    public (bool Success, string Message) SaveRecipe()
    {
        string cleanItemName = ItemName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanItemName))
        {
            ErrorMessage = "결과 아이템 이름을 입력해주세요.";
            return (false, ErrorMessage);
        }

        if (!int.TryParse(OutputQuantityText?.Trim(), out int outQty) || outQty <= 0)
        {
            ErrorMessage = "결과 수량은 1 이상의 정수여야 합니다.";
            return (false, ErrorMessage);
        }

        if (Materials.Count == 0)
        {
            ErrorMessage = "최소 1개 이상의 재료를 추가해주세요.";
            return (false, ErrorMessage);
        }

        var newRecipe = new Recipe
        {
            ItemName = cleanItemName,
            OutputQuantity = outQty,
            Category = SelectedCategory?.Value ?? RecipeService.CategoryCraftMaterial,
            Materials = Materials.Select(m => new Material { Name = m.Name, Quantity = m.Quantity }).ToList()
        };

        var (success, message) = _recipeService.AddUserRecipe(newRecipe);
        if (!success)
        {
            ErrorMessage = message;
            return (false, ErrorMessage);
        }

        // 각 재료의 구슬 갯수 등록 (입력된 경우)
        foreach (var mat in Materials)
        {
            if (mat.OrbCount.HasValue && mat.OrbCount.Value > 0)
            {
                _optimizationService.SetOrbCost(mat.Name, mat.OrbCount.Value);
            }
        }

        return (true, message);
    }
}
