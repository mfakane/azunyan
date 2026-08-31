@echo off
setlocal EnableExtensions

set "AzunoteArguments=%*"
set "AzunoteWait="
set "AzunoteOptions=1"

if "%~1"=="" goto launch
for %%A in (%*) do (
    if "%%~A"=="--" set "AzunoteOptions="
    if defined AzunoteOptions if /I "%%~A"=="--wait" set "AzunoteWait=1"
    if defined AzunoteOptions if /I "%%~A"=="-w" set "AzunoteWait=1"
    if defined AzunoteOptions if /I "%%~A"=="--help" set "AzunoteWait=1"
    if defined AzunoteOptions if /I "%%~A"=="-h" set "AzunoteWait=1"
)

:launch
if defined AzunoteWait (
    "%~dp0Azunote.exe" %AzunoteArguments%
) else (
    start "" /b "%~dp0Azunote.exe" %AzunoteArguments%
)
exit /b %ERRORLEVEL%
