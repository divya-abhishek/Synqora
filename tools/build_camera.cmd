@echo off
setlocal
if exist "%~dp0..\.local\native-toolchain\msvc\setup_x64.bat" (
  call "%~dp0..\.local\native-toolchain\msvc\setup_x64.bat"
  if errorlevel 1 exit /b 1
)
where cl >nul 2>nul
if errorlevel 1 (
  echo Run from an x64 Visual Studio developer prompt with the Windows SDK installed.
  exit /b 1
)
pushd "%~dp0..\native\camera"
if not exist build mkdir build
cl /nologo /std:c++20 /EHsc /MT /O2 /W4 /external:anglebrackets /external:W0 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /D_WIN32_WINNT=0x0A00 /LD FrameFile.cpp MediaSource.cpp Activation.cpp Inspect.cpp /Fobuild\ /Febuild\Synqora.Camera.dll /link /DEF:Camera.def mfplat.lib mf.lib mfuuid.lib mfreadwrite.lib mfsensorgroup.lib ole32.lib advapi32.lib propsys.lib uuid.lib runtimeobject.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /W4 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /D_WIN32_WINNT=0x0A00 Probe.cpp /Fobuild\ /Febuild\CameraProbe.exe /link build\Synqora.Camera.lib mfplat.lib mf.lib mfuuid.lib mfreadwrite.lib ole32.lib advapi32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /MT /O2 /W4 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /D_WIN32_WINNT=0x0A00 DeviceProbe.cpp /Fobuild\ /Febuild\CameraDeviceProbe.exe /link build\Synqora.Camera.lib mfplat.lib mf.lib mfuuid.lib mfreadwrite.lib ole32.lib advapi32.lib
set compileResult=%errorlevel%
popd
exit /b %compileResult%
