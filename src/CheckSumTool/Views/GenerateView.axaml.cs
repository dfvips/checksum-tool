using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CheckSumTool.Helpers;
using CheckSumTool.ViewModels;

namespace CheckSumTool.Views;

public partial class GenerateView : UserControl
{
    private readonly Border _dropArea;

    public GenerateView()
    {
        InitializeComponent();

        _dropArea = this.FindControl<Border>("DropArea")!;
        DropZone.Attach(_dropArea, AddPaths);
        _dropArea.AddHandler(PointerPressedEvent, OnDropAreaPressed, RoutingStrategies.Bubble);
    }

    private void AddPaths(IReadOnlyList<string> paths)
    {
        if (DataContext is GenerateViewModel vm)
        {
            vm.AddFiles(paths);
        }
    }

    private async void OnDropAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GenerateViewModel vm || vm.IsRunning || !IsLeftClick(e))
        {
            return;
        }

        e.Handled = true;

        try
        {
            var paths = await FilePicker.PickFilesAsync(this, vm.Loc["GenerateDropHint"]);
            if (paths.Count > 0)
            {
                vm.AddFiles(paths);
            }
        }
        catch (Exception ex)
        {
            vm.SetError("StatusPickFailed", ex.Message);
        }
    }

    private async void OnPickFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GenerateViewModel vm || vm.IsRunning)
        {
            return;
        }

        try
        {
            var paths = await FilePicker.PickFoldersAsync(this, vm.Loc["PickFolder"]);
            if (paths.Count > 0)
            {
                vm.AddFiles(paths);
            }
        }
        catch (Exception ex)
        {
            vm.SetError("StatusPickFolderFailed", ex.Message);
        }
    }

    private bool IsLeftClick(PointerPressedEventArgs e)
        => e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
}
