param(
    [Parameter(Mandatory = $true)] [string]$DocumentPath,
    [Parameter(Mandatory = $true)] [string]$PdfPath,
    [Parameter(Mandatory = $true)] [string]$StatusPath
)

$word = $null
$doc = $null
try {
    "STARTED $(Get-Date -Format o)" | Out-File -LiteralPath $StatusPath -Encoding utf8
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Open($DocumentPath, $false, $true, $false)
    "OPENED $(Get-Date -Format o)" | Out-File -LiteralPath $StatusPath -Encoding utf8 -Append
    $doc.ExportAsFixedFormat($PdfPath, 17)
    "EXPORTED $(Get-Date -Format o)" | Out-File -LiteralPath $StatusPath -Encoding utf8 -Append
}
catch {
    "ERROR $($_.Exception.Message)" | Out-File -LiteralPath $StatusPath -Encoding utf8 -Append
    exit 1
}
finally {
    if ($null -ne $doc) { $doc.Close($false) }
    if ($null -ne $word) { $word.Quit() }
    if ($null -ne $doc) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($doc) }
    if ($null -ne $word) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) }
}
