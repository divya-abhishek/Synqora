#pragma once
#include <windows.h>
#include <mfidl.h>
#include <vector>

// Original Synqora identities. These must stay stable across installer upgrades.
inline constexpr GUID CLSID_SynqoraCamera = {0x57fd6f94,0x9413,0x4e47,{0xa4,0xca,0xc0,0xaf,0xd7,0x38,0xd1,0x52}};
inline constexpr GUID SynqoraFrameFile = {0x9acc4ae2,0x70aa,0x4081,{0xaf,0x5d,0xad,0x09,0x12,0x43,0x0f,0xb8}};
inline constexpr UINT32 CameraWidth = 1280, CameraHeight = 720, CameraFps = 60;
inline constexpr DWORD PixelBytes = CameraWidth * CameraHeight * 4;

#ifdef SYNQORA_CAMERA_EXPORTS
#define CAMERA_API extern "C" __declspec(dllexport)
#else
#define CAMERA_API extern "C" __declspec(dllimport)
#endif
CAMERA_API HRESULT __stdcall SynqoraCameraCreate(LPCWSTR path, void** context);
CAMERA_API HRESULT __stdcall SynqoraCameraWrite(void* context, const BYTE* bgra, UINT32 bytes, UINT32 width, UINT32 height, UINT32 stride);
CAMERA_API void __stdcall SynqoraCameraSetFraming(void* context, BOOL fill);
CAMERA_API void __stdcall SynqoraCameraClear(void* context);
CAMERA_API void __stdcall SynqoraCameraDestroy(void* context);
CAMERA_API HRESULT __stdcall SynqoraCameraCreateSource(LPCWSTR path, IMFMediaSource** source);
CAMERA_API HRESULT __stdcall SynqoraCameraStart(LPCWSTR path, void** camera);
CAMERA_API void __stdcall SynqoraCameraStop(void* camera);
struct SourceSampleInfo {
    UINT32 width, height, bytes;
    LONGLONG timestamp;
    UINT64 checksum;
    BYTE minimumLuma, maximumLuma;
};
static_assert(sizeof(SourceSampleInfo) == 40);
CAMERA_API HRESULT __stdcall SynqoraCameraInspect(LPCWSTR path, SourceSampleInfo* info);

struct FrameHeader {
    UINT32 magic, version;
    volatile LONG sequence;
    UINT32 bytes;
    ULONGLONG tick;
    UINT32 width, height;
};
static_assert(sizeof(FrameHeader) == 32);

class FrameReader {
    HANDLE file_ = INVALID_HANDLE_VALUE, mapping_ = nullptr;
    const FrameHeader* data_ = nullptr;
    std::vector<BYTE> lastFrame_;
    ULONGLONG lastTick_ = 0;
public:
    ~FrameReader();
    HRESULT Open(LPCWSTR path);
    bool Read(BYTE* pixels, DWORD size);
};
