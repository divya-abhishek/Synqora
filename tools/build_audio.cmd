@echo off
setlocal
call "%~dp0..\.local\native-toolchain\msvc\setup_x64.bat"
if errorlevel 1 exit /b 1
pushd "%~dp0.."
python tools\prepare_audio.py
if errorlevel 1 exit /b 1
set "SYNQORA_WDK=%CD%\.local\wdk\package\c"
set "SYNQORA_SDK=%CD%\.local\native-toolchain\msvc\Windows Kits\10"
pushd native\audio\build
if not exist obj mkdir obj
cl /nologo /std:c++17 /O2 /W4 /EHsc /MT ..\RingProbe.cpp /Foobj\ /FeRingProbe.exe
if errorlevel 1 exit /b 1
RingProbe.exe
if errorlevel 1 exit /b 1
cl /nologo /c /std:c++17 /kernel /O2 /Oi /GS /GR- /Zl /W3 /wd4996 /wd4595 /D_AMD64_ /DAMD64 /D_WIN64 /DWINNT=1 /DWINVER=0x0A00 /D_WIN32_WINNT=0x0A00 /DNTDDI_VERSION=0x0A00000C /DPOOL_NX_OPTIN=1 /D_USE_WAVERT_ /D_NEW_DELETE_OPERATORS_ /DPC_IMPLEMENTATION /DUNICODE /D_UNICODE /DDEBUG_LEVEL=0 /I.. /Isource /Isource\EndpointsCommon /Isource\TabletAudioSample /I"%SYNQORA_WDK%\Include\10.0.26100.0\km" /I"%SYNQORA_WDK%\Include\10.0.26100.0\km\crt" /I"%SYNQORA_WDK%\Include\wdf\kmdf\1.15" @sources.rsp ..\Bridge.cpp /Foobj\
if errorlevel 1 exit /b 1
link /nologo /driver /subsystem:native,10.00 /entry:FxDriverEntry /nodefaultlib /INTEGRITYCHECK /DYNAMICBASE /NXCOMPAT /OUT:SynqoraMicrophone.sys /LIBPATH:"%SYNQORA_WDK%\Lib\10.0.26100.0\km\x64" /LIBPATH:"%SYNQORA_WDK%\Lib\wdf\kmdf\x64\1.15" obj\adapter.obj obj\basetopo.obj obj\common.obj obj\hw.obj obj\kshelper.obj obj\savedata.obj obj\tonegenerator.obj obj\AudioModuleHelper.obj obj\MiniportAudioEngineNode.obj obj\MiniportStreamAudioEngineNode.obj obj\mintopo.obj obj\minwavert.obj obj\minwavertstream.obj obj\NewDelete.obj obj\micarraytopo.obj obj\speakertopo.obj obj\speakerhptopo.obj obj\hdmitopo.obj obj\micintopo.obj obj\spdiftopo.obj obj\Bridge.obj ntoskrnl.lib hal.lib wmilib.lib wdmsec.lib portcls.lib stdunk.lib libcntpr.lib BufferOverflowFastFailK.lib WdfDriverEntry.lib WdfLdr.lib
set compileResult=%errorlevel%
popd
popd
popd
exit /b %compileResult%

