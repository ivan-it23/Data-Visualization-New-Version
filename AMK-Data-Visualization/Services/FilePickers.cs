using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Единая точка вызова системных диалогов выбора файла и папки.
/// </summary>
/// <remarks>
/// Делает две вещи, которые раньше приходилось повторять на каждом вызове:
/// привязывает диалог к окну-владельцу и не даёт отказу пройти незамеченным.
///
/// Отказ здесь — не теоретическая возможность. <c>Windows.Storage.Pickers</c>
/// не работают в процессе с правами администратора: вызов завершается
/// <c>E_FAIL</c>. Обработчики меню объявлены как <c>async void</c>, поэтому
/// исключение уходило мимо кода окна, и для пользователя это выглядело так,
/// будто пункт меню не нажимается вовсе.
///
/// Штатно приложение с повышенными правами не работает — установщик запускает
/// его через оболочку именно поэтому, — но пользователь может запустить его так
/// вручную, и тогда он должен получить объяснение, а не молчание.
/// </remarks>
internal static class FilePickers
{
    /// <summary>
    /// Процесс работает с повышенными правами. Вычисляется один раз: в течение
    /// жизни процесса токен не меняется.
    /// </summary>
    public static bool IsElevatedProcess { get; } = DetectElevation();

    public static async Task<StorageFile?> PickSingleFileAsync(FileOpenPicker picker, Window owner)
    {
        Bind(picker, owner);
        try
        {
            return await picker.PickSingleFileAsync();
        }
        catch (Exception exception) when (IsPickerFailure(exception))
        {
            await ReportAsync(owner, "выбора файла", exception);
            return null;
        }
    }

    public static async Task<IReadOnlyList<StorageFile>> PickMultipleFilesAsync(FileOpenPicker picker, Window owner)
    {
        Bind(picker, owner);
        try
        {
            return await picker.PickMultipleFilesAsync();
        }
        catch (Exception exception) when (IsPickerFailure(exception))
        {
            await ReportAsync(owner, "выбора файлов", exception);
            return [];
        }
    }

    public static async Task<StorageFile?> PickSaveFileAsync(FileSavePicker picker, Window owner)
    {
        Bind(picker, owner);
        try
        {
            return await picker.PickSaveFileAsync();
        }
        catch (Exception exception) when (IsPickerFailure(exception))
        {
            await ReportAsync(owner, "сохранения файла", exception);
            return null;
        }
    }

    public static async Task<StorageFolder?> PickSingleFolderAsync(FolderPicker picker, Window owner)
    {
        Bind(picker, owner);
        try
        {
            return await picker.PickSingleFolderAsync();
        }
        catch (Exception exception) when (IsPickerFailure(exception))
        {
            await ReportAsync(owner, "выбора папки", exception);
            return null;
        }
    }

    /// <summary>
    /// Диалог принадлежит окну: без этой привязки он не открывается вовсе —
    /// приложение работает без пакетной идентичности.
    /// </summary>
    private static void Bind(object picker, Window owner)
    {
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
    }

    private static bool IsPickerFailure(Exception exception)
    {
        return exception is COMException or UnauthorizedAccessException or InvalidOperationException;
    }

    private static async Task ReportAsync(Window owner, string action, Exception exception)
    {
        if (owner.Content is not FrameworkElement root || root.XamlRoot is null)
        {
            return;
        }

        string message = IsElevatedProcess
            ? "Приложение запущено с правами администратора, а в таком режиме Windows "
              + $"не открывает системный диалог {action}.\n\n"
              + "Закройте приложение и запустите его обычным способом — с ярлыка на рабочем "
              + "столе или из меню «Пуск»."
            : $"Windows не открыл системный диалог {action}.\n\n{exception.Message}";

        ContentDialog dialog = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Диалог выбора недоступен",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "ОК"
        };

        await dialog.ShowScaledAsync();
    }

    private static bool DetectElevation()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception) when (exception is SystemException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
