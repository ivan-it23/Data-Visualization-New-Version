using System.Runtime.InteropServices;
using AMK.DataVisualization.IO.DataFiles;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class FileConstructorWindow : Window
{
    private const int WindowOwnerIndex = -8;
    private readonly Action<string> fileSaved;
    private readonly List<FileConstructorSegment> segments = [];
    private string savedPath = string.Empty;
    private bool allowPermanentClose;
    private bool activated;
    private bool isBusy;

    internal FileConstructorWindow(Action<string> fileSaved)
    {
        this.fileSaved = fileSaved;
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(820, 520));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = false;
        }

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DataVisualization.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        AppWindow.Closing += AppWindow_Closing;
        RefreshView();
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        if (ownerHandle != IntPtr.Zero)
        {
            SetWindowLongPtr(windowHandle, WindowOwnerIndex, ownerHandle);
        }

        if (!activated)
        {
            Activate();
            activated = true;
        }
        else
        {
            AppWindow.Show();
            Activate();
        }
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        Close();
    }

    public void ResetForOpenedDataFile()
    {
        segments.Clear();
        savedPath = string.Empty;
        SegmentsListView.SelectedIndex = -1;
        RefreshView();
    }

    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = CreateDataFilePicker("Добавить файлы данных");
        IReadOnlyList<Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            List<string> errors = [];
            foreach (Windows.Storage.StorageFile file in files)
            {
                try
                {
                    FileConstructorSegment segment = await Task.Run(() => ToolDataFileConstructor.LoadSegment(file.Path));
                    segments.Add(segment);
                }
                catch (Exception exception) when (IsExpectedFileException(exception))
                {
                    errors.Add($"{file.Name}: {exception.Message}");
                }
            }

            RefreshView(segments.Count - 1);
            if (errors.Count > 0)
            {
                await ShowMessageAsync("Не все файлы добавлены", string.Join(Environment.NewLine, errors));
            }
        });
    }

    private async void OpenAssemblyButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            CommitButtonText = "Открыть сборку"
        };
        picker.FileTypeFilter.Add(".bin");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                IReadOnlyList<FileConstructorSegment> loaded = await Task.Run(() => ToolDataFileConstructor.LoadAssembly(file.Path));
                segments.Clear();
                segments.AddRange(loaded);
                savedPath = file.Path;
                RefreshView(0);
            }
            catch (Exception exception) when (IsExpectedFileException(exception))
            {
                await ShowMessageAsync("Не удалось открыть сборку", exception.Message);
            }
        });
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        int selectedIndex = SegmentsListView.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= segments.Count)
        {
            return;
        }

        segments.RemoveAt(selectedIndex);
        RefreshView(Math.Min(selectedIndex, segments.Count - 1));
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedSegment(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedSegment(1);
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = "constructed_file"
        };
        picker.FileTypeChoices.Add("AMK BIN", [".bin"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await SaveAsync(file.Path, isResave: false);
    }

    private async void ResaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            await SaveAsync(savedPath, isResave: true);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
    }

    private void SegmentsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
    }

    private async Task SaveAsync(string outputPath, bool isResave)
    {
        await RunBusyAsync(async () =>
        {
            try
            {
                FileConstructorSaveResult result = await Task.Run(
                    () => ToolDataFileConstructor.SaveConstructedFile(outputPath, segments));
                savedPath = outputPath;
                RefreshView();
                if (result.LayoutSaved)
                {
                    await ShowMessageAsync(
                        "Готово",
                        $"Файл успешно {(isResave ? "обновлён" : "создан")}:{Environment.NewLine}{outputPath}");
                }
                else
                {
                    await ShowMessageAsync("Файл сохранён с предупреждением", result.Warning ?? "Не сохранены границы сегментов.");
                }

                fileSaved(outputPath);
            }
            catch (Exception exception) when (IsExpectedFileException(exception))
            {
                await ShowMessageAsync("Не удалось сохранить сборку", exception.Message);
            }
        });
    }

    private void MoveSelectedSegment(int delta)
    {
        int selectedIndex = SegmentsListView.SelectedIndex;
        int targetIndex = selectedIndex + delta;
        if (selectedIndex < 0 || selectedIndex >= segments.Count || targetIndex < 0 || targetIndex >= segments.Count)
        {
            return;
        }

        (segments[selectedIndex], segments[targetIndex]) = (segments[targetIndex], segments[selectedIndex]);
        RefreshView(targetIndex);
    }

    private void RefreshView(int selectedIndex = -1)
    {
        uint expectedSignature = segments.FirstOrDefault(segment => segment.IsHomogeneous)?.Signature ?? 0;
        SegmentsListView.Items.Clear();
        int totalRecords = 0;
        for (int index = 0; index < segments.Count; index++)
        {
            FileConstructorSegment segment = segments[index];
            bool compatible = segment.IsHomogeneous && segment.Signature == expectedSignature;
            totalRecords += segment.RecordCount;
            SegmentsListView.Items.Add(
                $"{index + 1}. {(compatible ? "совместимо" : "не совместимо")} | " +
                $"{segment.RecordCount} кадров | sig=0x{segment.Signature:X8} | {segment.Label}");
        }

        if (segments.Count == 0)
        {
            StatusTextBlock.Text = string.Empty;
        }
        else if (ToolDataFileConstructor.AreCompatible(segments, out _))
        {
            StatusTextBlock.Text = $"Совместимо: все файлы относятся к одному прибору. Всего кадров: {totalRecords}.";
        }
        else
        {
            StatusTextBlock.Text = "Не совместимо: сигнатуры файлов отличаются или один из файлов содержит смешанные сигнатуры.";
        }

        if (selectedIndex >= 0 && selectedIndex < segments.Count)
        {
            SegmentsListView.SelectedIndex = selectedIndex;
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        int selectedIndex = SegmentsListView.SelectedIndex;
        bool canSave = !isBusy && ToolDataFileConstructor.AreCompatible(segments, out _);
        RemoveButton.IsEnabled = !isBusy && selectedIndex >= 0;
        MoveUpButton.IsEnabled = !isBusy && selectedIndex > 0;
        MoveDownButton.IsEnabled = !isBusy && selectedIndex >= 0 && selectedIndex < segments.Count - 1;
        SaveAsButton.IsEnabled = canSave;
        ResaveButton.IsEnabled = canSave && !string.IsNullOrWhiteSpace(savedPath);
    }

    private FileOpenPicker CreateDataFilePicker(string commitButtonText)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            CommitButtonText = commitButtonText
        };
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".biv");
        picker.FileTypeFilter.Add(".DEV");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return picker;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        BusyProgressRing.IsActive = true;
        UpdateButtons();
        try
        {
            await action();
        }
        finally
        {
            isBusy = false;
            BusyProgressRing.IsActive = false;
            UpdateButtons();
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "ОК"
        };
        await dialog.ShowAsync();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowPermanentClose)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private static bool IsExpectedFileException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or NotSupportedException;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);
}
