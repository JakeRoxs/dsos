:: Assumed to be run from root directory.

set OUTPUT_ROOT=Bin\x64_release
if not exist "%OUTPUT_ROOT%" (
    if exist "intermediate\vs2022\bin\x64_release" set OUTPUT_ROOT=intermediate\vs2022\bin\x64_release
)
if not exist "%OUTPUT_ROOT%" (
    if exist "intermediate\vs2022\Source\Server" set OUTPUT_ROOT=intermediate\vs2022\Source\Server
)
if not exist "%OUTPUT_ROOT%" (
    if exist "intermediate\vs2022\Source\Server.DarkSouls3" set OUTPUT_ROOT=intermediate\vs2022\Source\Server.DarkSouls3
)
if not exist "%OUTPUT_ROOT%" (
    if exist "intermediate\vs2022\Source\Server.DarkSouls2" set OUTPUT_ROOT=intermediate\vs2022\Source\Server.DarkSouls2
)

echo Using package source output path: %OUTPUT_ROOT%

mkdir DS3OS
mkdir DS3OS\Loader
mkdir DS3OS\Server
mkdir DS3OS\Prerequisites
copy Resources\ReadMe.txt DS3OS\ReadMe.txt
xcopy /s Resources\Prerequisites DS3OS\Prerequisites

set ERR=0

if exist "%OUTPUT_ROOT%\steam_appid.txt" (
    xcopy /s "%OUTPUT_ROOT%\steam_appid.txt" DS3OS\Server\
) else if exist "Resources\steam_appid.txt" (
    xcopy /s "Resources\steam_appid.txt" DS3OS\Server\
) else (
    echo WARNING: steam_appid.txt not found in %OUTPUT_ROOT% or Resources
    set ERR=1
)
if exist "%OUTPUT_ROOT%\steam_api64.dll" (
    xcopy /s "%OUTPUT_ROOT%\steam_api64.dll" DS3OS\Server\
) else if exist "Source\ThirdParty\steam\redistributable_bin\win64\steam_api64.dll" (
    xcopy /s "Source\ThirdParty\steam\redistributable_bin\win64\steam_api64.dll" DS3OS\Server\
) else (
    echo WARNING: steam_api64.dll not found in %OUTPUT_ROOT% or source tree
    set ERR=1
)
if exist "%OUTPUT_ROOT%\WebUI" (
    xcopy /s "%OUTPUT_ROOT%\WebUI\" DS3OS\Server\WebUI\
) else if exist "Source\WebUI" (
    xcopy /s "Source\WebUI\" DS3OS\Server\WebUI\
) else (
    echo WARNING: WebUI folder not found in %OUTPUT_ROOT% or Source\WebUI
    set ERR=1
)
if exist "%OUTPUT_ROOT%\Server.exe" (
    xcopy /s "%OUTPUT_ROOT%\Server.exe" DS3OS\Server\
) else if exist "intermediate\vs2022\Source\Server\Server.exe" (
    xcopy /s "intermediate\vs2022\Source\Server\Server.exe" DS3OS\Server\
) else (
    echo WARNING: Server.exe not found in %OUTPUT_ROOT% or intermediate\vs2022\Source\Server
    set ERR=1
)
if exist "%OUTPUT_ROOT%\Server.pdb" (
    xcopy /s "%OUTPUT_ROOT%\Server.pdb" DS3OS\Server\
) else if exist "intermediate\vs2022\Source\Server\Server.pdb" (
    xcopy /s "intermediate\vs2022\Source\Server\Server.pdb" DS3OS\Server\
) else (
    echo WARNING: Server.pdb not found in %OUTPUT_ROOT% or intermediate\vs2022\Source\Server
    set ERR=1
)

if exist "%OUTPUT_ROOT%\Injector.pdb" (
    xcopy /s "%OUTPUT_ROOT%\Injector.pdb" DS3OS\Loader\
) else if exist "intermediate\vs2022\Source\Injector\Injector.pdb" (
    xcopy /s "intermediate\vs2022\Source\Injector\Injector.pdb" DS3OS\Loader\
) else if exist "Source\Injector\bin\x64_release\Injector.pdb" (
    xcopy /s "Source\Injector\bin\x64_release\Injector.pdb" DS3OS\Loader\
) else if exist "build\Source\Injector\Injector.pdb" (
    xcopy /s "build\Source\Injector\Injector.pdb" DS3OS\Loader\
) else (
    echo WARNING: Injector.pdb not found in %OUTPUT_ROOT% or source/bin/build paths
    set ERR=1
)
if exist "%OUTPUT_ROOT%\Injector.dll" (
    xcopy /s "%OUTPUT_ROOT%\Injector.dll" DS3OS\Loader\
) else if exist "intermediate\vs2022\Source\Injector\Injector.dll" (
    xcopy /s "intermediate\vs2022\Source\Injector\Injector.dll" DS3OS\Loader\
) else if exist "Source\Injector\bin\x64_release\Injector.dll" (
    xcopy /s "Source\Injector\bin\x64_release\Injector.dll" DS3OS\Loader\
) else if exist "build\Source\Injector\Injector.dll" (
    xcopy /s "build\Source\Injector\Injector.dll" DS3OS\Loader\
) else (
    echo WARNING: Injector.dll not found in %OUTPUT_ROOT% or source/bin/build paths
    set ERR=1
)

if "%ERR%"=="1" (
    echo ERROR: One or more required files were missing. Check build output path and CMake configuration.
    dir /b "%OUTPUT_ROOT%" 2>nul
    exit /b 1
) else (
    echo Package preparation succeeded.
)
