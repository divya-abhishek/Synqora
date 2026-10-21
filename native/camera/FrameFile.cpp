#define SYNQORA_CAMERA_EXPORTS
#include "Camera.h"
#include <sddl.h>
#include <algorithm>
#include <memory>
#include <string>
#include <vector>

namespace {
constexpr UINT32 Magic = 0x53595143;
struct Writer {
    HANDLE file = INVALID_HANDLE_VALUE, mapping = nullptr;
    FrameHeader* data = nullptr;
    bool fill = false;
    ~Writer() { if (data) UnmapViewOfFile(data); if (mapping) CloseHandle(mapping); if (file != INVALID_HANDLE_VALUE) CloseHandle(file); }
};
HRESULT LastError() { return HRESULT_FROM_WIN32(GetLastError()); }
}

HRESULT __stdcall SynqoraCameraCreate(LPCWSTR path, void** context) {
    if (!path || !context) return E_POINTER;
    *context = nullptr;
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return LastError();
    DWORD size = 0; GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<BYTE> user(size);
    if (!GetTokenInformation(token, TokenUser, user.data(), size, &size)) { auto hr = LastError(); CloseHandle(token); return hr; }
    CloseHandle(token);
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid, &sid)) return LastError();
    // The pixel file is private to its user and the Windows camera services. It has no session keys.
    std::wstring sddl = L"D:P(A;;GA;;;SY)(A;;GR;;;LS)(A;;GA;;;"; sddl += sid; sddl += L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) return LastError();
    SECURITY_ATTRIBUTES security{sizeof(security), descriptor, FALSE};
    auto writer = std::make_unique<Writer>();
    writer->file = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, &security, CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
    LocalFree(descriptor);
    if (writer->file == INVALID_HANDLE_VALUE) return LastError();
    writer->mapping = CreateFileMappingW(writer->file, nullptr, PAGE_READWRITE, 0, sizeof(FrameHeader) + PixelBytes, nullptr);
    if (!writer->mapping) return LastError();
    writer->data = static_cast<FrameHeader*>(MapViewOfFile(writer->mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(FrameHeader) + PixelBytes));
    if (!writer->data) return LastError();
    writer->data->magic = Magic; writer->data->version = 1; writer->data->bytes = PixelBytes;
    writer->data->width = CameraWidth; writer->data->height = CameraHeight;
    *context = writer.release();
    return S_OK;
}

HRESULT __stdcall SynqoraCameraWrite(void* context, const BYTE* pixels, UINT32 bytes, UINT32 width, UINT32 height, UINT32 stride) {
    if (!context || !pixels) return E_POINTER;
    if (!width || !height || width > 1920 || height > 1920 || UINT64(width) * height > 1920 * 1080 || stride < width * 4 || UINT64(stride) * height > bytes) return E_INVALIDARG;
    auto writer = static_cast<Writer*>(context);
    auto header = writer->data;
    BYTE* output = reinterpret_cast<BYTE*>(header + 1);
    UINT32 sourceWidth = width, sourceHeight = height;
    if (writer->fill) {
        if (UINT64(width) * CameraHeight > UINT64(height) * CameraWidth) sourceWidth = std::max(1u, height * CameraWidth / CameraHeight);
        else sourceHeight = std::max(1u, width * CameraHeight / CameraWidth);
    }
    const UINT32 sourceLeft = (width - sourceWidth) / 2, sourceTop = (height - sourceHeight) / 2;
    const double scale = std::min(double(CameraWidth) / width, double(CameraHeight) / height);
    const UINT32 outWidth = writer->fill ? CameraWidth : std::max(1u, UINT32(width * scale)), outHeight = writer->fill ? CameraHeight : std::max(1u, UINT32(height * scale));
    const UINT32 left = (CameraWidth - outWidth) / 2, top = (CameraHeight - outHeight) / 2;
    InterlockedIncrement(&header->sequence);
    ZeroMemory(output, PixelBytes);
    // Bounded CPU scaling on the decode worker; preserve phone orientation and aspect ratio.
    for (UINT32 y = 0; y < outHeight; ++y) {
        const BYTE* row = pixels + (sourceTop + UINT64(y) * sourceHeight / outHeight) * stride;
        auto target = reinterpret_cast<UINT32*>(output + ((top + y) * CameraWidth + left) * 4);
        for (UINT32 x = 0; x < outWidth; ++x) target[x] = reinterpret_cast<const UINT32*>(row)[sourceLeft + UINT64(x) * sourceWidth / outWidth] | 0xff000000u;
    }
    header->tick = GetTickCount64();
    MemoryBarrier(); InterlockedIncrement(&header->sequence);
    return S_OK;
}
void __stdcall SynqoraCameraSetFraming(void* context, BOOL fill) { if (context) static_cast<Writer*>(context)->fill = fill != FALSE; }
void __stdcall SynqoraCameraClear(void* context) {
    if (!context) return;
    auto header = static_cast<Writer*>(context)->data;
    InterlockedIncrement(&header->sequence); header->tick = 0;
    ZeroMemory(header + 1, PixelBytes); MemoryBarrier(); InterlockedIncrement(&header->sequence);
}
void __stdcall SynqoraCameraDestroy(void* context) { SynqoraCameraClear(context); delete static_cast<Writer*>(context); }

FrameReader::~FrameReader() { if (data_) UnmapViewOfFile(data_); if (mapping_) CloseHandle(mapping_); if (file_ != INVALID_HANDLE_VALUE) CloseHandle(file_); }
HRESULT FrameReader::Open(LPCWSTR path) {
    file_ = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file_ == INVALID_HANDLE_VALUE) return LastError();
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file_, &size) || size.QuadPart != sizeof(FrameHeader) + PixelBytes) return E_INVALIDARG;
    mapping_ = CreateFileMappingW(file_, nullptr, PAGE_READONLY, 0, 0, nullptr);
    if (!mapping_) return LastError();
    data_ = static_cast<const FrameHeader*>(MapViewOfFile(mapping_, FILE_MAP_READ, 0, 0, sizeof(FrameHeader) + PixelBytes));
    if (!data_) return LastError();
    try { lastFrame_.resize(PixelBytes); } catch (...) { return E_OUTOFMEMORY; }
    lastTick_ = 0;
    return S_OK;
}
bool FrameReader::Read(BYTE* pixels, DWORD size) {
    if (!pixels || size != PixelBytes || !data_ || data_->magic != Magic || data_->version != 1 || data_->bytes != PixelBytes || data_->width != CameraWidth || data_->height != CameraHeight) return false;
    for (int attempt = 0; attempt < 3; ++attempt) {
        const LONG before = data_->sequence; MemoryBarrier();
        if (before & 1) continue;
        const auto tick = data_->tick;
        if (!tick || GetTickCount64() - tick > 750) { lastTick_ = 0; return false; } // Never expose a stale phone picture.
        CopyMemory(pixels, data_ + 1, size); MemoryBarrier();
        if (before == data_->sequence) {
            CopyMemory(lastFrame_.data(), pixels, size); lastTick_ = tick;
            return true;
        }
    }
    // A writer holds an odd sequence while copying/scaling a new frame. That is
    // contention, not camera-off: keep the last complete frame for this sample.
    // Clear/disconnect sets tick to zero and must invalidate the cache immediately.
    const auto tick = data_->tick;
    if (tick && lastTick_ && GetTickCount64() - tick <= 750 && GetTickCount64() - lastTick_ <= 750) {
        CopyMemory(pixels, lastFrame_.data(), size);
        return true;
    }
    lastTick_ = 0;
    return false;
}
