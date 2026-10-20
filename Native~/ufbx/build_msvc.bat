@echo off
rem Builds plufbx.dll with Visual Studio 2022 (Desktop development with C++)
rem and copies it to Runtime\Plugins\x86_64. Run from this folder (Native~\ufbx).
setlocal
set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSDIR=%%i
if not defined VSDIR (echo C++ build tools not found & exit /b 1)
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cd /d "%~dp0"
cl /nologo /O2 /LD /utf-8 /Fe:plufbx.dll plufbx.c ufbx.c || exit /b 1
copy /y plufbx.dll ..\..\Runtime\Plugins\x86_64\plufbx.dll >nul || exit /b 1
del /q *.obj plufbx.lib plufbx.exp plufbx.dll 2>nul
echo OK
