#define SYNQORA_CAMERA_EXPORTS
#include "Camera.h"
#include <mfapi.h>
#include <mferror.h>
#include <mfvirtualcamera.h>
#include <wrl.h>
#include <wrl/module.h>
#include <memory>
#include <mutex>
#include <condition_variable>
#include <thread>
#include <string>
#include <cstdio>

using namespace Microsoft::WRL;
HRESULT MakeSynqoraSource(IMFAttributes*, IMFMediaSource**);
#define FORWARD(name, params, args) HRESULT STDMETHODCALLTYPE name params override { return attributes_->name args; }

namespace {
class Activation : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ChainInterfaces<IMFActivate, IMFAttributes>, FtmBase> {
    ComPtr<IMFAttributes> attributes_;
    ComPtr<IMFMediaSource> source_;
    std::mutex gate_;
public:
    HRESULT Init() { return MFCreateAttributes(&attributes_, 8); }
    HRESULT STDMETHODCALLTYPE ActivateObject(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        std::lock_guard lock(gate_);
        if (!source_) { const HRESULT hr = MakeSynqoraSource(attributes_.Get(), &source_); if (FAILED(hr)) return hr; }
        return source_->QueryInterface(iid, result);
    }
    HRESULT STDMETHODCALLTYPE ShutdownObject() override { std::lock_guard lock(gate_); if (source_) source_->Shutdown(); source_.Reset(); return S_OK; }
    HRESULT STDMETHODCALLTYPE DetachObject() override { std::lock_guard lock(gate_); source_.Reset(); return S_OK; }
    // Frame Server releases this activation object after taking ownership of the
    // returned source. Releasing the activator must not shut down that live source.
    ~Activation() = default;
    FORWARD(GetItem, (REFGUID k, PROPVARIANT* v), (k,v))
    FORWARD(GetItemType, (REFGUID k, MF_ATTRIBUTE_TYPE* t), (k,t))
    FORWARD(CompareItem, (REFGUID k, REFPROPVARIANT v, BOOL* r), (k,v,r))
    FORWARD(Compare, (IMFAttributes* a, MF_ATTRIBUTES_MATCH_TYPE t, BOOL* r), (a,t,r))
    FORWARD(GetUINT32, (REFGUID k, UINT32* v), (k,v))
    FORWARD(GetUINT64, (REFGUID k, UINT64* v), (k,v))
    FORWARD(GetDouble, (REFGUID k, double* v), (k,v))
    FORWARD(GetGUID, (REFGUID k, GUID* v), (k,v))
    FORWARD(GetStringLength, (REFGUID k, UINT32* v), (k,v))
    FORWARD(GetString, (REFGUID k, LPWSTR v, UINT32 n, UINT32* c), (k,v,n,c))
    FORWARD(GetAllocatedString, (REFGUID k, LPWSTR* v, UINT32* n), (k,v,n))
    FORWARD(GetBlobSize, (REFGUID k, UINT32* n), (k,n))
    FORWARD(GetBlob, (REFGUID k, UINT8* v, UINT32 n, UINT32* c), (k,v,n,c))
    FORWARD(GetAllocatedBlob, (REFGUID k, UINT8** v, UINT32* n), (k,v,n))
    FORWARD(GetUnknown, (REFGUID k, REFIID i, LPVOID* v), (k,i,v))
    FORWARD(SetItem, (REFGUID k, REFPROPVARIANT v), (k,v))
    FORWARD(DeleteItem, (REFGUID k), (k))
    FORWARD(DeleteAllItems, (), ())
    FORWARD(SetUINT32, (REFGUID k, UINT32 v), (k,v))
    FORWARD(SetUINT64, (REFGUID k, UINT64 v), (k,v))
    FORWARD(SetDouble, (REFGUID k, double v), (k,v))
    FORWARD(SetGUID, (REFGUID k, REFGUID v), (k,v))
    FORWARD(SetString, (REFGUID k, LPCWSTR v), (k,v))
    FORWARD(SetBlob, (REFGUID k, const UINT8* v, UINT32 n), (k,v,n))
    FORWARD(SetUnknown, (REFGUID k, IUnknown* v), (k,v))
    FORWARD(LockStore, (), ())
    FORWARD(UnlockStore, (), ())
    FORWARD(GetCount, (UINT32* n), (n))
    FORWARD(GetItemByIndex, (UINT32 i, GUID* k, PROPVARIANT* v), (i,k,v))
    FORWARD(CopyAllItems, (IMFAttributes* a), (a))
};

class Factory : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IClassFactory> {
public:
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto activation = Make<Activation>(); if (!activation) return E_OUTOFMEMORY;
        const HRESULT hr = activation->Init(); if (FAILED(hr)) return hr;
        return activation.CopyTo(iid, result);
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) Module<InProc>::GetModule().IncrementObjectCount(); else Module<InProc>::GetModule().DecrementObjectCount(); return S_OK; }
};
struct CameraHandle {
    std::thread worker;
    std::mutex gate;
    std::condition_variable changed;
    bool ready = false, stopping = false;
    HRESULT status = E_PENDING;
    void Run(std::wstring path) {
        // Creation/consent and teardown stay off the UI thread, in the same COM apartment.
        const HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        HRESULT hr = apartment;
        bool mediaStarted = false;
        HMODULE module = nullptr;
        ComPtr<IMFVirtualCamera> camera;
        if (SUCCEEDED(hr)) { hr = MFStartup(MF_VERSION); mediaStarted = SUCCEEDED(hr); }
        if (SUCCEEDED(hr)) {
            module = LoadLibraryExW(L"mfsensorgroup.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            const auto create = module ? reinterpret_cast<decltype(&MFCreateVirtualCamera)>(GetProcAddress(module, "MFCreateVirtualCamera")) : nullptr;
            if (!create) hr = HRESULT_FROM_WIN32(ERROR_OLD_WIN_VERSION);
            else {
                WCHAR identity[40]{}; StringFromGUID2(CLSID_SynqoraCamera, identity, ARRAYSIZE(identity));
                hr = create(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_Session, MFVirtualCameraAccess_CurrentUser, L"Synqora Camera", identity, nullptr, 0, &camera);
                if (FAILED(hr)) std::fprintf(stderr, "Virtual camera creation failed: 0x%08lx\n", static_cast<unsigned long>(hr));
                if (SUCCEEDED(hr)) { hr = camera->SetString(SynqoraFrameFile, path.c_str()); if (FAILED(hr)) std::fprintf(stderr, "Virtual camera attributes failed: 0x%08lx\n", static_cast<unsigned long>(hr)); }
                if (SUCCEEDED(hr)) { hr = camera->Start(nullptr); if (FAILED(hr)) std::fprintf(stderr, "Virtual camera start failed: 0x%08lx\n", static_cast<unsigned long>(hr)); }
            }
        }
        {
            std::unique_lock lock(gate);
            status = hr; ready = true; changed.notify_all();
            if (SUCCEEDED(hr)) changed.wait(lock, [&] { return stopping; });
        }
        if (camera) { camera->Stop(); camera->Remove(); camera->Shutdown(); camera.Reset(); }
        if (module) FreeLibrary(module);
        if (mediaStarted) MFShutdown();
        if (SUCCEEDED(apartment)) CoUninitialize();
    }
    ~CameraHandle() {
        { std::lock_guard lock(gate); stopping = true; changed.notify_all(); }
        if (worker.joinable()) worker.join();
    }
};
}

HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    if (!result) return E_POINTER; *result = nullptr;
    if (clsid != CLSID_SynqoraCamera) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = Make<Factory>(); return factory ? factory.CopyTo(iid, result) : E_OUTOFMEMORY;
}
HRESULT __stdcall DllCanUnloadNow() { return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE; }

HRESULT __stdcall SynqoraCameraStart(LPCWSTR path, void** result) {
    if (!path || !result) return E_POINTER; *result = nullptr;
    try {
        auto handle = std::make_unique<CameraHandle>();
        handle->worker = std::thread(&CameraHandle::Run, handle.get(), std::wstring(path));
        std::unique_lock lock(handle->gate);
        handle->changed.wait(lock, [&] { return handle->ready; });
        const HRESULT hr = handle->status; lock.unlock();
        if (FAILED(hr)) return hr;
        *result = handle.release(); return S_OK;
    } catch (...) { return E_OUTOFMEMORY; }
}
void __stdcall SynqoraCameraStop(void* camera) { delete static_cast<CameraHandle*>(camera); }
