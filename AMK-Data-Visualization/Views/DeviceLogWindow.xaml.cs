using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Hardware.Serial;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class DeviceLogWindow : Window
{
    private const int WindowWidth = 1040;
    private const int WindowHeight = 580;
    private readonly DeviceLogRowCollection rows = [];
    private readonly DispatcherTimer refreshTimer;
    private bool allowPermanentClose;
    private bool activated;
    private bool forceFullRefresh = true;
    private bool followTail = true;
    private bool applyingSnapshot;
    private bool tailScrollRequested;
    private bool applyingTailScroll;
    private long renderedGeneration = -1;
    private ScrollViewer? logScrollViewer;
    private ScrollBar? logVerticalScrollBar;

    internal DeviceLogWindow()
    {
        InitializeComponent();
        LogListView.ItemsSource = rows;
        LogListView.LayoutUpdated += LogListView_LayoutUpdated;
        LogListView.KeyDown += LogListView_KeyDown;
        LogListView.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(LogListView_PointerWheelChanged),
            handledEventsToo: true);
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

        followTail = true;
        forceFullRefresh = true;
        RefreshLog();
        refreshTimer.Start();
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= RefreshTimer_Tick;
        if (logScrollViewer is not null)
        {
            logScrollViewer.ViewChanged -= LogScrollViewer_ViewChanged;
        }

        if (logVerticalScrollBar is not null)
        {
            logVerticalScrollBar.Scroll -= LogVerticalScrollBar_Scroll;
        }

        LogListView.LayoutUpdated -= LogListView_LayoutUpdated;
        LogListView.KeyDown -= LogListView_KeyDown;

        Close();
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollInfrastructure();
        RefreshLog();
    }

    private void EnsureScrollInfrastructure()
    {
        ScrollViewer? scrollViewer = FindDescendant<ScrollViewer>(LogListView);
        if (!ReferenceEquals(logScrollViewer, scrollViewer))
        {
            if (logScrollViewer is not null)
            {
                logScrollViewer.ViewChanged -= LogScrollViewer_ViewChanged;
            }

            logScrollViewer = scrollViewer;
            if (logScrollViewer is not null)
            {
                logScrollViewer.ViewChanged += LogScrollViewer_ViewChanged;
            }
        }

        DependencyObject scrollRoot = logScrollViewer is not null ? logScrollViewer : LogListView;
        ScrollBar? verticalScrollBar = FindVerticalScrollBar(scrollRoot);
        if (!ReferenceEquals(logVerticalScrollBar, verticalScrollBar))
        {
            if (logVerticalScrollBar is not null)
            {
                logVerticalScrollBar.Scroll -= LogVerticalScrollBar_Scroll;
            }

            logVerticalScrollBar = verticalScrollBar;
            if (logVerticalScrollBar is not null)
            {
                logVerticalScrollBar.Scroll += LogVerticalScrollBar_Scroll;
            }
        }
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

        IReadOnlyList<DeviceLogLine> snapshot = DeviceExchangeLog.GetLines();
        applyingSnapshot = true;
        try
        {
            ApplySnapshot(snapshot, forceFullRefresh);
        }
        finally
        {
            applyingSnapshot = false;
        }

        forceFullRefresh = false;
        renderedGeneration = generation;
        SynchronizeControls();

        if (followTail && rows.Count > 0)
        {
            RequestTailScroll();
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

        long firstDesiredSequence = snapshot[0].Sequence;
        while (rows.Count > 0 && rows[0].Sequence < firstDesiredSequence)
        {
            rows.RemoveAt(0);
        }

        if (rows.Count == 0)
        {
            AppendRows(snapshot, 0);
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

        AppendRows(snapshot, snapshotStart + rows.Count);
    }

    private void ReplaceRows(IReadOnlyList<DeviceLogLine> snapshot)
    {
        rows.ReplaceWith(snapshot);
    }

    private void AppendRows(IReadOnlyList<DeviceLogLine> snapshot, int startIndex)
    {
        for (int index = startIndex; index < snapshot.Count; index++)
        {
            rows.Add(new DeviceLogRow(snapshot[index]));
        }
    }

    private void LogScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        bool pinnedToBottom = IsPinnedToBottom();
        if (applyingTailScroll)
        {
            if (!e.IsIntermediate)
            {
                applyingTailScroll = false;
                if (followTail && !pinnedToBottom)
                {
                    RequestTailScroll();
                }
            }

            return;
        }

        if (pinnedToBottom)
        {
            followTail = true;
            tailScrollRequested = false;
            return;
        }

        if (applyingSnapshot)
        {
            RequestTailScroll();
            return;
        }

        if (e.IsIntermediate)
        {
            StopFollowingTail();
            return;
        }

        if (followTail)
        {
            RequestTailScroll();
            TryScrollToTail();
        }
    }

    private void LogListView_LayoutUpdated(object? sender, object e)
    {
        if (logScrollViewer is null || logVerticalScrollBar is null)
        {
            EnsureScrollInfrastructure();
        }

        if (tailScrollRequested)
        {
            TryScrollToTail();
        }
    }

    private void RequestTailScroll()
    {
        if (followTail && rows.Count > 0)
        {
            tailScrollRequested = true;
        }
    }

    private void TryScrollToTail()
    {
        if (!tailScrollRequested || !followTail || rows.Count == 0)
        {
            tailScrollRequested = false;
            return;
        }

        logScrollViewer ??= FindDescendant<ScrollViewer>(LogListView);
        if (logScrollViewer is null)
        {
            return;
        }

        if (IsPinnedToBottom())
        {
            tailScrollRequested = false;
            return;
        }

        double targetOffset = logScrollViewer.ScrollableHeight;
        tailScrollRequested = false;
        applyingTailScroll = true;
        if (!logScrollViewer.ChangeView(
                horizontalOffset: null,
                verticalOffset: targetOffset,
                zoomFactor: null,
                disableAnimation: true))
        {
            applyingTailScroll = false;
            tailScrollRequested = true;
        }
    }

    private void LogListView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int wheelDelta = e.GetCurrentPoint(LogListView).Properties.MouseWheelDelta;
        if (wheelDelta > 0 || !IsPinnedToBottom())
        {
            StopFollowingTail();
        }
    }

    private void LogListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool movesUp = e.Key is VirtualKey.Up or VirtualKey.PageUp or VirtualKey.Home;
        bool movesDown = e.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End;
        if (movesUp || (movesDown && !IsPinnedToBottom()))
        {
            StopFollowingTail();
        }
    }

    private void LogVerticalScrollBar_Scroll(object sender, ScrollEventArgs e)
    {
        bool movesAwayFromTail = e.ScrollEventType is ScrollEventType.SmallDecrement
            or ScrollEventType.LargeDecrement
            or ScrollEventType.First
            or ScrollEventType.ThumbTrack;
        if (movesAwayFromTail
            || (e.ScrollEventType != ScrollEventType.EndScroll && !IsPinnedToBottom()))
        {
            StopFollowingTail();
        }
    }

    private void StopFollowingTail()
    {
        followTail = false;
        tailScrollRequested = false;
        applyingTailScroll = false;
    }

    private bool IsPinnedToBottom()
    {
        logScrollViewer ??= FindDescendant<ScrollViewer>(LogListView);
        return logScrollViewer is null
            || logScrollViewer.ScrollableHeight <= 0
            || logScrollViewer.VerticalOffset >= logScrollViewer.ScrollableHeight - 2;
    }

    private void SynchronizeControls()
    {
        bool enabled = DeviceExchangeLog.IsEnabled;
        bool rawAvailable = DeviceExchangeLog.IsRawDataAvailable;
        LoggingToggleButton.IsChecked = enabled;
        RawDataToggleButton.IsChecked = DeviceExchangeLog.ShowRawData;
        RawDataToggleButton.Visibility = rawAvailable ? Visibility.Visible : Visibility.Collapsed;
        LogSummaryTextBlock.Text =
            $"Строк: {rows.Count} из {DeviceExchangeLog.MaximumLines} · файлов сессий: до {DeviceExchangeLog.MaximumFiles}";
        string activeFilePath = DeviceExchangeLog.FilePath;
        LogPathTextBlock.Text = activeFilePath.Length > 0
            ? activeFilePath
            : $"Папка журналов: {DeviceExchangeLog.DirectoryPath}";
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
        followTail = true;
        forceFullRefresh = true;
        RefreshLog();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceExchangeLog.Clear();
        followTail = true;
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
            Directory.CreateDirectory(DeviceExchangeLog.DirectoryPath);
            Process.Start(new ProcessStartInfo(DeviceExchangeLog.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LogSummaryTextBlock.Text = $"Не удалось открыть папку журналов: {exception.Message}";
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

    private static ScrollBar? FindVerticalScrollBar(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollBar { Orientation: Orientation.Vertical } scrollBar)
            {
                return scrollBar;
            }

            ScrollBar? descendant = FindVerticalScrollBar(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}

internal sealed class DeviceLogRowCollection : ObservableCollection<DeviceLogRow>
{
    public void ReplaceWith(IEnumerable<DeviceLogLine> lines)
    {
        Items.Clear();
        foreach (DeviceLogLine line in lines)
        {
            Items.Add(new DeviceLogRow(line));
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
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
