param(
    [string] $GameRoot,
    [string] $WorkshopRoot,
    [switch] $ValidateReferencesOnly
)

$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$installedGame = Split-Path -Parent $workspace
if (-not $GameRoot) {
    $preservedGame = Join-Path (Split-Path -Parent $installedGame) 'RimWorld-1.6'
    $GameRoot = if (Test-Path -LiteralPath $preservedGame -PathType Container) { $preservedGame } else { $installedGame }
}
$game = (Resolve-Path -LiteralPath $GameRoot).Path
$version = (Get-Content -LiteralPath (Join-Path $game 'Version.txt') -Raw).Trim()
if ($version -notmatch '^1\.6(?:\.|\s|$)') {
    throw "RimWorld 1.6 references required; found '$version' at $game. Use the preserved RimWorld-1.6 folder."
}
if (-not $WorkshopRoot) {
    $frozenWorkshop = Join-Path $game 'Workshop'
    $WorkshopRoot = if (Test-Path -LiteralPath $frozenWorkshop -PathType Container) { $frozenWorkshop } else { Join-Path $game '..\..\workshop\content\294100' }
}
$workshop = (Resolve-Path -LiteralPath $WorkshopRoot).Path

$required = @(
    (Join-Path $game 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'),
    (Join-Path $workshop '2009463077\1.6\Assemblies\0Harmony.dll'),
    (Join-Path $workshop '839005762\1.6\Assemblies\AlienRace.dll')
)
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Missing build reference: $file"
    }
}

if ($ValidateReferencesOnly) {
    [pscustomobject]@{
        GameRoot = $game
        WorkshopRoot = $workshop
        VersionFile = $version
        References = @($required | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 })
    }
    return
}

$localDotnet = Join-Path $workspace '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projects = @(
    'Source\RimRound\RimRound.csproj',
    '1.6\ExternalMods\SwellGlow\Source\SwellGlow\SwellGlow.csproj',
    'Source\RimRoundFeedOther\RimRoundFeedOther.csproj',
    'Addons\JunkFood\Source\JunkFood.csproj'
)
$rimRoundReferenceRoot = Join-Path $workspace '1.6'
foreach ($relativeProject in $projects) {
    $project = Join-Path $workspace $relativeProject
    $extraArguments = @()
    if ($relativeProject -eq 'Addons\JunkFood\Source\JunkFood.csproj') {
        $junkFoodOutput = Join-Path $workspace '1.6\ExternalMods\JunkFood\1.6\Assemblies'
        $extraArguments += "/p:OutputPath=$junkFoodOutput/"
    }
    & $dotnet build $project --configuration Release --nologo --verbosity quiet @extraArguments "/p:RimWorldGameRoot=$game" "/p:RimWorldWorkshopRoot=$workshop" "/p:RimRoundReferenceRoot=$rimRoundReferenceRoot"
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $relativeProject"
    }
}

$outputs = @(
    '1.6\Assemblies\RimRound.dll',
    '1.6\Assemblies\RimRoundFeedOther.dll',
    '1.6\ExternalMods\SwellGlow\Assemblies\SwellGlow.dll',
    '1.6\ExternalMods\JunkFood\1.6\Assemblies\Oinky.JunkFood.dll'
)
foreach ($relativeOutput in $outputs) {
    $output = Join-Path $workspace $relativeOutput
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Build completed without expected output: $output"
    }
    $item = Get-Item -LiteralPath $output
    [pscustomobject]@{
        Assembly = $relativeOutput
        Bytes = $item.Length
        SHA256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    }
}
