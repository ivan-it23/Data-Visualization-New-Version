param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'AMK-Data-Visualization\AMK-Data-Visualization.csproj'
$buildDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\build\win-x64'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\publish\win-x64'))
$setupScript = Join-Path $repositoryRoot 'installer\setup.iss'
$compilerCandidates = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compilerPath) {
    throw 'Inno Setup 6 was not found. Install Inno Setup and retry.'
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'The Version property is missing in AMK-Data-Visualization.csproj.'
}

if (-not $SkipPublish) {
    $rootWithSeparator = $repositoryRoot.TrimEnd('\') + '\'
    foreach ($directory in @($buildDirectory, $publishDirectory)) {
        if (-not $directory.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Build directory is outside the repository: $directory"
        }

        if (Test-Path -LiteralPath $directory) {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }

        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    # Keep packaging output separate from Visual Studio's bin directory. This
    # allows creating an installer while a developer build is still running.
    & dotnet publish $projectPath `
        -c Release `
        -p:Platform=x64 `
        "-p:OutDir=$buildDirectory\" `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    # The WinUI compiler places the application PRI and compiled XAML (XBF)
    # in OutDir, but `dotnet publish -o` does not copy them to PublishDir.
    # Without these files the installed executable crashes in
    # MainWindow.InitializeComponent with XamlParseException.
    $compiledXamlFiles = @(Get-ChildItem -LiteralPath $buildDirectory -Recurse -File -Filter '*.xbf')
    if ($compiledXamlFiles.Count -eq 0) {
        throw "No compiled WinUI XAML files were found in: $buildDirectory"
    }

    foreach ($sourceFile in $compiledXamlFiles) {
        $relativePath = $sourceFile.FullName.Substring($buildDirectory.Length).TrimStart('\')
        $destinationPath = Join-Path $publishDirectory $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        if (-not (Test-Path -LiteralPath $destinationDirectory)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath -Force
    }

    $applicationPriSource = Join-Path $buildDirectory 'DataVisualization.pri'
    if (-not (Test-Path -LiteralPath $applicationPriSource)) {
        throw "The compiled WinUI resource index was not found: $applicationPriSource"
    }

    Copy-Item `
        -LiteralPath $applicationPriSource `
        -Destination (Join-Path $publishDirectory 'DataVisualization.pri') `
        -Force
}

$requiredPublishedFiles = @(
    'DataVisualization.exe'
    'DataVisualization.dll'
    'DataVisualization.runtimeconfig.json'
    'DataVisualization.pri'
    'App.xbf'
    'MainWindow.xbf'
    'Controls\LineChartControl.xbf'
    'Views\ConnectionSettingsWindow.xbf'
    'Views\FileConstructorWindow.xbf'
    'Views\MetrologyWindow.xbf'
    'Views\SchematicWindow.xbf'
    'Microsoft.ui.xaml.dll'
    'Microsoft.WindowsAppRuntime.Bootstrap.dll'
)

foreach ($relativePath in $requiredPublishedFiles) {
    $publishedPath = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
        throw "Required installer payload file was not found: $publishedPath"
    }
}

& $compilerPath "/DMyAppVersion=$version" $setupScript
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $repositoryRoot "installer\Output\DataVisualization-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not created: $installerPath"
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
$feedPath = Join-Path (Split-Path -Parent $installerPath) 'data-visualization-version.txt'
$legacyFeedPath = Join-Path (Split-Path -Parent $installerPath) 'version.txt'
if (Test-Path -LiteralPath $legacyFeedPath) {
    Remove-Item -LiteralPath $legacyFeedPath -Force
}

$feedContents = @(
    $version
    [System.IO.Path]::GetFileName($installerPath)
    $hash
) -join [Environment]::NewLine
[System.IO.File]::WriteAllText(
    $feedPath,
    $feedContents + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Installer: $installerPath"
Write-Output "Feed file: $feedPath"
Write-Output "Version:   $version"
Write-Output "SHA-256:  $hash"
