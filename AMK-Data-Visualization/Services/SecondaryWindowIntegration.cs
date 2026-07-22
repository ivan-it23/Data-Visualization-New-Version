using System.Runtime.InteropServices;

namespace AMK.DataVisualization.App.Services;

internal static class SecondaryWindowIntegration
{
    private const int WindowOwnerIndex = -8;
    private const int WindowExtendedStyleIndex = -20;
    private const int ClassIconIndex = -14;
    private const int ClassSmallIconIndex = -34;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int WindowMessageGetIcon = 0x007F;
    private const int WindowMessageSetIcon = 0x0080;
    private const long ExtendedStyleAppWindow = 0x00040000L;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const int ShowRestore = 9;

    public static void ConfigureForOwner(IntPtr windowHandle, IntPtr ownerHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (ownerHandle != IntPtr.Zero)
        {
            SetWindowLongPtr(windowHandle, WindowOwnerIndex, ownerHandle);
        }

        long extendedStyle = GetWindowLongPtr(windowHandle, WindowExtendedStyleIndex).ToInt64();
        extendedStyle |= ExtendedStyleAppWindow;
        extendedStyle &= ~ExtendedStyleToolWindow;
        SetWindowLongPtr(windowHandle, WindowExtendedStyleIndex, new IntPtr(extendedStyle));

        InheritOwnerIcon(windowHandle, ownerHandle);
    }

    public static void RestoreIfMinimized(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero && IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, ShowRestore);
        }
    }

    private static void InheritOwnerIcon(IntPtr windowHandle, IntPtr ownerHandle)
    {
        if (ownerHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr largeIcon = SendMessage(ownerHandle, WindowMessageGetIcon, new IntPtr(IconBig), IntPtr.Zero);
        if (largeIcon == IntPtr.Zero)
        {
            largeIcon = GetClassLongPtr(ownerHandle, ClassIconIndex);
        }

        IntPtr smallIcon = SendMessage(ownerHandle, WindowMessageGetIcon, new IntPtr(IconSmall), IntPtr.Zero);
        if (smallIcon == IntPtr.Zero)
        {
            smallIcon = GetClassLongPtr(ownerHandle, ClassSmallIconIndex);
        }

        if (largeIcon != IntPtr.Zero)
        {
            SendMessage(windowHandle, WindowMessageSetIcon, new IntPtr(IconBig), largeIcon);
        }

        if (smallIcon != IntPtr.Zero)
        {
            SendMessage(windowHandle, WindowMessageSetIcon, new IntPtr(IconSmall), smallIcon);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
