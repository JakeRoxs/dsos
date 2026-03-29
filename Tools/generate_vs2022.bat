@echo off

SET ScriptPath=%~dp0
REM Determine the repository root as the parent of Tools
SET RootPath=%ScriptPath%..\
for %%I in ("%RootPath%") do set "RootPath=%%~fI"
if "%RootPath:~-1%"=="\" set "RootPath=%RootPath:~0,-1%"

if not exist "%RootPath%" (
    echo ERROR: calculated root path does not exist: %RootPath%
    echo Exiting.
    exit /b 1
)

SET BuildPath=%RootPath%\intermediate\vs2022
if not exist "%BuildPath%" (
    echo Creating build directory: %BuildPath%
    mkdir "%BuildPath%" 2>nul || (
      echo ERROR: cannot create build path %BuildPath%
      exit /b 1
    )
)

SET CMakeExePath=%ScriptPath%Build\cmake\windows\bin\cmake.exe
if not exist "%CMakeExePath%" (
    echo Vendored CMake not found at: %CMakeExePath%, searching PATH...
    for /f "delims=" %%i in ('where cmake 2^>nul') do (
        if exist "%%i" (
            set "CMakeExePath=%%i"
            echo Found cmake on PATH: %%i
            goto :foundCMake
        )
    )
)

if not exist "%CMakeExePath%" (
    echo WARNING: cmake not found from vendored path or PATH.
    echo Attempting to install cmake with choco if available...
    choco install -y cmake --installargs "ADD_CMAKE_TO_PATH=System" >nul 2>&1 || echo "choco install cmake failed or not available"
    for /f "delims=" %%i in ('where cmake 2^>nul') do (
        if exist "%%i" (
            set "CMakeExePath=%%i"
            echo Found cmake after choco install: %%i
            goto :foundCMake
        )
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
