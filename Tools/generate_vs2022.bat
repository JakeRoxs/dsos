@echo off

SET ScriptPath=%~dp0
REM Determine the repository root as the parent of Tools
SET RootPath=%ScriptPath%..\
for %%I in ("%RootPath%") do set "RootPath=%%~fI"
if "%RootPath:~-1%"=="\" set "RootPath=%RootPath:~0,-1%"
SET BuildPath=%RootPath%\intermediate\vs2022
SET CMakeExePath=%ScriptPath%Build\cmake\windows\bin\cmake.exe

if not exist "%CMakeExePath%" (
    echo Vendored CMake not found at: %CMakeExePath%, searching PATH...
    for /f "delims=" %%i in ('where cmake 2^>nul') do (
        set "CMakeExePath=%%i"
        echo Found cmake on PATH: %%i
        goto :foundCMake
    )
)
:foundCMake
if not exist "%CMakeExePath%" (
    echo ERROR: cmake not found. Install cmake or set PATH to cmake.exe.
    exit /b 1
)

echo Using CMake executable: %CMakeExePath%

REM Optional: override the CMake generator via the GENERATOR environment variable.
REM If GENERATOR is not set, default to the Visual Studio 17 2022 generator.
REM Example: set "GENERATOR=Ninja" before running this script to generate Ninja build files
REM (Ninja must be installed and available on PATH or otherwise discoverable by CMake).
if "%GENERATOR%"=="" (
    set "GENERATOR=Visual Studio 17 2022"
)

echo Generating %RootPath%
echo %CMakeExePath% -S %RootPath% -B %BuildPath% -G "%GENERATOR%"

"%CMakeExePath%" -S "%RootPath%" -B "%BuildPath%" -G "%GENERATOR%"
