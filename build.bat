@echo off
rem ==========================================================================
rem  True Flasher - build script            Made by Adromedary
rem  Uses the C# compiler that ships with Windows (.NET Framework 4.x).
rem  No Visual Studio or SDK needed.
rem
rem    build.bat          build bin\TrueFlasher.exe (window) and bin\TrueFlasherCLI.exe (console)
rem    build.bat test     build, then run the simulated fake-drive tests
rem ==========================================================================
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] The .NET Framework 4 C# compiler was not found.
    echo         Turn on ".NET Framework 4.8 Advanced Services" in "Turn Windows features on or off".
    goto :fail
)

if not exist bin mkdir bin

set "COMMON=/nologo /optimize+ /platform:anycpu /win32manifest:src\app.manifest /win32icon:src\app.ico /resource:src\app.ico,TrueFlasher.app.ico /resource:src\logo.png,TrueFlasher.logo.png /r:System.Windows.Forms.dll /r:System.Drawing.dll"

echo Building True Flasher (window)...
"%CSC%" %COMMON% /target:winexe /main:TrueFlasher.GuiProgram /out:bin\TrueFlasher.exe src\*.cs
if errorlevel 1 goto :fail
echo   OK  bin\TrueFlasher.exe

echo Building True Flasher CLI (console)...
"%CSC%" %COMMON% /target:exe /main:TrueFlasher.Program /out:bin\TrueFlasherCLI.exe src\*.cs
if errorlevel 1 goto :fail
echo   OK  bin\TrueFlasherCLI.exe

if /i "%~1"=="test" (
    echo.
    echo Building simulation tests...
    "%CSC%" /nologo /optimize+ /target:exe /platform:anycpu /out:bin\SimTests.exe tests\SimTests.cs src\Pattern.cs src\Device.cs src\Engine.cs src\Report.cs
    if errorlevel 1 goto :fail
    echo Running simulation tests ^(needs about 2 GB of free RAM^)...
    echo.
    bin\SimTests.exe
    if errorlevel 1 goto :fail
)

echo.
echo Done. Double-click bin\TrueFlasher.exe ^(Windows will ask for administrator rights^).
if "%~1"=="" pause
exit /b 0

:fail
echo.
echo Build FAILED.
if "%~1"=="" pause
exit /b 1
