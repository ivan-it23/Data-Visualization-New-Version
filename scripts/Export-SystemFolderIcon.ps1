param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\AMK-Data-Visualization\Assets')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ShellStockIcon
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetStockIconInfo(
        uint stockIconId,
        uint flags,
        ref SHSTOCKICONINFO stockIconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr iconHandle);
}
'@

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$folderStockIconId = 3
$stockIconFlag = 0x00000100
$smallIconFlag = 0x00000001

$stockIconInfo = [ShellStockIcon+SHSTOCKICONINFO]::new()
$stockIconInfo.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($stockIconInfo)
$result = [ShellStockIcon]::SHGetStockIconInfo(
    $folderStockIconId,
    $stockIconFlag -bor $smallIconFlag,
    [ref]$stockIconInfo)

if ($result -lt 0 -or $stockIconInfo.hIcon -eq [IntPtr]::Zero) {
    throw ('SHGetStockIconInfo failed with HRESULT 0x{0:X8}.' -f $result)
}

try {
    $icon = [System.Drawing.Icon]::FromHandle($stockIconInfo.hIcon)
    $bitmap = $icon.ToBitmap()
    try {
        $outputPath = Join-Path $resolvedOutputDirectory 'SystemFolder.png'
        $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}
finally {
    [ShellStockIcon]::DestroyIcon($stockIconInfo.hIcon) | Out-Null
}

Write-Output $outputPath
