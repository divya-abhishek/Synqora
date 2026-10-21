#define SYNQORA_CAMERA_EXPORTS
#include "Camera.h"
#include <mfapi.h>
#include <mferror.h>
#include <mfreadwrite.h>
#include <ks.h>
#include <ksmedia.h>
#include <ksproxy.h>
#include <wrl.h>
#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <vector>

using namespace Microsoft::WRL;
#define CHECK(call) do { const HRESULT hr_ = (call); if (FAILED(hr_)) return hr_; } while (false)

// IMFMediaEventQueue handles asynchronous clients. Never keep a state lock during GetEvent.
#define EVENT_METHODS \
 HRESULT STDMETHODCALLTYPE GetEvent(DWORD f, IMFMediaEvent** e) override { auto q = Events(); return q ? q->GetEvent(f,e) : MF_E_SHUTDOWN; } \
 HRESULT STDMETHODCALLTYPE BeginGetEvent(IMFAsyncCallback* c, IUnknown* s) override { auto q = Events(); return q ? q->BeginGetEvent(c,s) : MF_E_SHUTDOWN; } \
 HRESULT STDMETHODCALLTYPE EndGetEvent(IMFAsyncResult* r, IMFMediaEvent** e) override { auto q = Events(); return q ? q->EndGetEvent(r,e) : MF_E_SHUTDOWN; } \
 HRESULT STDMETHODCALLTYPE QueueEvent(MediaEventType t, REFGUID g, HRESULT h, const PROPVARIANT* v) override { auto q = Events(); return q ? q->QueueEventParamVar(t,g,h,v) : MF_E_SHUTDOWN; }

namespace {
constexpr LONGLONG Duration = 10'000'000 / CameraFps;
HRESULT StreamAttributes(IMFAttributes* attributes) {
    CHECK(attributes->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY, PINNAME_VIDEO_CAPTURE));
    CHECK(attributes->SetUINT32(MF_DEVICESTREAM_STREAM_ID, 0));
    CHECK(attributes->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED, 1));
    return attributes->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES, MFFrameSourceTypes_Color);
}
HRESULT MediaType(REFGUID subtype, IMFMediaType** result, UINT32 fps = CameraFps) {
    ComPtr<IMFMediaType> type; CHECK(MFCreateMediaType(&type));
    CHECK(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video)); CHECK(type->SetGUID(MF_MT_SUBTYPE, subtype));
    CHECK(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    CHECK(type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE));
    CHECK(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, CameraWidth, CameraHeight));
    CHECK(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, fps, 1));
    CHECK(MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
    return type.CopyTo(result);
}
BYTE Clip(int value) { return BYTE(std::clamp(value, 0, 255)); }

class Stream final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ChainInterfaces<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>, IMFAsyncCallback, FtmBase> {
    std::mutex gate_;
    std::condition_variable changed_;
    ComPtr<IMFMediaSource> parent_;
    ComPtr<IMFMediaEventQueue> events_;
    ComPtr<IMFStreamDescriptor> descriptor_;
    ComPtr<IMFAttributes> attributes_;
    ComPtr<IMFVideoSampleAllocator> allocator_;
    std::deque<ComPtr<IUnknown>> requests_;
    DWORD queue_ = 0;
    MF_STREAM_STATE state_ = MF_STREAM_STATE_STOPPED;
    bool shutdown_ = false, nv12_ = true;
    FrameReader frames_;
    std::vector<BYTE> pixels_;
    std::chrono::steady_clock::time_point next_;
    LONGLONG duration_ = Duration;
    ComPtr<IMFMediaEventQueue> Events() { std::lock_guard lock(gate_); return events_; }
public:
    HRESULT Init(IMFMediaSource* parent, LPCWSTR path) {
        parent_ = parent; pixels_.resize(PixelBytes);
        CHECK(frames_.Open(path)); CHECK(MFCreateEventQueue(&events_)); CHECK(MFCreateAttributes(&attributes_, 5));
        CHECK(StreamAttributes(attributes_.Get()));
        ComPtr<IMFMediaType> a, b, c, d;
        CHECK(MediaType(MFVideoFormat_NV12, &a, 30)); CHECK(MediaType(MFVideoFormat_NV12, &b, 60));
        CHECK(MediaType(MFVideoFormat_RGB32, &c, 30)); CHECK(MediaType(MFVideoFormat_RGB32, &d, 60));
        IMFMediaType* types[]{a.Get(), b.Get(), c.Get(), d.Get()}; CHECK(MFCreateStreamDescriptor(0, 4, types, &descriptor_));
        CHECK(StreamAttributes(descriptor_.Get()));
        ComPtr<IMFMediaTypeHandler> handler; CHECK(descriptor_->GetMediaTypeHandler(&handler)); CHECK(handler->SetCurrentMediaType(a.Get()));
        return MFAllocateSerialWorkQueue(MFASYNC_CALLBACK_QUEUE_MULTITHREADED, &queue_);
    }
    ~Stream() { Shutdown(); }
    EVENT_METHODS
    HRESULT STDMETHODCALLTYPE GetMediaSource(IMFMediaSource** source) override { if (!source) return E_POINTER; std::lock_guard lock(gate_); return shutdown_ ? MF_E_SHUTDOWN : parent_.CopyTo(source); }
    HRESULT STDMETHODCALLTYPE GetStreamDescriptor(IMFStreamDescriptor** descriptor) override { if (!descriptor) return E_POINTER; std::lock_guard lock(gate_); return shutdown_ ? MF_E_SHUTDOWN : descriptor_.CopyTo(descriptor); }
    HRESULT STDMETHODCALLTYPE GetStreamState(MF_STREAM_STATE* state) override { if (!state) return E_POINTER; std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN; *state = state_; return S_OK; }
    HRESULT STDMETHODCALLTYPE SetStreamState(MF_STREAM_STATE state) override {
        if (state == MF_STREAM_STATE_STOPPED) return Stop(false);
        if (state == MF_STREAM_STATE_RUNNING) { ComPtr<IMFMediaTypeHandler> handler; CHECK(descriptor_->GetMediaTypeHandler(&handler)); ComPtr<IMFMediaType> type; CHECK(handler->GetCurrentMediaType(&type)); return Start(type.Get(), false); }
        if (state != MF_STREAM_STATE_PAUSED) return E_INVALIDARG;
        std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN;
        state_ = state; requests_.clear(); changed_.notify_all(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE RequestSample(IUnknown* token) override {
        std::lock_guard lock(gate_);
        if (shutdown_) return MF_E_SHUTDOWN;
        if (state_ != MF_STREAM_STATE_RUNNING) return MF_E_INVALIDREQUEST;
        if (requests_.size() >= 4) return MF_E_NOTACCEPTING;
        try { requests_.emplace_back(token); } catch (...) { return E_OUTOFMEMORY; }
        const HRESULT hr = MFPutWorkItem2(queue_, 0, this, nullptr);
        if (FAILED(hr)) requests_.pop_back();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE GetParameters(DWORD*, DWORD*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Invoke(IMFAsyncResult*) override {
        std::unique_lock lock(gate_);
        if (shutdown_ || state_ != MF_STREAM_STATE_RUNNING || requests_.empty()) return S_OK;
        ComPtr<IUnknown> token = requests_.front(); requests_.pop_front();
        changed_.wait_until(lock, next_, [&] { return shutdown_ || state_ != MF_STREAM_STATE_RUNNING; });
        if (shutdown_ || state_ != MF_STREAM_STATE_RUNNING) return S_OK;
        next_ = std::chrono::steady_clock::now() + std::chrono::nanoseconds(duration_ * 100);
        ComPtr<IMFSample> sample;
        HRESULT hr = allocator_->AllocateSample(&sample);
        if (SUCCEEDED(hr)) hr = Fill(sample.Get());
        if (SUCCEEDED(hr)) hr = sample->SetSampleTime(MFGetSystemTime());
        if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(duration_);
        if (SUCCEEDED(hr) && token) hr = sample->SetUnknown(MFSampleExtension_Token, token.Get());
        if (SUCCEEDED(hr)) return events_->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get());
        events_->QueueEventParamVar(MEError, GUID_NULL, hr, nullptr);
        return S_OK;
    }
    HRESULT Fill(IMFSample* sample) {
        if (!frames_.Read(pixels_.data(), PixelBytes)) std::fill(pixels_.begin(), pixels_.end(), BYTE(0));
        ComPtr<IMFMediaBuffer> buffer; CHECK(sample->GetBufferByIndex(0, &buffer));
        ComPtr<IMF2DBuffer2> image; CHECK(buffer.As(&image));
        BYTE *scan = nullptr, *start = nullptr; LONG pitch = 0; DWORD length = 0;
        CHECK(image->Lock2DSize(MF2DBuffer_LockFlags_Write, &scan, &pitch, &start, &length));
        const UINT32 rowBytes = nv12_ ? CameraWidth : CameraWidth * 4;
        if (UINT32(abs(pitch)) < rowBytes || length < (nv12_ ? CameraWidth * CameraHeight * 3 / 2 : PixelBytes)) { image->Unlock2D(); return E_UNEXPECTED; }
        if (!nv12_) {
            for (UINT32 y = 0; y < CameraHeight; ++y) CopyMemory(scan + LONGLONG(y) * pitch, pixels_.data() + y * CameraWidth * 4, CameraWidth * 4);
        } else {
            for (UINT32 y = 0; y < CameraHeight; ++y) {
                auto output = scan + LONGLONG(y) * pitch;
                auto input = pixels_.data() + y * CameraWidth * 4;
                for (UINT32 x = 0; x < CameraWidth; ++x) { auto p = input + x * 4; output[x] = Clip(((66 * p[2] + 129 * p[1] + 25 * p[0] + 128) >> 8) + 16); }
            }
            BYTE* chroma = scan + LONGLONG(CameraHeight) * pitch;
            for (UINT32 y = 0; y < CameraHeight; y += 2) {
                auto output = chroma + LONGLONG(y / 2) * pitch;
                for (UINT32 x = 0; x < CameraWidth; x += 2) {
                    int r = 0, g = 0, b = 0;
                    for (UINT32 dy = 0; dy < 2; ++dy) for (UINT32 dx = 0; dx < 2; ++dx) { auto p = pixels_.data() + ((y + dy) * CameraWidth + x + dx) * 4; b += p[0]; g += p[1]; r += p[2]; }
                    r /= 4; g /= 4; b /= 4;
                    output[x] = Clip(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                    output[x + 1] = Clip(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
                }
            }
        }
        CHECK(image->Unlock2D());
        return buffer->SetCurrentLength(nv12_ ? CameraWidth * CameraHeight * 3 / 2 : PixelBytes);
    }
    HRESULT Start(IMFMediaType* type, bool notify) {
        std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN;
        GUID subtype{}; UINT32 width = 0, height = 0;
        CHECK(type->GetGUID(MF_MT_SUBTYPE, &subtype)); CHECK(MFGetAttributeSize(type, MF_MT_FRAME_SIZE, &width, &height));
        if (width != CameraWidth || height != CameraHeight || (subtype != MFVideoFormat_RGB32 && subtype != MFVideoFormat_NV12)) return MF_E_INVALIDMEDIATYPE;
        UINT32 numerator = 30, denominator = 1;
        CHECK(MFGetAttributeRatio(type, MF_MT_FRAME_RATE, &numerator, &denominator));
        if (!denominator || (numerator != 30 * denominator && numerator != 60 * denominator)) return MF_E_INVALIDMEDIATYPE;
        duration_ = 10'000'000LL * denominator / numerator;
        if (state_ == MF_STREAM_STATE_RUNNING) return S_OK;
        ComPtr<IMFMediaTypeHandler> handler; CHECK(descriptor_->GetMediaTypeHandler(&handler)); CHECK(handler->SetCurrentMediaType(type));
        if (!allocator_) CHECK(MFCreateVideoSampleAllocatorEx(IID_PPV_ARGS(&allocator_)));
        CHECK(allocator_->InitializeSampleAllocator(5, type));
        nv12_ = subtype == MFVideoFormat_NV12; state_ = MF_STREAM_STATE_RUNNING; next_ = std::chrono::steady_clock::now();
        return notify ? events_->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, nullptr) : S_OK;
    }
    HRESULT Stop(bool notify) {
        std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN;
        state_ = MF_STREAM_STATE_STOPPED; requests_.clear(); changed_.notify_all();
        if (allocator_) allocator_->UninitializeSampleAllocator();
        return notify ? events_->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr) : S_OK;
    }
    void Shutdown() {
        std::lock_guard lock(gate_); if (shutdown_) return;
        shutdown_ = true; changed_.notify_all(); requests_.clear();
        if (events_) events_->Shutdown(); events_.Reset();
        if (allocator_) allocator_->UninitializeSampleAllocator(); allocator_.Reset(); parent_.Reset();
        if (queue_) { MFUnlockWorkQueue(queue_); queue_ = 0; }
    }
    HRESULT Attributes(IMFAttributes** result) { std::lock_guard lock(gate_); return shutdown_ ? MF_E_SHUTDOWN : attributes_.CopyTo(result); }
};

class Source final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ChainInterfaces<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>, IMFGetService, IMFSampleAllocatorControl, IKsControl, FtmBase> {
    std::mutex gate_;
    ComPtr<IMFMediaEventQueue> events_;
    ComPtr<IMFAttributes> attributes_;
    ComPtr<IMFPresentationDescriptor> descriptor_;
    ComPtr<Stream> stream_;
    bool started_ = false, announced_ = false, shutdown_ = false;
    ComPtr<IMFMediaEventQueue> Events() { std::lock_guard lock(gate_); return events_; }
public:
    HRESULT Init(IMFAttributes* attributes) {
        CHECK(MFCreateAttributes(&attributes_, 8)); CHECK(attributes->CopyAllItems(attributes_.Get())); CHECK(MFCreateEventQueue(&events_));
        WCHAR path[2048]{}; CHECK(attributes_->GetString(SynqoraFrameFile, path, ARRAYSIZE(path), nullptr));
        stream_ = Make<Stream>(); if (!stream_) return E_OUTOFMEMORY;
        CHECK(stream_->Init(this, path));
        ComPtr<IMFStreamDescriptor> streamDescriptor; CHECK(stream_->GetStreamDescriptor(&streamDescriptor));
        IMFStreamDescriptor* streams[]{streamDescriptor.Get()}; CHECK(MFCreatePresentationDescriptor(1, streams, &descriptor_)); CHECK(descriptor_->SelectStream(0));
        ComPtr<IMFSensorProfileCollection> profiles; ComPtr<IMFSensorProfile> legacy;
        CHECK(MFCreateSensorProfileCollection(&profiles)); CHECK(MFCreateSensorProfile(KSCAMERAPROFILE_Legacy, 0, nullptr, &legacy));
        CHECK(legacy->AddProfileFilter(0, L"((RES==;FRT<=60,1;SUT==))")); CHECK(profiles->AddProfile(legacy.Get()));
        return attributes_->SetUnknown(MF_DEVICEMFT_SENSORPROFILE_COLLECTION, profiles.Get());
    }
    ~Source() { Shutdown(); }
    EVENT_METHODS
    HRESULT STDMETHODCALLTYPE GetCharacteristics(DWORD* output) override { if (!output) return E_POINTER; std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN; *output = MFMEDIASOURCE_IS_LIVE; return S_OK; }
    HRESULT STDMETHODCALLTYPE CreatePresentationDescriptor(IMFPresentationDescriptor** output) override { if (!output) return E_POINTER; std::lock_guard lock(gate_); return shutdown_ ? MF_E_SHUTDOWN : descriptor_->Clone(output); }
    HRESULT STDMETHODCALLTYPE Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat, const PROPVARIANT* position) override {
        if (!descriptor || !position) return E_POINTER;
        if (timeFormat && *timeFormat != GUID_NULL) return MF_E_UNSUPPORTED_TIME_FORMAT;
        if (position->vt != VT_EMPTY && (position->vt != VT_I8 || position->hVal.QuadPart != 0)) return MF_E_INVALIDREQUEST;
        std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN;
        DWORD count = 0; CHECK(descriptor->GetStreamDescriptorCount(&count)); if (count != 1) return E_INVALIDARG;
        BOOL selected = FALSE; ComPtr<IMFStreamDescriptor> streamDescriptor; CHECK(descriptor->GetStreamDescriptorByIndex(0, &selected, &streamDescriptor));
        DWORD id = 99; CHECK(streamDescriptor->GetStreamIdentifier(&id)); if (!selected || id != 0) return E_INVALIDARG;
        ComPtr<IMFMediaTypeHandler> handler; ComPtr<IMFMediaType> type; CHECK(streamDescriptor->GetMediaTypeHandler(&handler)); CHECK(handler->GetCurrentMediaType(&type));
        ComPtr<IUnknown> identity; CHECK(stream_.As(&identity));
        CHECK(events_->QueueEventParamUnk(announced_ ? MEUpdatedStream : MENewStream, GUID_NULL, S_OK, identity.Get()));
        CHECK(stream_->Start(type.Get(), true)); started_ = announced_ = true;
        return events_->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, position);
    }
    HRESULT STDMETHODCALLTYPE Stop() override { std::lock_guard lock(gate_); if (shutdown_) return MF_E_SHUTDOWN; CHECK(stream_->Stop(true)); started_ = false; return events_->QueueEventParamVar(MESourceStopped, GUID_NULL, S_OK, nullptr); }
    HRESULT STDMETHODCALLTYPE Pause() override { return MF_E_INVALID_STATE_TRANSITION; }
    HRESULT STDMETHODCALLTYPE Shutdown() override {
        std::lock_guard lock(gate_); if (shutdown_) return S_OK; shutdown_ = true;
        if (stream_) stream_->Shutdown(); stream_.Reset();
        if (events_) events_->Shutdown(); events_.Reset(); descriptor_.Reset(); attributes_.Reset(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSourceAttributes(IMFAttributes** output) override { if (!output) return E_POINTER; std::lock_guard lock(gate_); return shutdown_ ? MF_E_SHUTDOWN : attributes_.CopyTo(output); }
    HRESULT STDMETHODCALLTYPE GetStreamAttributes(DWORD id, IMFAttributes** output) override { if (id != 0) return MF_E_INVALIDSTREAMNUMBER; if (!output) return E_POINTER; std::lock_guard lock(gate_); return shutdown_ ? MF_E_SHUTDOWN : stream_->Attributes(output); }
    HRESULT STDMETHODCALLTYPE SetD3DManager(IUnknown*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetService(REFGUID, REFIID, void** result) override { if (!result) return E_POINTER; *result = nullptr; return MF_E_UNSUPPORTED_SERVICE; }
    HRESULT STDMETHODCALLTYPE SetDefaultAllocator(DWORD id, IUnknown*) override { return id == 0 ? S_OK : MF_E_INVALIDSTREAMNUMBER; }
    HRESULT STDMETHODCALLTYPE GetAllocatorUsage(DWORD id, DWORD* input, MFSampleAllocatorUsage* usage) override { if (!input || !usage) return E_POINTER; if (id != 0) return MF_E_INVALIDSTREAMNUMBER; *input = 0; *usage = MFSampleAllocatorUsage_UsesCustomAllocator; return S_OK; }
    HRESULT STDMETHODCALLTYPE KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG* bytes) override { if (bytes) *bytes = 0; return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
    HRESULT STDMETHODCALLTYPE KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG* bytes) override { if (bytes) *bytes = 0; return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
    HRESULT STDMETHODCALLTYPE KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG* bytes) override { if (bytes) *bytes = 0; return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
};
}

HRESULT MakeSynqoraSource(IMFAttributes* attributes, IMFMediaSource** result) {
    if (!attributes || !result) return E_POINTER; *result = nullptr;
    try {
        auto source = Make<Source>(); if (!source) return E_OUTOFMEMORY;
        const HRESULT hr = source->Init(attributes);
        if (FAILED(hr)) { source->Shutdown(); return hr; }
        const HRESULT queried = source.CopyTo(result);
        if (FAILED(queried)) source->Shutdown();
        return queried;
    } catch (...) { return E_OUTOFMEMORY; }
}
HRESULT __stdcall SynqoraCameraCreateSource(LPCWSTR path, IMFMediaSource** result) {
    if (!path || !result) return E_POINTER;
    ComPtr<IMFAttributes> attributes; CHECK(MFCreateAttributes(&attributes, 1)); CHECK(attributes->SetString(SynqoraFrameFile, path));
    return MakeSynqoraSource(attributes.Get(), result);
}
