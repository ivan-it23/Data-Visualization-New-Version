param(
    [Parameter(Mandatory = $true)]
    [string]$DocumentPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int]$FirstPage = 2,
    [int]$LastPage = 14
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$word = $null
$doc = $null
try {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Open($DocumentPath, $false, $true, $false)
    $doc.Activate()

    for ($page = $FirstPage; $page -le $LastPage; $page++) {
        $start = $doc.GoTo(1, 1, $page).Start
        $next = $doc.GoTo(1, 1, $page + 1).Start
        if ($next -le $start) {
            $next = $doc.Content.End
        }
        $range = $doc.Range($start, [Math]::Max($start, $next - 1))
        $range.CopyAsPicture()
        Start-Sleep -Milliseconds 250
        $image = [System.Windows.Forms.Clipboard]::GetImage()
        if ($null -eq $image) {
            $dataObject = [System.Windows.Forms.Clipboard]::GetDataObject()
            $formats = $dataObject.GetFormats() -join ', '
            $emf = $dataObject.GetData('EnhancedMetafile')
            $type = if ($null -eq $emf) { 'null' } else { $emf.GetType().FullName }
            throw "Word did not place page $page as a bitmap on the clipboard. EMF type: $type. Formats: $formats"
        }
        $path = Join-Path $OutputDirectory ("page-{0:D2}.png" -f $page)
        $image.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $image.Dispose()
        Write-Output $path
    }
}
finally {
    if ($null -ne $doc) { $doc.Close($false) }
    if ($null -ne $word) { $word.Quit() }
    if ($null -ne $doc) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($doc) }
    if ($null -ne $word) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) }
}
