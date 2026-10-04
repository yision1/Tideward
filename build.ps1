param(
    [ValidateSet('x64', 'arm64')][string] $Architecture = "x64",
    [string] $Version = "0.18.2",
    [string] $Output = "build/Tideward"
)

$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'A stable release version is required.' }
$Output = [IO.Path]::GetFullPath($Output, $workspace)
$tools = [IO.Path]::GetFullPath($workspace + '-tools')
$dotnet = Join-Path $tools 'dotnet/dotnet.exe'
if (Test-Path -LiteralPath $dotnet) {
    $env:DOTNET_ROOT = Join-Path $tools 'dotnet'
    $env:DOTNET_CLI_HOME = Join-Path $tools 'cli-home'
    $env:NUGET_PACKAGES = Join-Path $tools 'nuget'
} else { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Existing MSVC build tools are required; this script does not install them.' }

Push-Location $workspace
try {
    $restoreSource = if (Test-Path -LiteralPath (Join-Path $tools 'nuget')) { @('--source', (Join-Path $tools 'nuget')) } else { @() }
    & $dotnet publish src/Tideward/Tideward.csproj -c Release -r "win-$Architecture" --self-contained true -o "$Output/app-$Version" -p:Platform=$Architecture -p:Version=$Version -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:CsWinRTAotOptimizerEnabled=false -p:NuGetAudit=false @restoreSource
    if ($LASTEXITCODE -ne 0) { throw 'Tideward Release publish failed.' }

    # MSBuild's .NET Framework tasks require a single, consistently cased Path entry.
    $buildPath = $env:PATH
    Remove-Item Env:PATH
    $env:Path = $buildPath
    & $msbuild src/Tideward.Launcher/Tideward.Launcher.vcxproj -p:Configuration=Release "-p:Platform=$Architecture" "-p:OutDir=$Output/"
    if ($LASTEXITCODE -ne 0) { throw 'Tideward native launcher build failed.' }

    Set-Content -LiteralPath (Join-Path $Output 'version.ini') -Value "version=$Version" -Encoding utf8NoBOM
    $launcherSymbols = Join-Path $Output 'Tideward.pdb'
    if (Test-Path -LiteralPath $launcherSymbols) { Remove-Item -LiteralPath $launcherSymbols -Force }
} finally { Pop-Location }
