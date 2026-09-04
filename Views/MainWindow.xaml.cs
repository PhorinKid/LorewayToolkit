using System.Windows;
using MabinogiCraftOptimizer.ViewModels;

namespace MabinogiCraftOptimizer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // 일반 시세 갱신 (유효 캐시 재사용)
    private async void OnRefreshNormalPricesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.RefreshAllPricesAsync(forceRefresh: false);
        }
    }

    // 강제 시세 갱신 (전체 재조회)
    private async void OnRefreshForcePricesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.RefreshAllPricesAsync(forceRefresh: true);
        }
    }

    // 레시피 추가 창 열기
    private void OnAddRecipeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var addWindow = new AddRecipeWindow(vm.RecipeService, vm.OptimizationService)
            {
                Owner = this
            };

            if (addWindow.ShowDialog() == true)
            {
                string addedItemName = addWindow.ViewModel.ItemName;
                vm.ReloadAllRecipes(addedItemName);
            }
        }
    }

    // 수정/저장 모드 토글
    private void OnToggleOrbEditClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ToggleEditMode();
        }
    }
}
