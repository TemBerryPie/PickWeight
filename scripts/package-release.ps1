param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [string]$CertificateThumbprint,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repositoryRoot "dist"
$appOutput = Join-Path $distRoot "PickWeight-win-x64"
$appDataOutput = Join-Path $appOutput "Data"
$pluginOutput = Join-Path $distRoot "ClassIsland"
$appArchive = Join-Path $distRoot "PickWeight-win-x64.zip"
$pluginPackage = Join-Path $repositoryRoot "PickWeight ClassIsland\cipx\PickWeight.ClassIsland.Plugin.cipx"
$pwshShim = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\pwsh.exe"
$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
$powershellBinary = if (Test-Path -LiteralPath $pwshShim) {
    $pwshShim
} elseif ($pwshCommand) {
    $pwshCommand.Source
} else {
    Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
}
if ([string]::IsNullOrWhiteSpace($powershellBinary)) {
    $powershellBinary = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
}
$env:AVALONIA_TELEMETRY_OPTOUT = "1"

foreach ($path in @($appOutput, $pluginOutput)) {
    $resolvedParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $path))
    if ($resolvedParent -ne [System.IO.Path]::GetFullPath($distRoot)) {
        throw "Refusing to clean output outside dist: $path"
    }
}

if (Test-Path $appOutput) {
    Remove-Item -LiteralPath $appOutput -Recurse -Force
}
if (Test-Path $pluginOutput) {
    Remove-Item -LiteralPath $pluginOutput -Recurse -Force
}
if (Test-Path $appArchive) {
    Remove-Item -LiteralPath $appArchive -Force
}

$publishArgs = @(
    "publish",
    (Join-Path $repositoryRoot "src\PickWeight.App\PickWeight.App.csproj"),
    "-c",
    $Configuration,
    "-p:Platform=x64",
    "-p:PublishProfile=win-x64"
)
if ($NoRestore) {
    $publishArgs += "--no-restore"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "PickWeight publish failed."
}

Get-ChildItem -LiteralPath $appDataOutput -Recurse -File -Filter "*.pdb" |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $repositoryRoot "implicit_config.sample.json") `
    -Destination (Join-Path $appDataOutput "implicit_config.json")
$keptCultureDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($culture in @("zh-CN", "zh-Hans")) {
    [void]$keptCultureDirectories.Add($culture)
}

Get-ChildItem -LiteralPath $appDataOutput -Directory |
    Where-Object {
        $_.Name -match '^[a-z]{2,3}(-[A-Za-z0-9]+){0,2}$' -and
        -not $keptCultureDirectories.Contains($_.Name)
    } |
    Remove-Item -Recurse -Force

$launcherPublishArgs = @(
    "publish",
    (Join-Path $repositoryRoot "src\PickWeight.Launcher\PickWeight.Launcher.csproj"),
    "-c",
    $Configuration,
    "-p:Platform=x64",
    "-p:PublishProfile=win-x64"
)
if ($NoRestore) {
    $launcherPublishArgs += "--no-restore"
}

dotnet @launcherPublishArgs
if ($LASTEXITCODE -ne 0) {
    throw "PickWeight launcher publish failed."
}

Get-ChildItem -LiteralPath $appOutput -File -Filter "*.pdb" |
    Remove-Item -Force

$pluginBuildArgs = @(
    "build",
    (Join-Path $repositoryRoot "PickWeight ClassIsland\PickWeight.ClassIsland.Plugin.csproj"),
    "-c",
    $Configuration,
    "-p:PowershellBinaryName=$powershellBinary"
)
if ($NoRestore) {
    $pluginBuildArgs += "--no-restore"
}

dotnet @pluginBuildArgs
if ($LASTEXITCODE -ne 0) {
    throw "ClassIsland plugin build failed."
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -eq $signTool) {
        throw "CertificateThumbprint was supplied but signtool.exe was not found."
    }

    $executables = Get-ChildItem -LiteralPath $appOutput -Recurse -File -Filter "*.exe"
    foreach ($executable in $executables) {
        & $signTool.Source sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $executable.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Signing failed: $($executable.FullName)"
        }
    }
    Write-Host "Executable files were Authenticode signed."
} else {
    Write-Warning "No certificate thumbprint supplied; the package is unsigned and must not be released as a signed build."
}

New-Item -ItemType Directory -Path $pluginOutput | Out-Null
Copy-Item -LiteralPath $pluginPackage -Destination $pluginOutput
Copy-Item -LiteralPath (Join-Path $repositoryRoot "PickWeight ClassIsland\README.md") `
    -Destination (Join-Path $pluginOutput "README.md")

Compress-Archive -LiteralPath $appOutput -DestinationPath $appArchive -CompressionLevel Optimal

Write-Host "Release package ready:"
Write-Host "  Main app: $appOutput"
Write-Host "  Main app archive: $appArchive"
Write-Host "  ClassIsland plugin: $pluginOutput"
