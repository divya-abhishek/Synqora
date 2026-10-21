#include "Camera.h"
#include <mfapi.h>
#include <mfreadwrite.h>
#include <wrl.h>
#include <atomic>
#include <cstdio>
#include <filesystem>
#include <thread>
#include <vector>
using Microsoft::WRL::ComPtr;

HRESULT Consume(bool expectRed) {
    ComPtr<IMFAttributes> attributes;
    HRESULT hr = MFCreateAttributes(&attributes, 1);
    if (SUCCEEDED(hr)) hr = attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    IMFActivate** devices = nullptr; UINT32 count = 0;
    if (SUCCEEDED(hr)) hr = MFEnumDeviceSources(attributes.Get(), &devices, &count);
    ComPtr<IMFActivate> selected;
    if (SUCCEEDED(hr)) {
        for (UINT32 i = 0; i < count; ++i) {
            WCHAR name[256]{};
            if (SUCCEEDED(devices[i]->GetString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, name, ARRAYSIZE(name), nullptr)) && wcsstr(name, L"Synqora Camera")) selected = devices[i];
            devices[i]->Release();
        }
        CoTaskMemFree(devices);
        if (!selected) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        std::puts("PASS: Synqora is enumerated as a Windows video capture device");
    }
    ComPtr<IMFMediaSource> source; ComPtr<IMFSourceReader> reader;
    if (SUCCEEDED(hr)) hr = selected->ActivateObject(IID_PPV_ARGS(&source));
    if (SUCCEEDED(hr)) hr = MFCreateSourceReaderFromMediaSource(source.Get(), nullptr, &reader);
    ComPtr<IMFSample> sample;
    for (int i = 0; i < 8 && SUCCEEDED(hr) && !sample; ++i) {
        DWORD index = 0, flags = 0; LONGLONG timestamp = 0;
        hr = reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &index, &flags, &timestamp, &sample);
    }
    if (SUCCEEDED(hr) && !sample) hr = E_UNEXPECTED;
    if (SUCCEEDED(hr)) {
        ComPtr<IMFMediaBuffer> buffer; hr = sample->ConvertToContiguousBuffer(&buffer);
        BYTE* pixels = nullptr; DWORD length = 0;
        if (SUCCEEDED(hr)) hr = buffer->Lock(&pixels, nullptr, &length);
        if (SUCCEEDED(hr)) {
            // The publisher sends a red test picture with neutral aspect bars.
            const bool valid = length >= CameraWidth * CameraHeight && (!expectRed || pixels[CameraWidth * (CameraHeight / 2) + CameraWidth / 2] > 50);
            buffer->Unlock();
            if (!valid) hr = E_UNEXPECTED;
            else std::puts("PASS: an independent process receives the published pixels through Windows capture");
        }
    }
    sample.Reset(); reader.Reset(); if (source) source->Shutdown();
    return hr;
}
int wmain(int argc, wchar_t** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return 1;
    hr = MFStartup(MF_VERSION);
    if (FAILED(hr)) { CoUninitialize(); return 1; }
    const bool observe = argc > 1 && (wcscmp(argv[1], L"--observe") == 0 || wcscmp(argv[1], L"--observe-red") == 0);
    if (observe) hr = Consume(wcscmp(argv[1], L"--observe-red") == 0);
    else {
        const auto folder = std::filesystem::absolute(argc > 1 ? argv[1] : L".");
        std::filesystem::create_directories(folder);
        const auto path = folder / L"device-probe.bin";
        void *writer = nullptr, *camera = nullptr;
        hr = SynqoraCameraCreate(path.c_str(), &writer);
        std::atomic<bool> running = true;
        std::thread producer;
        if (SUCCEEDED(hr)) producer = std::thread([&] {
            std::vector<BYTE> red(64 * 64 * 4);
            for (size_t i = 0; i < red.size(); i += 4) { red[i + 2] = 255; red[i + 3] = 255; }
            while (running) { SynqoraCameraWrite(writer, red.data(), static_cast<UINT32>(red.size()), 64, 64, 256); Sleep(60); }
        });
        if (SUCCEEDED(hr)) hr = SynqoraCameraStart(path.c_str(), &camera);
        if (SUCCEEDED(hr)) {
            std::puts("PASS: Windows accepted the session camera registration");
            WCHAR executable[MAX_PATH]{}; GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable));
            std::wstring command = L"\"" + std::wstring(executable) + L"\" --observe-red";
            STARTUPINFOW startup{sizeof(startup)}; PROCESS_INFORMATION process{};
            if (!CreateProcessW(executable, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) hr = HRESULT_FROM_WIN32(GetLastError());
            else {
                if (WaitForSingleObject(process.hProcess, 20000) == WAIT_TIMEOUT) { TerminateProcess(process.hProcess, 2); hr = HRESULT_FROM_WIN32(ERROR_TIMEOUT); }
                else { DWORD code = 1; GetExitCodeProcess(process.hProcess, &code); if (code) hr = static_cast<HRESULT>(code); }
                CloseHandle(process.hThread); CloseHandle(process.hProcess);
            }
        }
        running = false; if (producer.joinable()) producer.join();
        SynqoraCameraStop(camera); SynqoraCameraDestroy(writer);
    }
    std::printf("Windows camera device probe: 0x%08lx\n", static_cast<unsigned long>(hr));
    MFShutdown(); CoUninitialize(); return FAILED(hr) ? static_cast<int>(hr) : 0;
}
