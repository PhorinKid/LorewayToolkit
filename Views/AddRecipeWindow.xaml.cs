using System.Windows;
using System.Windows.Input;
using MabinogiCraftOptimizer.Models;
using MabinogiCraftOptimizer.Services;
using MabinogiCraftOptimizer.ViewModels;

namespace MabinogiCraftOptimizer.Views;

public partial class AddRecipeWindow : Window
{
    public AddRecipeViewModel ViewModel { get; }

    public AddRecipeWindow(RecipeService recipeService, OptimizationService optimizationService)
    {
        InitializeComponent();
        ViewModel = new AddRecipeViewModel(recipeService, optimizationService);
        DataContext = ViewModel;
    }

    private void OnAddMaterialClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryAddMaterial())
        {
            MatNameTextBox.Focus();
        }
    }

    private void OnMaterialInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModel.TryAddMaterial())
            {
                MatNameTextBox.Focus();
            }
        }
    }

    private void OnRemoveMaterialClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecipeMaterialItem mat })
        {
            ViewModel.RemoveMaterial(mat);
        }
    }

    private void OnSaveRecipeClick(object sender, RoutedEventArgs e)
    {
        var (success, _) = ViewModel.SaveRecipe();
        if (success)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
