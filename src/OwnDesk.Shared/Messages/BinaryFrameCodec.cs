using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace OwnDesk.Shared.Messages;

public static class BinaryFrameCodec
{
    private const int PrefixLength = 8;
    private const uint Magic = 0x3146444F; // ODF1, little endian.

    public static byte[] Encode(BinaryFrameHeader header, byte[] imageBytes)
    {
        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header, JsonDefaults.Options);
        var payload = new byte[PrefixLength + headerJson.Length + imageBytes.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), headerJson.Length);
        headerJson.CopyTo(payload.AsSpan(PrefixLength));
        imageBytes.CopyTo(payload.AsSpan(PrefixLength + headerJson.Length));

        return payload;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out BinaryFrameHeader header, out ReadOnlySpan<byte> imageBytes)
    {
        header = default!;
        imageBytes = default;

        if (payload.Length < PrefixLength || BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]) != Magic)
        {
            return false;
        }

        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
        if (headerLength <= 0 || PrefixLength + headerLength >= payload.Length)
        {
            return false;
        }

        header = JsonSerializer.Deserialize<BinaryFrameHeader>(
            payload.Slice(PrefixLength, headerLength),
            JsonDefaults.Options) ?? throw new JsonException("Binary frame header is empty.");
        imageBytes = payload[(PrefixLength + headerLength)..];
        return true;
    }

    public static BinaryFrameHeader DecodeHeader(ReadOnlySpan<byte> payload)
    {
        if (!TryDecode(payload, out var header, out _))
        {
            throw new InvalidOperationException("Invalid OwnDesk binary frame.");
        }

        return header;
    }

    public static string DecodeHeaderJson(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < PrefixLength || BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]) != Magic)
        {
            throw new InvalidOperationException("Invalid OwnDesk binary frame magic.");
        }

        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
        if (headerLength <= 0 || PrefixLength + headerLength >= payload.Length)
        {
            throw new InvalidOperationException("Invalid OwnDesk binary frame header length.");
        }

        return Encoding.UTF8.GetString(payload.Slice(PrefixLength, headerLength));
    }
}

