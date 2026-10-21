@echo off
setlocal
call "%~dp0..\.local\native-toolchain\msvc\setup_x64.bat"
if errorlevel 1 exit /b 1
pushd "%~dp0..\native\camera"
if not exist "..\..\.local\camera-flicker-baseline" mkdir "..\..\.local\camera-flicker-baseline"
copy /y "build\Synqora.Camera.dll" "..\..\.local\camera-flicker-baseline\Synqora.Camera.dll" >nul
cl /nologo /std:c++20 /EHsc /MT /O2 /W4 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /D_WIN32_WINNT=0x0A00 Probe.cpp /Fo"..\..\.local\camera-flicker-baseline\Probe.obj" /Fe"..\..\.local\camera-flicker-baseline\CameraProbe.exe" /link build\Synqora.Camera.lib mfplat.lib mf.lib mfuuid.lib mfreadwrite.lib ole32.lib advapi32.lib
set result=%errorlevel%
popd
exit /b %result%
