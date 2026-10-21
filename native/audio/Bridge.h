#pragma once
#include <ntddk.h>
NTSTATUS SynqoraAudioInitialize(PDRIVER_OBJECT driver);
void SynqoraAudioShutdown();
void SynqoraAudioRead(UCHAR* output, ULONG bytes, ULONGLONG& cursor);
