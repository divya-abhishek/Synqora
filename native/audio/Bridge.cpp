#include <ntddk.h>
#include <wdmsec.h>
#include "PcmRing.h"
#include "Bridge.h"

namespace {
// Original Synqora control identity; never install or remove another product's endpoint.
constexpr GUID ControlClass = {0x978ed562,0x9103,0x4688,{0xbc,0x73,0x41,0xe1,0xc6,0xdf,0x92,0x78}};
constexpr ULONG WritePcm = CTL_CODE(FILE_DEVICE_SOUND, 0x801, METHOD_BUFFERED, FILE_WRITE_DATA);
constexpr ULONG ClearPcm = CTL_CODE(FILE_DEVICE_SOUND, 0x802, METHOD_BUFFERED, FILE_WRITE_DATA);
PDEVICE_OBJECT control;
PDRIVER_DISPATCH previous[IRP_MJ_MAXIMUM_FUNCTION + 1]{};
UNICODE_STRING link = RTL_CONSTANT_STRING(L"\\DosDevices\\SynqoraMicrophone");
KSPIN_LOCK ringLock;
PcmRing ring;

NTSTATUS Dispatch(PDEVICE_OBJECT device, PIRP request) {
    const auto stack = IoGetCurrentIrpStackLocation(request);
    if (device != control) return previous[stack->MajorFunction](device, request);
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    KIRQL oldIrql;
    if (stack->MajorFunction == IRP_MJ_CREATE || stack->MajorFunction == IRP_MJ_CLOSE || stack->MajorFunction == IRP_MJ_CLEANUP) {
        KeAcquireSpinLock(&ringLock, &oldIrql); ring.Clear(); KeReleaseSpinLock(&ringLock, oldIrql);
        status = STATUS_SUCCESS;
    } else if (stack->MajorFunction == IRP_MJ_DEVICE_CONTROL) {
        const auto code = stack->Parameters.DeviceIoControl.IoControlCode;
        const ULONG bytes = stack->Parameters.DeviceIoControl.InputBufferLength;
        if (code == ClearPcm && bytes == 0) {
            KeAcquireSpinLock(&ringLock, &oldIrql); ring.Clear(); KeReleaseSpinLock(&ringLock, oldIrql);
            status = STATUS_SUCCESS;
        } else if (code == WritePcm) {
            if (!bytes || bytes > 8192 || bytes % sizeof(int16_t) || !request->AssociatedIrp.SystemBuffer) status = STATUS_INVALID_BUFFER_SIZE;
            else {
                KeAcquireSpinLock(&ringLock, &oldIrql);
                ring.Write(static_cast<const int16_t*>(request->AssociatedIrp.SystemBuffer), bytes / sizeof(int16_t), KeQueryInterruptTime());
                KeReleaseSpinLock(&ringLock, oldIrql); status = STATUS_SUCCESS;
            }
        }
    }
    request->IoStatus.Status = status; request->IoStatus.Information = 0;
    IoCompleteRequest(request, IO_NO_INCREMENT); return status;
}
}
NTSTATUS SynqoraAudioInitialize(PDRIVER_OBJECT driver) {
    KeInitializeSpinLock(&ringLock); ring.Clear();
    UNICODE_STRING name = RTL_CONSTANT_STRING(L"\\Device\\SynqoraMicrophone");
    // Interactive user can feed audio; exclusive handle prevents competing producers.
    UNICODE_STRING security = RTL_CONSTANT_STRING(L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)");
    NTSTATUS status = IoCreateDeviceSecure(driver, 0, &name, FILE_DEVICE_SOUND, FILE_DEVICE_SECURE_OPEN, TRUE, &security, &ControlClass, &control);
    if (!NT_SUCCESS(status)) return status;
    const UCHAR operations[]{IRP_MJ_CREATE, IRP_MJ_CLOSE, IRP_MJ_CLEANUP, IRP_MJ_DEVICE_CONTROL};
    for (auto operation : operations) { previous[operation] = driver->MajorFunction[operation]; driver->MajorFunction[operation] = Dispatch; }
    status = IoCreateSymbolicLink(&link, &name);
    if (!NT_SUCCESS(status)) {
        for (auto operation : operations) driver->MajorFunction[operation] = previous[operation];
        IoDeleteDevice(control); control = nullptr; return status;
    }
    control->Flags |= DO_BUFFERED_IO;
    control->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}
void SynqoraAudioShutdown() {
    IoDeleteSymbolicLink(&link);
    if (control) { IoDeleteDevice(control); control = nullptr; }
    ring.Clear();
}
void SynqoraAudioRead(UCHAR* output, ULONG bytes, ULONGLONG& cursor) {
    KIRQL oldIrql;
    KeAcquireSpinLock(&ringLock, &oldIrql);
    ring.Read(reinterpret_cast<int16_t*>(output), bytes / sizeof(int16_t), cursor, KeQueryInterruptTime());
    KeReleaseSpinLock(&ringLock, oldIrql);
    if (bytes % 2) output[bytes - 1] = 0;
}
