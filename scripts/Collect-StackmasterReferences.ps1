<#
.SYNOPSIS
Creates a private ZIP of the exact Valheim and BepInEx assemblies needed to compile Stackmaster.

.DESCRIPTION
This script reads the installed game and the existing VanillaPlus r2modman profile, then writes
one ZIP in Downloads. It does not change Valheim, Steam, r2modman, or any profile, and it does not
send the ZIP anywhere. The ZIP is for private build references only and must never be committed or
included in Stackmaster's public package.
#>

#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ValheimManagedPath,

    [Parameter()]
    [string]$BepInExCorePath,

    [Parameter()]
    [string]$OutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

trap {
    $failedLine = $_.InvocationInfo.ScriptLineNumber
    Write-Host "Stackmaster reference collection failed at line ${failedLine}: $($_.Exception.Message)" -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($_.InvocationInfo.Line)) {
        Write-Host "Command: $($_.InvocationInfo.Line.Trim())" -ForegroundColor Red
    }
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ValheimManagedPath)) {
    $ValheimManagedPath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed'
}
if ([string]::IsNullOrWhiteSpace($BepInExCorePath)) {
    $BepInExCorePath = Join-Path -Path $env:APPDATA -ChildPath 'r2modmanPlus-local\Valheim\profiles\VanillaPlus\BepInEx\core'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $downloads = Join-Path -Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) -ChildPath 'Downloads'
    $OutputPath = Join-Path -Path $downloads -ChildPath 'stackmaster-reference-assemblies.zip'
}

if (-not (Test-Path -LiteralPath $ValheimManagedPath -PathType Container)) {
    throw "Valheim Managed folder not found: $ValheimManagedPath"
}
if (-not (Test-Path -LiteralPath $BepInExCorePath -PathType Container)) {
    throw "BepInEx core folder not found: $BepInExCorePath"
}

$gameAssemblyNames = @(
    'assembly_valheim.dll',
    'assembly_utils.dll',
    'assembly_guiutils.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.InputLegacyModule.dll',
    'Unity.TextMeshPro.dll'
)

$files = @()
foreach ($name in $gameAssemblyNames) {
    $candidate = Join-Path -Path $ValheimManagedPath -ChildPath $name
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $files += $candidate
    }
}

$requiredGameAssembly = Join-Path -Path $ValheimManagedPath -ChildPath 'assembly_valheim.dll'
if (-not (Test-Path -LiteralPath $requiredGameAssembly -PathType Leaf)) {
    throw 'assembly_valheim.dll was not found.'
}

$bepInExAssemblies = @(Get-ChildItem -LiteralPath $BepInExCorePath -Filter '*.dll' -File -ErrorAction Stop)
foreach ($assembly in $bepInExAssemblies) {
    $files += $assembly.FullName
}

if ($files.Count -eq 0) {
    throw 'No reference assemblies were found.'
}

$outputDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw "Output directory does not exist: $outputDirectory"
}

Compress-Archive -LiteralPath $files -DestinationPath $OutputPath -CompressionLevel Optimal -Force
Write-Host "Stackmaster reference ZIP written to: $OutputPath" -ForegroundColor Green
Write-Host 'Upload that ZIP in this chat. It contains compile-time libraries only; no saves, characters, worlds, logs, or configuration files.'
