#define SYNQORA_CAMERA_EXPORTS
#include "Camera.h"
#include <mfapi.h>
#include <mfreadwrite.h>
#include <wrl.h>
#include <algorithm>
using Microsoft::WRL::ComPtr;

HRESULT __stdcall SynqoraCameraInspect(LPCWSTR path, SourceSampleInfo* info) {
    if (!path || !info) return E_POINTER;
    *info = {};
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(apartment)) return apartment;
    HRESULT hr = MFStartup(MF_VERSION);
    if (FAILED(hr)) { CoUninitialize(); return hr; }
    ComPtr<IMFMediaSource> source;
    ComPtr<IMFSourceReader> reader;
    hr = SynqoraCameraCreateSource(path, &source);
    if (SUCCEEDED(hr)) hr = MFCreateSourceReaderFromMediaSource(source.Get(), nullptr, &reader);
    ComPtr<IMFSample> sample;
    for (int i = 0; i < 8 && SUCCEEDED(hr) && !sample; ++i) {
        DWORD stream = 0, flags = 0;
        hr = reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &stream, &flags, &info->timestamp, &sample);
    }
    if (SUCCEEDED(hr) && !sample) hr = E_UNEXPECTED;
    if (SUCCEEDED(hr)) {
        ComPtr<IMFMediaBuffer> buffer; hr = sample->ConvertToContiguousBuffer(&buffer);
        BYTE* pixels = nullptr; DWORD bytes = 0;
        if (SUCCEEDED(hr)) hr = buffer->Lock(&pixels, nullptr, &bytes);
        if (SUCCEEDED(hr)) {
            if (bytes < CameraWidth * CameraHeight) hr = E_UNEXPECTED;
            else {
                const auto range = std::minmax_element(pixels, pixels + CameraWidth * CameraHeight);
                info->minimumLuma = *range.first; info->maximumLuma = *range.second;
                info->width = CameraWidth; info->height = CameraHeight; info->bytes = bytes;
                info->checksum = 14695981039346656037ULL;
                for (DWORD i = 0; i < bytes; ++i) info->checksum = (info->checksum ^ pixels[i]) * 1099511628211ULL;
            }
            buffer->Unlock();
        }
    }
    sample.Reset(); reader.Reset(); if (source) source->Shutdown(); source.Reset();
    MFShutdown(); CoUninitialize(); return hr;
}
