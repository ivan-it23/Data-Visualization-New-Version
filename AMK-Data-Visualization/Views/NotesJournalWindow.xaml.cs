using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Charting.Notes;
using AMK.DataVisualization.Charting.Series;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class NotesJournalWindow : Window
{
    private const int WindowWidth = 1040;
    private const int WindowHeight = 590;
    private readonly Func<NotesJournalSource> sourceProvider;
    private readonly Func<int> activeChartProvider;
    private readonly Func<ChartNote, int, NoteOperationResult> configurationApplier;
    private readonly Func<string, string, bool> textUpdater;
    private readonly ObservableCollection<NotesJournalRow> rows = [];
    private bool allowPermanentClose;
    private bool activated;

    internal NotesJournalWindow(
        Func<NotesJournalSource> sourceProvider,
        Func<int> activeChartProvider,
        Func<ChartNote, int, NoteOperationResult> configurationApplier,
        Func<string, string, bool> textUpdater)
    {
        this.sourceProvider = sourceProvider;
        this.activeChartProvider = activeChartProvider;
        this.configurationApplier = configurationApplier;
        this.textUpdater = textUpdater;
        InitializeComponent();
        NotesListView.ItemsSource = rows;
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
        RefreshRows();
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        SecondaryWindowIntegration.ConfigureForOwner(windowHandle, ownerHandle);

        if (!activated)
        {
            Activate();
            activated = true;
        }
        else
        {
            AppWindow.Show();
        }

        SecondaryWindowIntegration.RestoreIfMinimized(windowHandle);
        SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle);
        DispatcherQueue.TryEnqueue(() =>
            SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle));
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        Close();
    }

    private void RefreshRows()
    {
        NotesJournalSource source = sourceProvider();
        rows.Clear();
        if (!source.IsAvailable)
        {
            InfoTextBlock.Text = "Нет активного файла/записи для журнала заметок.";
            return;
        }

        foreach (ChartNote note in source.Notes)
        {
            rows.Add(new NotesJournalRow(note.Copy()));
        }

        int targetChartNumber = activeChartProvider();
        InfoTextBlock.Text = $"Источник: {source.NotesPath} | Заметок: {rows.Count} | График применения: {targetChartNumber}";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRows();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplySelectedAsync();
    }

    private async void NotesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await ApplySelectedAsync();
    }

    private async Task ApplySelectedAsync()
    {
        if (NotesListView.SelectedItem is not NotesJournalRow row)
        {
            await ShowMessageAsync("Журнал заметок", "Выберите заметку в списке.");
            return;
        }

        int targetChartNumber = activeChartProvider();
        NoteOperationResult result = configurationApplier(row.Note, targetChartNumber);
        if (!result.Success)
        {
            await ShowMessageAsync("Журнал заметок", result.ErrorMessage);
            return;
        }

        NotesJournalSource source = sourceProvider();
        InfoTextBlock.Text = source.IsAvailable
            ? $"Источник: {source.NotesPath} | Заметок: {rows.Count} | График применения: {targetChartNumber}"
            : "Нет активного файла/записи для журнала заметок.";
    }

    private async void EditNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string noteId }
            || rows.FirstOrDefault(row => row.NoteId == noteId) is not { } row)
        {
            return;
        }

        TextBox editor = new()
        {
            Text = row.DraftText,
            MaxLength = 4095,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 190,
            MinWidth = 520,
            SelectionStart = 0,
            SelectionLength = row.DraftText.Length
        };
        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Редактирование заметки",
            Content = editor,
            PrimaryButtonText = "ОК",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            row.SetDraft(editor.Text);
        }
    }

    private async void SaveNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string noteId }
            || rows.FirstOrDefault(row => row.NoteId == noteId) is not { } row)
        {
            return;
        }

        if (!row.IsDirty)
        {
            await ShowMessageAsync(
                "Журнал заметок",
                "Нет изменений для сохранения. Нажмите иконку редактирования, чтобы изменить текст.");
            return;
        }

        if (!textUpdater(row.NoteId, row.DraftText))
        {
            await ShowMessageAsync("Заметки", "Не удалось сохранить изменения заметки.");
            return;
        }

        row.AcceptDraft();
        await ShowMessageAsync("Журнал заметок", "Текст заметки сохранён.");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = content,
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
}

internal sealed record NotesJournalSource(bool IsAvailable, string NotesPath, IReadOnlyList<ChartNote> Notes);

internal sealed record NoteOperationResult(bool Success, string ErrorMessage)
{
    public static NoteOperationResult Ok { get; } = new(true, string.Empty);

    public static NoteOperationResult Failed(string errorMessage) => new(false, errorMessage);
}

internal sealed class NotesJournalRow : INotifyPropertyChanged
{
    private string draftText;
    private string preview;

    public NotesJournalRow(ChartNote note)
    {
        Note = note;
        draftText = note.Text;
        preview = BuildPreview(draftText);
        Timestamp = FormatTimestamp(note.CreatedUtcMs);
        Preset = note.PresetName.Length == 0 ? "(без пресета)" : note.PresetName;
        Configuration = BuildConfigurationSummary(note);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChartNote Note { get; }

    public string NoteId => Note.NoteId;

    public string Timestamp { get; }

    public string Preset { get; }

    public string Configuration { get; }

    public string DraftText => draftText;

    public string Preview
    {
        get => preview;
        private set
        {
            preview = value;
            OnPropertyChanged();
        }
    }

    public bool IsDirty => !string.Equals(draftText, Note.Text, StringComparison.Ordinal);

    public void SetDraft(string text)
    {
        draftText = text;
        Preview = BuildPreview(text);
    }

    public void AcceptDraft()
    {
        Note.Text = draftText;
        Preview = BuildPreview(draftText);
    }

    private static string FormatTimestamp(ulong utcMilliseconds)
    {
        if (utcMilliseconds == 0 || utcMilliseconds > long.MaxValue)
        {
            return "-";
        }

        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds((long)utcMilliseconds)
                .LocalDateTime
                .ToString("dd.MM.yyyy HH:mm:ss");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "-";
        }
    }

    private static string BuildConfigurationSummary(ChartNote note)
    {
        if (!NotesModule.ParseConfigSnapshot(
                note.ConfigSnapshot,
                out XAxisType xAxisType,
                out _,
                out List<SnapshotCurveItem> curves))
        {
            return "(ошибка чтения snapshot)";
        }

        return $"X={(xAxisType == XAxisType.Temperature ? "Температура" : "Кадр")}; Кривых={curves.Count}";
    }

    private static string BuildPreview(string text)
    {
        string preview = text.Replace('\r', ' ').Replace('\n', ' ');
        return preview.Length > 80 ? preview[..80] + "..." : preview;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
