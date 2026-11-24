@echo off
rem Builds TarkovAudio.dll (x64) and its test runner with the MSVC toolchain.
rem Usage: build.cmd [test]
setlocal
set "HERE=%~dp0"
set "OUT=%HERE%bin"

where cl >nul 2>nul
if not errorlevel 1 goto :have_cl

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :no_msvc
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR goto :no_msvc
rem vcvars shells out to vswhere by bare name.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

:have_cl

if not exist "%OUT%" mkdir "%OUT%"
pushd "%OUT%"

set "CFLAGS=/nologo /std:c++17 /O2 /fp:fast /EHsc /W4 /MT /DNDEBUG"

cl %CFLAGS% /LD "%HERE%TarkovAudio.cpp" /Fe:TarkovAudio.dll /link advapi32.lib || goto :fail

if /i "%~1"=="test" (
    cl %CFLAGS% "%HERE%TarkovAudioTest.cpp" /Fe:TarkovAudioTest.exe || goto :fail
    "%OUT%\TarkovAudioTest.exe" "%OUT%\TarkovAudio.dll" || goto :fail
)

popd
exit /b 0

:fail
popd
exit /b 1

:no_msvc
echo TarkovAudio: MSVC not found. Install Visual Studio Build Tools with the C++ workload. 1>&2
exit /b 1
