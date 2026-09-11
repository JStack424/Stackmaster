<#
.SYNOPSIS
Creates a sanitized, offline report of a Windows Valheim/r2modman development environment.

.DESCRIPTION
This script performs read-only discovery of likely Steam Valheim installations, Steam app
metadata, Valheim managed assemblies, r2modman Valheim profiles, and BepInEx/Harmony DLL
versions. It never installs software, modifies Steam/r2modman/Valheim, copies game files,
reads configuration values or logs, or sends data over the network. Its only write is the
requested report file.

By default the report is written beside this script as stackmaster-environment-report.json.
User-profile prefixes are replaced with %USERPROFILE% in reported paths.

.PARAMETER OutputPath
Where to write the report. The parent directory must already exist.

.PARAMETER Format
Json (default) or Text.

.PARAMETER ValheimPath
Optional explicit Valheim install folder if Steam auto-discovery does not find it.

.PARAMETER R2ModManDataPath
Optional r2modman Valheim folder or profiles folder if auto-discovery does not find it.

.PARAMETER ProfileName
Optional exact r2modman profile name to include.

.EXAMPLE
.\scripts\Inspect-StackmasterEnvironment.ps1

.EXAMPLE
.\scripts\Inspect-StackmasterEnvironment.ps1 -ValheimPath 'D:\SteamLibrary\steamapps\common\Valheim'

.EXAMPLE
.\scripts\Inspect-StackmasterEnvironment.ps1 -R2ModManDataPath "$env:APPDATA\r2modmanPlus-local\Valheim" -ProfileName 'Stackmaster Dev' -OutputPath "$env:USERPROFILE\Desktop\stackmaster-environment.json"
#>

#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputPath = (Join-Path $PSScriptRoot 'stackmaster-environment-report.json'),

    [Parameter()]
    [ValidateSet('Json', 'Text')]
    [string]$Format = 'Json',

    [Parameter()]
    [string]$ValheimPath,

    [Parameter()]
    [string]$R2ModManDataPath,

    [Parameter()]
    [string]$ProfileName
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function ConvertTo-SanitizedPath {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    try {
        $fullPath = [IO.Path]::GetFullPath($expanded)
    }
    catch {
        $fullPath = $expanded
    }

    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
        $trimmedProfile = $userProfile.TrimEnd([char[]]@('\', '/'))
        if ($fullPath.Equals($trimmedProfile, [StringComparison]::OrdinalIgnoreCase)) {
            return '%USERPROFILE%'
        }
        if ($fullPath.StartsWith($trimmedProfile + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith($trimmedProfile + '/', [StringComparison]::OrdinalIgnoreCase)) {
            return '%USERPROFILE%' + $fullPath.Substring($trimmedProfile.Length)
        }
    }

    return $fullPath
}

function Add-UniqueExistingDirectory {
    param(
        [System.Collections.Generic.List[string]]$List,
        [AllowNull()][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not (Test-Path -LiteralPath $expanded -PathType Container)) {
        return
    }

    $resolved = (Resolve-Path -LiteralPath $expanded).Path
    foreach ($existing in $List) {
        if ($existing.Equals($resolved, [StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }
    $List.Add($resolved)
}

function Get-FileRecord {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    $assemblyVersion = $null
    try {
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($item.FullName).Version.ToString()
    }
    catch {
        # Native executables and non-.NET files legitimately have no assembly version.
    }

    return [ordered]@{
        name            = $item.Name
        path            = ConvertTo-SanitizedPath $item.FullName
        fileVersion     = $item.VersionInfo.FileVersion
        productVersion  = $item.VersionInfo.ProductVersion
        assemblyVersion = $assemblyVersion
        sizeBytes       = $item.Length
    }
}

function Get-VdfValue {
    param(
        [string]$Content,
        [string]$Key
    )

    $pattern = '"' + [Regex]::Escape($Key) + '"\s+"([^"]*)"'
    $match = [Regex]::Match($Content, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        return $match.Groups[1].Value
    }
    return $null
}

function Get-SteamRoots {
    $roots = New-Object 'System.Collections.Generic.List[string]'

    $registryCandidates = @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
    )

    foreach ($candidate in $registryCandidates) {
        try {
            $value = (Get-ItemProperty -LiteralPath $candidate.Path -Name $candidate.Name -ErrorAction Stop).($candidate.Name)
            Add-UniqueExistingDirectory -List $roots -Path $value
        }
        catch {
            # Missing registry locations are normal.
        }
    }

    if (${env:ProgramFiles(x86)}) {
        Add-UniqueExistingDirectory -List $roots -Path (Join-Path ${env:ProgramFiles(x86)} 'Steam')
    }
    if ($env:ProgramFiles) {
        Add-UniqueExistingDirectory -List $roots -Path (Join-Path $env:ProgramFiles 'Steam')
    }

    $initialRoots = @($roots)
    foreach ($steamRoot in $initialRoots) {
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) {
            continue
        }

        try {
            $libraryText = Get-Content -LiteralPath $libraryFile -Raw
            foreach ($match in [Regex]::Matches($libraryText, '"path"\s+"([^"]+)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                $libraryPath = $match.Groups[1].Value -replace '\\\\', '\'
                Add-UniqueExistingDirectory -List $roots -Path $libraryPath
            }
        }
        catch {
            # A malformed or inaccessible library file is reported indirectly by absence.
        }
    }

    return @($roots)
}

function Get-ValheimInstallRecords {
    param([AllowNull()][string]$ExplicitPath)

    $installCandidates = New-Object 'System.Collections.Generic.List[object]'
    $steamRoots = @(Get-SteamRoots)

    foreach ($root in $steamRoots) {
        $manifestPath = Join-Path $root 'steamapps\appmanifest_892970.acf'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            continue
        }

        $manifestText = Get-Content -LiteralPath $manifestPath -Raw
        $installDirName = Get-VdfValue -Content $manifestText -Key 'installdir'
        if ([string]::IsNullOrWhiteSpace($installDirName)) {
            $installDirName = 'Valheim'
        }

        $installCandidates.Add([ordered]@{
            source       = 'Steam manifest'
            installPath  = Join-Path $root (Join-Path 'steamapps\common' $installDirName)
            manifestPath = $manifestPath
            buildId      = Get-VdfValue -Content $manifestText -Key 'buildid'
            lastUpdated  = Get-VdfValue -Content $manifestText -Key 'LastUpdated'
            stateFlags   = Get-VdfValue -Content $manifestText -Key 'StateFlags'
        })
    }

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $expanded = [Environment]::ExpandEnvironmentVariables($ExplicitPath)
        $installCandidates.Add([ordered]@{
            source       = 'Explicit parameter'
            installPath  = $expanded
            manifestPath = $null
            buildId      = $null
            lastUpdated  = $null
            stateFlags   = $null
        })
    }

    $results = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.List[string]'

    foreach ($candidate in $installCandidates) {
        if (-not (Test-Path -LiteralPath $candidate.installPath -PathType Container)) {
            continue
        }

        $resolvedInstall = (Resolve-Path -LiteralPath $candidate.installPath).Path
        $duplicate = $false
        foreach ($existing in $seen) {
            if ($existing.Equals($resolvedInstall, [StringComparison]::OrdinalIgnoreCase)) {
                $duplicate = $true
                break
            }
        }
        if ($duplicate) {
            continue
        }
        $seen.Add($resolvedInstall)

        $managedPath = Join-Path $resolvedInstall 'valheim_Data\Managed'
        $managedDlls = @()
        $managedAssemblyCount = 0
        if (Test-Path -LiteralPath $managedPath -PathType Container) {
            $allManagedDlls = @(Get-ChildItem -LiteralPath $managedPath -Filter '*.dll' -File -ErrorAction SilentlyContinue)
            $managedAssemblyCount = $allManagedDlls.Count
            $relevantPattern = '^(assembly_valheim|assembly_utils|assembly_guiutils|UnityEngine\.CoreModule|UnityEngine\.UI|UnityEngine\.InputLegacyModule|Unity\.TextMeshPro|netstandard|mscorlib)\.dll$'
            foreach ($dll in ($allManagedDlls | Where-Object { $_.Name -match $relevantPattern } | Sort-Object Name)) {
                $managedDlls += Get-FileRecord $dll.FullName
            }
        }

        $results.Add([ordered]@{
            source                 = $candidate.source
            installPath            = ConvertTo-SanitizedPath $resolvedInstall
            steamManifestPath      = ConvertTo-SanitizedPath $candidate.manifestPath
            steamBuildId           = $candidate.buildId
            steamLastUpdatedUnix   = $candidate.lastUpdated
            steamStateFlags        = $candidate.stateFlags
            executable             = Get-FileRecord (Join-Path $resolvedInstall 'valheim.exe')
            managedDirectory       = ConvertTo-SanitizedPath $managedPath
            managedAssemblyCount   = $managedAssemblyCount
            relevantManagedDlls    = @($managedDlls)
        })
    }

    return @($results)
}

function Get-R2ProfileRoots {
    param([AllowNull()][string]$ExplicitPath)

    $roots = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $expanded = [Environment]::ExpandEnvironmentVariables($ExplicitPath)
        if (Test-Path -LiteralPath (Join-Path $expanded 'profiles') -PathType Container) {
            Add-UniqueExistingDirectory -List $roots -Path (Join-Path $expanded 'profiles')
        }
        else {
            Add-UniqueExistingDirectory -List $roots -Path $expanded
        }
    }

    if ($env:APPDATA) {
        Add-UniqueExistingDirectory -List $roots -Path (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles')
    }

    return @($roots)
}

function Get-R2ProfileRecords {
    param(
        [AllowNull()][string]$ExplicitDataPath,
        [AllowNull()][string]$ExactProfileName
    )

    $profileRoots = @(Get-R2ProfileRoots -ExplicitPath $ExplicitDataPath)
    $results = New-Object 'System.Collections.Generic.List[object]'

    foreach ($root in $profileRoots) {
        $directories = @()
        if (Test-Path -LiteralPath (Join-Path $root 'BepInEx') -PathType Container) {
            $directories = @(Get-Item -LiteralPath $root)
        }
        else {
            $directories = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)
        }

        foreach ($profile in $directories) {
            if (-not [string]::IsNullOrWhiteSpace($ExactProfileName) -and
                -not $profile.Name.Equals($ExactProfileName, [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $bepInExRoot = Join-Path $profile.FullName 'BepInEx'
            $coreRoot = Join-Path $bepInExRoot 'core'
            $pluginRoot = Join-Path $bepInExRoot 'plugins'
            $pluginDllCount = 0
            if (Test-Path -LiteralPath $pluginRoot -PathType Container) {
                $pluginDllCount = @(Get-ChildItem -LiteralPath $pluginRoot -Filter '*.dll' -File -Recurse -ErrorAction SilentlyContinue).Count
            }

            $results.Add([ordered]@{
                name               = $profile.Name
                path               = ConvertTo-SanitizedPath $profile.FullName
                bepInExDetected    = Test-Path -LiteralPath $bepInExRoot -PathType Container
                bepInExCore        = Get-FileRecord (Join-Path $coreRoot 'BepInEx.dll')
                harmony            = Get-FileRecord (Join-Path $coreRoot '0Harmony.dll')
                coreDirectory      = ConvertTo-SanitizedPath $coreRoot
                pluginsDirectory   = ConvertTo-SanitizedPath $pluginRoot
                pluginDllCount     = $pluginDllCount
                logOutputPath      = if (Test-Path -LiteralPath (Join-Path $bepInExRoot 'LogOutput.log') -PathType Leaf) { ConvertTo-SanitizedPath (Join-Path $bepInExRoot 'LogOutput.log') } else { $null }
                environmentProps   = [ordered]@{
                    BepInExCore  = ConvertTo-SanitizedPath $coreRoot
                    ModDeployPath = ConvertTo-SanitizedPath $pluginRoot
                }
            })
        }
    }

    return [ordered]@{
        roots    = @($profileRoots | ForEach-Object { ConvertTo-SanitizedPath $_ })
        profiles = @($results)
    }
}

function Convert-ReportToText {
    param($Report)

    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('Stackmaster environment report')
    $lines.Add("Generated UTC: $($Report.generatedUtc)")
    $lines.Add("PowerShell: $($Report.host.powerShellVersion) ($($Report.host.edition))")
    $lines.Add("OS: $($Report.host.osVersion)")
    $lines.Add('')
    $lines.Add("Valheim installations: $($Report.valheimInstallations.Count)")
    foreach ($install in $Report.valheimInstallations) {
        $lines.Add("- Path: $($install.installPath)")
        $lines.Add("  Steam build: $($install.steamBuildId)")
        if ($install.executable) {
            $lines.Add("  Executable version: $($install.executable.productVersion)")
        }
        $lines.Add("  Managed DLL count: $($install.managedAssemblyCount)")
        foreach ($dll in $install.relevantManagedDlls) {
            $lines.Add("  DLL: $($dll.name) | assembly $($dll.assemblyVersion) | $($dll.path)")
        }
    }
    $lines.Add('')
    $lines.Add("r2modman profile roots: $($Report.r2modman.roots.Count)")
    foreach ($root in $Report.r2modman.roots) {
        $lines.Add("- $root")
    }
    $lines.Add("r2modman profiles: $($Report.r2modman.profiles.Count)")
    foreach ($profile in $Report.r2modman.profiles) {
        $lines.Add("- $($profile.name): $($profile.path)")
        if ($profile.bepInExCore) {
            $lines.Add("  BepInEx: $($profile.bepInExCore.assemblyVersion)")
        }
        if ($profile.harmony) {
            $lines.Add("  Harmony: $($profile.harmony.assemblyVersion)")
        }
        $lines.Add("  Plugin DLL count: $($profile.pluginDllCount)")
    }
    $lines.Add('')
    foreach ($note in $Report.notes) {
        $lines.Add("Note: $note")
    }
    return $lines -join [Environment]::NewLine
}

$outputFullPath = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($OutputPath))
$outputDirectory = Split-Path -Parent $outputFullPath
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw "Output directory does not exist: $outputDirectory"
}

$valheimRecords = @(Get-ValheimInstallRecords -ExplicitPath $ValheimPath)
$r2Records = Get-R2ProfileRecords -ExplicitDataPath $R2ModManDataPath -ExactProfileName $ProfileName

$notes = New-Object 'System.Collections.Generic.List[string]'
$notes.Add('Read-only discovery; no game, profile, registry, or system settings were changed.')
$notes.Add('No logs or configuration contents were read, no game files were copied, and no network requests were made.')
$notes.Add('Paths beneath the Windows user profile are sanitized to %USERPROFILE%. Review the report before sharing it.')
if ($valheimRecords.Count -eq 0) {
    $notes.Add('No Valheim installation was found. Re-run with -ValheimPath pointing to the folder that contains valheim.exe.')
}
if ($r2Records.profiles.Count -eq 0) {
    $notes.Add('No r2modman Valheim profile was found. Re-run with -R2ModManDataPath pointing to the Valheim data folder or profiles folder.')
}

$report = [ordered]@{
    schemaVersion         = 1
    generatedUtc          = [DateTime]::UtcNow.ToString('o')
    host                  = [ordered]@{
        osVersion         = [Environment]::OSVersion.VersionString
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
        edition           = if ($PSVersionTable.PSObject.Properties.Name -contains 'PSEdition') { $PSVersionTable.PSEdition } else { 'Desktop' }
        processArchitecture = $env:PROCESSOR_ARCHITECTURE
    }
    valheimInstallations  = @($valheimRecords)
    r2modman              = $r2Records
    notes                 = @($notes)
}

if ($Format -eq 'Json') {
    $content = $report | ConvertTo-Json -Depth 10
}
else {
    $content = Convert-ReportToText -Report $report
}

Set-Content -LiteralPath $outputFullPath -Value $content -Encoding UTF8
Write-Host "Stackmaster environment report written to: $outputFullPath"
Write-Host 'Review the report before sharing it. No software was installed and no game/profile files were changed.'
