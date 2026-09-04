using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using MabinogiCraftOptimizer.Models;

namespace MabinogiCraftOptimizer.Services;

/// <summary>
/// 기본 레시피 및 사용자 레시피 통합 로드/저장 서비스
/// </summary>
public class RecipeService
{
    private readonly string _defaultRecipePath;
    private readonly string _userRecipePath;
    private readonly string? _projectUserRecipePath;
    private readonly object _fileLock = new();

    public const string CategoryWeapon = "Weapon";
    public const string CategoryCraftMaterial = "CraftMaterial";
    public const string CategoryMaterial = "Material";

    public RecipeService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _defaultRecipePath = Path.Combine(baseDir, "Data", "default-recipes.json");
        _userRecipePath = Path.Combine(baseDir, "Data", "user-recipes.json");

        string projPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Data", "user-recipes.json"));
        if (Directory.Exists(Path.GetDirectoryName(projPath)))
        {
            _projectUserRecipePath = projPath;
        }
    }

    /// <summary>
    /// 기본 및 사용자 추가 레시피를 통합 로드
    /// </summary>
    public List<Recipe> LoadAllRecipes()
    {
        var allRecipes = new List<Recipe>();

        if (File.Exists(_defaultRecipePath))
        {
            allRecipes.AddRange(LoadRecipesFromFile(_defaultRecipePath));
        }

        if (File.Exists(_userRecipePath))
        {
            allRecipes.AddRange(LoadRecipesFromFile(_userRecipePath));
        }

        return allRecipes;
    }

    /// <summary>
    /// 사용자 추가 레시피 로드
    /// </summary>
    public List<Recipe> LoadUserRecipes()
    {
        lock (_fileLock)
        {
            if (File.Exists(_userRecipePath))
            {
                return LoadRecipesFromFile(_userRecipePath);
            }
            if (!string.IsNullOrEmpty(_projectUserRecipePath) && File.Exists(_projectUserRecipePath))
            {
                return LoadRecipesFromFile(_projectUserRecipePath);
            }
            return new List<Recipe>();
        }
    }

    /// <summary>
    /// 아이템 카테고리 판별 (Weapon, CraftMaterial, Material)
    /// </summary>
    public string GetItemCategory(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return CategoryMaterial;

        var all = LoadAllRecipes();
        var recipe = all.FirstOrDefault(r => string.Equals(r.ItemName.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (recipe == null) return CategoryMaterial;

        return string.Equals(recipe.Category, CategoryWeapon, StringComparison.OrdinalIgnoreCase)
            ? CategoryWeapon
            : CategoryCraftMaterial;
    }

    public bool IsRecipeExists(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        return LoadAllRecipes().Any(r => string.Equals(r.ItemName.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 검증 후 사용자 레시피 추가 및 파일 저장
    /// </summary>
    public (bool Success, string Message) AddUserRecipe(Recipe newRecipe)
    {
        if (newRecipe == null)
            return (false, "레시피 정보가 올바르지 않습니다.");

        string cleanItemName = newRecipe.ItemName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanItemName))
            return (false, "결과 아이템 이름을 입력해주세요.");

        if (newRecipe.OutputQuantity <= 0)
            return (false, "결과 수량은 1개 이상이어야 합니다.");

        if (newRecipe.Materials == null || newRecipe.Materials.Count == 0)
            return (false, "최소 1개 이상의 재료를 등록해야 합니다.");

        foreach (var mat in newRecipe.Materials)
        {
            if (string.IsNullOrWhiteSpace(mat.Name))
                return (false, "재료 이름이 비어 있는 항목이 있습니다.");
            if (mat.Quantity <= 0)
                return (false, $"재료 '{mat.Name}'의 수량은 1개 이상이어야 합니다.");
        }

        if (newRecipe.Materials.Any(m => string.Equals(m.Name.Trim(), cleanItemName, StringComparison.OrdinalIgnoreCase)))
            return (false, "자기 자신을 제작 재료로 등록할 수 없습니다.");

        if (IsRecipeExists(cleanItemName))
            return (false, $"이미 '{cleanItemName}'의 Recipe가 존재합니다.");

        var allExisting = LoadAllRecipes();
        if (CheckCycle(newRecipe, allExisting, out string cyclePath))
            return (false, $"순환 참조가 발생하여 저장할 수 없습니다: {cyclePath}");

        string finalCategory = string.Equals(newRecipe.Category?.Trim(), CategoryWeapon, StringComparison.OrdinalIgnoreCase)
            ? CategoryWeapon
            : CategoryCraftMaterial;

        lock (_fileLock)
        {
            try
            {
                var userRecipes = LoadUserRecipes();
                newRecipe.ItemName = cleanItemName;
                newRecipe.Category = finalCategory;
                foreach (var mat in newRecipe.Materials)
                {
                    mat.Name = mat.Name.Trim();
                }

                userRecipes.Add(newRecipe);

                string? dir = Path.GetDirectoryName(_userRecipePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(userRecipes, options);
                File.WriteAllText(_userRecipePath, json);

                if (!string.IsNullOrEmpty(_projectUserRecipePath))
                {
                    try { File.WriteAllText(_projectUserRecipePath, json); } catch { }
                }

                return (true, $"'{cleanItemName}' 레시피가 성공적으로 등록되었습니다.");
            }
            catch (Exception ex)
            {
                return (false, $"레시피 저장 중 오류가 발생했습니다: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 레시피 간 순환 참조(사이클) 여부 DFS 검사
    /// </summary>
    private bool CheckCycle(Recipe candidate, List<Recipe> existingRecipes, out string cyclePath)
    {
        cyclePath = string.Empty;
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in existingRecipes)
        {
            if (!adj.ContainsKey(r.ItemName)) adj[r.ItemName] = new List<string>();
            foreach (var m in r.Materials)
            {
                adj[r.ItemName].Add(m.Name.Trim());
            }
        }

        string candName = candidate.ItemName.Trim();
        if (!adj.ContainsKey(candName)) adj[candName] = new List<string>();
        foreach (var m in candidate.Materials)
        {
            adj[candName].Add(m.Name.Trim());
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string> { candName };
        string detectedPath = string.Empty;

        bool Dfs(string current)
        {
            if (!adj.TryGetValue(current, out var neighbors)) return false;

            foreach (var next in neighbors)
            {
                if (string.Equals(next, candName, StringComparison.OrdinalIgnoreCase))
                {
                    path.Add(next);
                    detectedPath = string.Join(" → ", path);
                    return true;
                }

                if (visited.Add(next))
                {
                    path.Add(next);
                    if (Dfs(next)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
            return false;
        }

        bool hasCycle = Dfs(candName);
        cyclePath = detectedPath;
        return hasCycle;
    }

    /// <summary>
    /// JSON 파일에서 Recipe 리스트 역직렬화
    /// </summary>
    private List<Recipe> LoadRecipesFromFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var list = JsonSerializer.Deserialize<List<Recipe>>(json, options) ?? new List<Recipe>();
            foreach (var r in list)
            {
                if (string.IsNullOrWhiteSpace(r.Category))
                {
                    r.Category = CategoryCraftMaterial;
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[오류] JSON 파일 읽기 실패 ({filePath}): {ex.Message}");
            return new List<Recipe>();
        }
    }
}
