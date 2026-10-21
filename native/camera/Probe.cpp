#include "Camera.h"
#include <mfapi.h>
#include <mfreadwrite.h>
#include <wrl.h>
#include <filesystem>
#include <cstdio>
#include <stdexcept>
#include <vector>
using Microsoft::WRL::ComPtr;
void Check(HRESULT hr, const char* step) { if (FAILED(hr)) { std::printf("FAIL: %s (0x%08lx)\n", step, static_cast<unsigned long>(hr)); throw std::runtime_error(step); } }
void Assert(bool ok, const char* step) { if (!ok) throw std::runtime_error(step); std::printf("PASS: %s\n", step); }

struct Reader {
    ComPtr<IMFMediaSource> source;
    ComPtr<IMFSourceReader> reader;
    Reader(LPCWSTR path, REFGUID format) {
        Check(SynqoraCameraCreateSource(path, &source), "create original source");
        Check(MFCreateSourceReaderFromMediaSource(source.Get(), nullptr, &reader), "create Windows source reader");
        ComPtr<IMFMediaType> type; Check(MFCreateMediaType(&type), "create type");
        Check(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), "video type"); Check(type->SetGUID(MF_MT_SUBTYPE, format), "pixel format");
        Check(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, CameraWidth, CameraHeight), "dimensions");
        Check(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, CameraFps, 1), "frame rate");
        Check(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), nullptr, type.Get()), "negotiate pixel format");
    }
    ~Reader() { reader.Reset(); if (source) source->Shutdown(); }
    std::vector<BYTE> Read(LONGLONG& timestamp) {
        ComPtr<IMFSample> sample;
        for (int i = 0; i < 8 && !sample; ++i) {
            DWORD stream = 0, flags = 0;
            Check(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &stream, &flags, &timestamp, &sample), "read camera sample");
        }
        Assert(sample != nullptr, "Windows receives a camera sample");
        ComPtr<IMFMediaBuffer> buffer; Check(sample->ConvertToContiguousBuffer(&buffer), "contiguous frame");
        BYTE* bytes = nullptr; DWORD size = 0; Check(buffer->Lock(&bytes, nullptr, &size), "lock output");
        std::vector<BYTE> result(bytes, bytes + size); Check(buffer->Unlock(), "unlock output"); return result;
    }
};

// Deterministically hold our own private writer in the middle of a publication.
// This exercises the source through Media Foundation without system registration.
struct InProgressFrame {
    HANDLE file = INVALID_HANDLE_VALUE, mapping = nullptr;
    FrameHeader* header = nullptr;
    void Open(LPCWSTR path) {
        file = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        Assert(file != INVALID_HANDLE_VALUE, "open isolated frame contention fixture");
        mapping = CreateFileMappingW(file, nullptr, PAGE_READWRITE, 0, 0, nullptr);
        Assert(mapping != nullptr, "map isolated frame contention fixture");
        header = static_cast<FrameHeader*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(FrameHeader) + PixelBytes));
        Assert(header != nullptr, "view isolated frame contention fixture");
        InterlockedIncrement(&header->sequence);
        ZeroMemory(header + 1, PixelBytes);
    }
    ~InProgressFrame() {
        if (header) { header->tick = 0; MemoryBarrier(); if (header->sequence & 1) InterlockedIncrement(&header->sequence); UnmapViewOfFile(header); }
        if (mapping) CloseHandle(mapping);
        if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    }
};

int wmain(int argc, wchar_t** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    std::puts("Starting isolated native camera checks.");
    const auto folder = std::filesystem::absolute(argc > 1 ? argv[1] : L".");
    std::filesystem::create_directories(folder);
    const auto path = folder / (L"camera-probe-" + std::to_wstring(GetCurrentProcessId()) + L".bin");
    void* writer = nullptr;
    int status = 0;
    try {
        Check(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "initialize COM"); Check(MFStartup(MF_VERSION), "initialize Media Foundation");
        Check(SynqoraCameraCreate(path.c_str(), &writer), "create private pixel file");
        {
            const auto module = GetModuleHandleW(L"Synqora.Camera.dll");
            const auto getFactory = reinterpret_cast<HRESULT (WINAPI*)(REFCLSID, REFIID, void**)>(GetProcAddress(module, "DllGetClassObject"));
            const auto canUnload = reinterpret_cast<HRESULT (WINAPI*)()>(GetProcAddress(module, "DllCanUnloadNow"));
            Assert(getFactory && canUnload, "camera COM entry points exported");
            ComPtr<IClassFactory> factory; Check(getFactory(CLSID_SynqoraCamera, IID_PPV_ARGS(&factory)), "obtain camera factory without system registration");
            ComPtr<IMFActivate> activation; Check(factory->CreateInstance(nullptr, IID_PPV_ARGS(&activation)), "create COM activation object");
            Check(activation->SetString(SynqoraFrameFile, path.c_str()), "pass frame source through activation attributes");
            ComPtr<IMFMediaSource> source; Check(activation->ActivateObject(IID_PPV_ARGS(&source)), "activate original source through COM contract");
            activation.Reset();
            DWORD characteristics = 0;
            Check(source->GetCharacteristics(&characteristics), "source survives release of its activation object (Frame Server lifetime)");
            Assert(canUnload() == S_FALSE, "COM objects keep the source DLL loaded");
            source->Shutdown(); source.Reset(); factory.Reset();
            Assert(canUnload() == S_OK, "source DLL releases its COM lifetime after shutdown");
        }
        std::vector<BYTE> red(64 * 64 * 4);
        for (size_t i = 0; i < red.size(); i += 4) { red[i + 2] = 255; red[i + 3] = 255; }
        Check(SynqoraCameraWrite(writer, red.data(), static_cast<UINT32>(red.size()), 64, 64, 256), "publish red test frame");
        Assert(SynqoraCameraWrite(writer, red.data(), 4, 64, 64, 256) == E_INVALIDARG, "undersized input rejected");
        {
            Reader camera(path.c_str(), MFVideoFormat_NV12); LONGLONG first = 0, next = 0;
            const auto frame = camera.Read(first);
            Assert(frame.size() == CameraWidth * CameraHeight * 3 / 2, "NV12 frame has negotiated length");
            Assert(frame[CameraWidth / 2 + CameraWidth * (CameraHeight / 2)] >= 80 && frame[0] == 16, "phone picture preserves aspect ratio with neutral bars");
            camera.Read(next); Assert(next > first, "sample timestamps increase");
            {
                InProgressFrame busy; busy.Open(path.c_str());
                const auto repeated = camera.Read(next);
                Assert(repeated[CameraWidth / 2 + CameraWidth * (CameraHeight / 2)] >= 80, "busy producer repeats the last complete frame without a black flash");
                busy.header->tick = 0; MemoryBarrier();
                const auto cleared = camera.Read(next);
                Assert(cleared[CameraWidth / 2 + CameraWidth * (CameraHeight / 2)] == 16, "clear invalidates the cached picture even during publication");
            }
        }
        Check(SynqoraCameraWrite(writer, red.data(), static_cast<UINT32>(red.size()), 64, 64, 256), "refresh frame");
        {
            Reader camera(path.c_str(), MFVideoFormat_RGB32); LONGLONG timestamp = 0;
            const auto frame = camera.Read(timestamp);
            const auto center = (CameraWidth * (CameraHeight / 2) + CameraWidth / 2) * 4;
            Assert(frame.size() == PixelBytes && frame[center + 2] == 255, "RGB32 negotiation carries original pixels");
        }
        SynqoraCameraSetFraming(writer, TRUE);
        auto bands = red;
        for (UINT32 y = 0; y < 64; ++y) if (y < 14 || y >= 50) for (UINT32 x = 0; x < 64; ++x) {
            const auto offset = (y * 64 + x) * 4; bands[offset] = 255; bands[offset + 2] = 0;
        }
        Check(SynqoraCameraWrite(writer, bands.data(), static_cast<UINT32>(bands.size()), 64, 64, 256), "publish cropped fill frame");
        {
            Reader camera(path.c_str(), MFVideoFormat_RGB32); LONGLONG timestamp = 0;
            const auto frame = camera.Read(timestamp);
            Assert(frame[2] == 255 && frame[0] == 0 && frame[PixelBytes - 2] == 255, "fill framing crops outer bands rather than stretching the whole picture");
        }
        SynqoraCameraSetFraming(writer, FALSE);
        SynqoraCameraClear(writer);
        {
            Reader camera(path.c_str(), MFVideoFormat_NV12); LONGLONG timestamp = 0;
            const auto frame = camera.Read(timestamp);
            Assert(frame[CameraWidth * (CameraHeight / 2) + CameraWidth / 2] == 16, "disconnect clears the published picture");
        }
    } catch (const std::exception& error) { std::printf("FAILED: %s\n", error.what()); status = 1; }
    SynqoraCameraDestroy(writer); MFShutdown(); CoUninitialize();
    if (std::filesystem::exists(path)) { std::puts("FAIL: private frame file remained open."); status = 1; }
    else std::puts("PASS: private frame file removed after handles close");
    return status;
}
