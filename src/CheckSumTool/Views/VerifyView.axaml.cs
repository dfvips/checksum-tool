using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CheckSumTool.Helpers;
using CheckSumTool.ViewModels;

namespace CheckSumTool.Views;

public partial class VerifyView : UserControl
{
    private readonly Border _dropArea;

    public VerifyView()
    {
        InitializeComponent();

        _dropArea = this.FindControl<Border>("DropArea")!;
        DropZone.Attach(_dropArea, AddPaths);
        _dropArea.AddHandler(PointerPressedEvent, OnDropAreaPressed, RoutingStrategies.Bubble);
    }

    private void AddPaths(IReadOnlyList<string> paths)
    {
        if (DataContext is VerifyViewModel vm)
        {
            vm.AddFiles(paths);
        }
    }

    private async void OnDropAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not VerifyViewModel vm || vm.IsRunning || !IsLeftClick(e))
        {
            return;
        }

        e.Handled = true;
        await PickFilesAsync(vm);
    }

    private async void OnPickFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VerifyViewModel vm || vm.IsRunning)
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

    private async Task PickFilesAsync(VerifyViewModel vm)
    {
        try
        {
            var paths = await FilePicker.PickFilesAsync(this, vm.Loc["VerifyDropHint"], checksumOnly: true);
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

    private bool IsLeftClick(PointerPressedEventArgs e)
        => e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
}
