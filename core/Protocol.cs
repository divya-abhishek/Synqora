using System.Buffers.Binary;
using System.Text.Json;

namespace Synqora.Core;

public enum FrameKind : byte { Hello = 1, Welcome = 2, Command = 3, Audio = 4, Video = 5, Ping = 6, Pong = 7, State = 8, OrientedVideo = 9, Companion = 10, FileChunk = 11, FileEnd = 12 }
public sealed record Frame(FrameKind Kind, byte[] Payload);
public static class Wire
{
    public const int MaxFrame = 2 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    public static async Task<Frame> Read(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[5]; await stream.ReadExactlyAsync(header, ct);
        var kind = (FrameKind)header[0];
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Unknown frame kind.");
        var size = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        var limit = kind switch { FrameKind.Audio => 16384, FrameKind.FileChunk => 65536, FrameKind.Companion => 65536, FrameKind.Video or FrameKind.OrientedVideo => MaxFrame, _ => 16384 };
        if (size < 0 || size > limit) throw new InvalidDataException("Frame exceeds channel limit.");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, ct); return new(kind, bytes);
    }
    public static async Task Write(Stream stream, FrameKind kind, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > MaxFrame) throw new InvalidDataException("Frame too large.");
        byte[] packet = new byte[payload.Length + 5]; packet[0] = (byte)kind; BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(1), payload.Length);
        payload.CopyTo(packet.AsMemory(5));
        await stream.WriteAsync(packet, ct); await stream.FlushAsync(ct);
    }
    public static bool ValidJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
        int at = 2;
        while (at + 4 <= bytes.Length)
        {
            if (bytes[at++] != 0xff) continue;
            byte marker = bytes[at++];
            if (marker is 0xd8 or 0xd9) continue;
            if (marker == 0xda) return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));
            if (length < 2 || at + length > bytes.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) return false;
                int height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 5, 2));
                return width > 0 && height > 0 && width <= 1920 && height <= 1920 && width * height <= 1920 * 1080;
            }
            at += length;
        }
        return false;
    }
}
