<#
.SYNOPSIS
  Helper script to configure and build rekindled-server with sane generator fallback.
.DESCRIPTION
  Detects Ninja and Visual Studio (via vswhere), or uses explicit -Generator parameter.
  Ensures out-of-source build and portable defaults for local dev and CI.
#>
param(
    [string]$Generator = "",
    [string]$BuildType = "Release",
    [string]$SourceDir = ".",
    [string]$BuildDir = "build",
    [switch]$Clean,
    [switch]$DryRun,
    [switch]$Help
)

# Set SourceDir default to repo root, as script is run from Tools folder.
if ($SourceDir -eq ".") {
    $SourceDir = Split-Path -Parent $PSScriptRoot
}

if ($Help) {
    Write-Host "Usage: .\\Tools\\build-cmake.ps1 [-Generator <Ninja|Visual Studio 18 2026|...>] [-BuildType <Debug|Release>] [-SourceDir <path>] [-BuildDir <path>] [-Clean] [-DryRun]"
    return
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-JobCountForBuild {
    param([int]$Requested = 0)

    if ($Requested -gt 0) { return $Requested }

    $jobCount = 0

    if ($Env:NUMBER_OF_PROCESSORS) {
        [int]$parsed = 0
        if ([int]::TryParse($Env:NUMBER_OF_PROCESSORS, [ref]$parsed) -and $parsed -gt 0) {
            $jobCount = $parsed
        }
    }

    if ($jobCount -le 0) {
        $jobCount = [System.Environment]::ProcessorCount
    }

    if ($jobCount -le 0) {
        $jobCount = 1
    }

    return $jobCount
}

function Find-VisualStudioGenerator {
    $vswhere = "$Env:ProgramFiles(x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { return $null }

    $vsinfo = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion -nologo 2>$null
    if (-not $vsinfo) { return $null }

    if ($vsinfo -match '^18(\.|$)') { return 'Visual Studio 18 2026' }
    if ($vsinfo -match '^17(\.|$)') { return 'Visual Studio 17 2022' }
    if ($vsinfo -match '^16(\.|$)') { return 'Visual Studio 16 2019' }
    return $null
}

if (-not $Generator) {
    if (Get-Command ninja -ErrorAction SilentlyContinue) {
        $Generator = 'Ninja'
        Write-Host "Using detected generator: Ninja"
    }
    elseif (Get-Command g++ -ErrorAction SilentlyContinue -or Get-Command clang++ -ErrorAction SilentlyContinue) {
        $Generator = 'Ninja'
        Write-Host "Using default generator: Ninja (compiler found)"
    }
    else {
        $Generator = Find-VisualStudioGenerator
        if ($Generator) {
            Write-Host "Using detected Visual Studio generator: $Generator"
        }
        else {
            if ($Env:GENERATOR) {
                $Generator = $Env:GENERATOR
                Write-Host "Using generator from GENERATOR env var: $Generator"
            }
            else {
                $message = "No supported CMake generator detected. Install Ninja or Visual Studio with C++ workload, or pass -Generator explicitly."
                Write-Host "ERROR: $message" -ForegroundColor Red
                throw $message
            }
        }
    }
}

# At this point we either have a valid generator or we have already thrown an error.
# No further generator check is required.


if ($Clean -and (Test-Path $BuildDir)) {
    Write-Host "Cleaning existing build folder: $BuildDir"
    Remove-Item -Recurse -Force $BuildDir
}

$cmakeArgs = @(
    '-S', $SourceDir,
    '-B', $BuildDir,
    '-G', $Generator,
    "-DCMAKE_BUILD_TYPE=$BuildType",
    '-DCMAKE_EXPORT_COMPILE_COMMANDS=ON'
)

Write-Host "Configuring with: cmake $($cmakeArgs -join ' ')"
if (-not $DryRun) {
    cmake @cmakeArgs
    if ($LASTEXITCODE -ne 0) { throw "CMake configure failed with exit code $LASTEXITCODE" }

    Write-Host "Building project ($BuildType)..."

    if ($Generator -like 'Visual Studio*') {
        cmake --build $BuildDir --config $BuildType -- /m
    }
    else {
        # Force numeric value and filter invalid strings (e.g. empty or malformed env data).
        $jobs = [int](Get-JobCountForBuild)

        if ($jobs -le 0) {
            Write-Host "Computed invalid job count ($jobs), defaulting to 1"
            $jobs = 1
        }

        if ($jobs -le 1) {
            Write-Host "Using single-threaded Ninja build (jobs=1)"
            Write-Host "Command: cmake --build $BuildDir --config $BuildType"
            cmake --build $BuildDir --config $BuildType
        }
        else {
            Write-Host "Using Ninja parallel build (parallel=$jobs)"
            Write-Host "Command: cmake --build $BuildDir --config $BuildType --parallel $jobs"
            cmake --build $BuildDir --config $BuildType --parallel $jobs
        }
    }

    if ($LASTEXITCODE -ne 0) { throw "CMake build failed with exit code $LASTEXITCODE" }

    Write-Host "Build complete: $BuildDir" -ForegroundColor Green
}
