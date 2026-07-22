using System.Collections.ObjectModel;
using System.Diagnostics;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Hardware.Serial;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class DeviceLogWindow : Window
{
    private const int WindowWidth = 1040;
    private const int WindowHeight = 680;
    private readonly ObservableCollection<DeviceLogRow> rows = [];
    private readonly DispatcherTimer refreshTimer;
    private bool allowPermanentClose;
    private bool activated;
    private bool forceFullRefresh = true;
    private long renderedGeneration = -1;
    private ScrollViewer? logScrollViewer;

    internal DeviceLogWindow()
    {
        InitializeComponent();
        LogListView.ItemsSource = rows;
        LogPathTextBlock.Text = DeviceExchangeLog.FilePath;
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        AppWindow.Closing += AppWindow_Closing;

        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        refreshTimer.Tick += RefreshTimer_Tick;
        RootGrid.Loaded += RootGrid_Loaded;
        SynchronizeControls();
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
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
            Activate();
        }

        SecondaryWindowIntegration.RestoreIfMinimized(windowHandle);
        SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle);
        DispatcherQueue.TryEnqueue(() =>
            SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle));

        forceFullRefresh = true;
        RefreshLog();
        refreshTimer.Start();
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= RefreshTimer_Tick;
        Close();
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        logScrollViewer = FindDescendant<ScrollViewer>(LogListView);
        RefreshLog();
    }

    private void RefreshTimer_Tick(object? sender, object e)
    {
        RefreshLog();
    }

    private void RefreshLog()
    {
        long generation = DeviceExchangeLog.Generation;
        if (!forceFullRefresh && generation == renderedGeneration)
        {
            return;
        }

        bool wasPinnedToBottom = IsPinnedToBottom();
        IReadOnlyList<DeviceLogLine> snapshot = DeviceExchangeLog.GetLines();
        ApplySnapshot(snapshot, forceFullRefresh);
        forceFullRefresh = false;
        renderedGeneration = generation;
        SynchronizeControls();

        if (wasPinnedToBottom && rows.Count > 0)
        {
            DeviceLogRow last = rows[^1];
            DispatcherQueue.TryEnqueue(() => LogListView.ScrollIntoView(last));
        }
    }

    private void ApplySnapshot(IReadOnlyList<DeviceLogLine> snapshot, bool reset)
    {
        if (reset)
        {
            ReplaceRows(snapshot);
            return;
        }

        if (snapshot.Count == 0)
        {
            rows.Clear();
            return;
        }

        HashSet<long> desiredSequences = snapshot.Select(line => line.Sequence).ToHashSet();
        while (rows.Count > 0 && !desiredSequences.Contains(rows[0].Sequence))
        {
            rows.RemoveAt(0);
        }

        if (rows.Count == 0)
        {
            ReplaceRows(snapshot);
            return;
        }

        int snapshotStart = -1;
        for (int index = 0; index < snapshot.Count; index++)
        {
            if (snapshot[index].Sequence == rows[0].Sequence)
            {
                snapshotStart = index;
                break;
            }
        }

        if (snapshotStart < 0 || snapshotStart + rows.Count > snapshot.Count)
        {
            ReplaceRows(snapshot);
            return;
        }

        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Sequence != snapshot[snapshotStart + index].Sequence)
            {
                ReplaceRows(snapshot);
                return;
            }
        }

        for (int index = snapshotStart + rows.Count; index < snapshot.Count; index++)
        {
            rows.Add(new DeviceLogRow(snapshot[index]));
        }
    }

    private void ReplaceRows(IReadOnlyList<DeviceLogLine> snapshot)
    {
        rows.Clear();
        foreach (DeviceLogLine line in snapshot)
        {
            rows.Add(new DeviceLogRow(line));
        }
    }

    private bool IsPinnedToBottom()
    {
        logScrollViewer ??= FindDescendant<ScrollViewer>(LogListView);
        return logScrollViewer is null
            || logScrollViewer.ScrollableHeight <= 0
            || logScrollViewer.VerticalOffset >= logScrollViewer.ScrollableHeight - 24;
    }

    private void SynchronizeControls()
    {
        bool enabled = DeviceExchangeLog.IsEnabled;
        bool rawAvailable = DeviceExchangeLog.IsRawDataAvailable;
        LoggingToggleButton.IsChecked = enabled;
        RawDataToggleButton.IsChecked = DeviceExchangeLog.ShowRawData;
        RawDataToggleButton.Visibility = rawAvailable ? Visibility.Visible : Visibility.Collapsed;
        RecordingStateIcon.Opacity = enabled ? 1.0 : 0.45;
        RecordingStateTextBlock.Text = enabled ? "Запись включена" : "Запись приостановлена";
        LogSummaryTextBlock.Text = $"Строк: {rows.Count} из {DeviceExchangeLog.MaximumLines}";
        EmptyStatePanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        refreshTimer.Interval = TimeSpan.FromMilliseconds(DeviceExchangeLog.ShowRawData ? 500 : 300);
    }

    private void LoggingToggleButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceExchangeLog.IsEnabled = LoggingToggleButton.IsChecked == true;
        SynchronizeControls();
    }

    private void RawDataToggleButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceExchangeLog.ShowRawData = RawDataToggleButton.IsChecked == true;
        forceFullRefresh = true;
        RefreshLog();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceExchangeLog.Clear();
        forceFullRefresh = true;
        RefreshLog();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        string text = DeviceExchangeLog.GetFullText();
        if (text.Length == 0)
        {
            LogSummaryTextBlock.Text = "Журнал пуст — копировать нечего";
            return;
        }

        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        LogSummaryTextBlock.Text = $"Скопировано строк: {rows.Count}";
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? directory = Path.GetDirectoryName(DeviceExchangeLog.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(DeviceExchangeLog.FilePath))
            {
                Process.Start(new ProcessStartInfo(DeviceExchangeLog.FilePath) { UseShellExecute = true });
            }
            else if (!string.IsNullOrWhiteSpace(directory))
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LogSummaryTextBlock.Text = $"Не удалось открыть журнал: {exception.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
        AppWindow.Hide();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowPermanentClose)
        {
            return;
        }

        refreshTimer.Stop();
        args.Cancel = true;
        sender.Hide();
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}

internal sealed class DeviceLogRow
{
    public DeviceLogRow(DeviceLogLine line)
    {
        Sequence = line.Sequence;
        Text = line.Text;
        FontWeight = line.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        RawBadgeVisibility = line.IsRawPayload ? Visibility.Visible : Visibility.Collapsed;
    }

    public long Sequence { get; }

    public string Text { get; }

    public Windows.UI.Text.FontWeight FontWeight { get; }

    public Visibility RawBadgeVisibility { get; }
}
