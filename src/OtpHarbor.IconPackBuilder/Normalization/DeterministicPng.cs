using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using SkiaSharp;

namespace OtpHarbor.IconPackBuilder.Normalization;

internal static class DeterministicPng
{
    public const int MaximumEncodedBytes = 1024 * 1024;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] EncodeRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || checked(width * height * 4) != rgba.Length)
            throw new InputValidationException("PNG encoder received invalid RGBA dimensions.");

        var scanlines = new byte[checked(height * (1 + width * 4))];
        for (var y = 0; y < height; y++)
            rgba.Slice(y * width * 4, width * 4).CopyTo(scanlines.AsSpan(y * (1 + width * 4) + 1));

        var zlib = EncodeFixedHuffmanZlib(scanlines);
        using var output = new MemoryStream(Signature.Length + zlib.Length + 64);
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        WriteChunk(output, "IHDR"u8, ihdr);
        WriteChunk(output, "IDAT"u8, zlib);
        WriteChunk(output, "IEND"u8, []);
        var bytes = output.ToArray();
        if (bytes.Length > MaximumEncodedBytes)
            throw new InputValidationException("Deterministic PNG exceeds the 1 MiB runtime-asset limit.");
        return bytes;
    }

    public static SKBitmap DecodeAndValidate(
        byte[] png,
        int expectedWidth,
        int expectedHeight,
        string path,
        string? backgroundColor = null)
    {
        if (png.Length is 0 or > MaximumEncodedBytes)
            throw new InputValidationException($"Runtime PNG '{path}' is empty or exceeds the 1 MiB limit.");
        ValidateStructure(png, expectedWidth, expectedHeight, path);
        using var stream = new MemoryStream(png, writable: false);
        using var codec = SKCodec.Create(stream)
            ?? throw new InputValidationException($"Runtime PNG '{path}' could not be decoded.");
        if (codec.Info.Width != expectedWidth || codec.Info.Height != expectedHeight)
            throw new InputValidationException($"Runtime PNG '{path}' decoded dimensions do not match its manifest.");
        if (checked(codec.Info.Width * codec.Info.Height * 4) > RuntimeIconRenderer.MaximumDecodedBytes)
            throw new InputValidationException($"Runtime PNG '{path}' exceeds the decoded-pixel budget.");
        var bitmap = new SKBitmap(expectedWidth, expectedHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InputValidationException($"Runtime PNG '{path}' could not be fully decoded ({result}).");
        }
        if (result == SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            throw new InputValidationException($"Runtime PNG '{path}' is truncated.");
        }
        var visiblePixels = 0;
        var separatedPixels = 0;
        var background = backgroundColor is null ? default : ParseColor(backgroundColor, path);
        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Alpha == 0) continue;
            visiblePixels++;
            if (backgroundColor is null) continue;
            var alpha = pixel.Alpha / 255d;
            var red = (byte)Math.Round(pixel.Red * alpha + background.Red * (1 - alpha));
            var green = (byte)Math.Round(pixel.Green * alpha + background.Green * (1 - alpha));
            var blue = (byte)Math.Round(pixel.Blue * alpha + background.Blue * (1 - alpha));
            if (red != background.Red || green != background.Green || blue != background.Blue)
                separatedPixels++;
        }
        if (visiblePixels == 0)
        {
            bitmap.Dispose();
            throw new InputValidationException($"Runtime PNG '{path}' is blank.");
        }
        if (backgroundColor is not null && separatedPixels == 0)
        {
            bitmap.Dispose();
            throw new InputValidationException($"Runtime PNG '{path}' is fully covered by backgroundColor '{backgroundColor}'.");
        }
        return bitmap;
    }

    public static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static void ValidateStructure(byte[] png, int expectedWidth, int expectedHeight, string path)
    {
        if (!png.AsSpan().StartsWith(Signature))
            throw new InputValidationException($"Runtime PNG '{path}' has an invalid signature.");
        var offset = Signature.Length;
        var chunks = new List<string>();
        var sawIdat = false;
        using var imageData = new MemoryStream();
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
                throw new InputValidationException($"Runtime PNG '{path}' has a truncated chunk.");
            var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            if (unsignedLength > MaximumEncodedBytes)
                throw new InputValidationException($"Runtime PNG '{path}' has an invalid chunk length.");
            var length = (int)unsignedLength;
            if (png.Length - offset - 12 < length)
                throw new InputValidationException($"Runtime PNG '{path}' has an invalid chunk length.");
            var type = png.AsSpan(offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));
            if (Crc32(type, data) != expectedCrc)
                throw new InputValidationException($"Runtime PNG '{path}' has a corrupt chunk CRC.");
            var name = System.Text.Encoding.ASCII.GetString(type);
            chunks.Add(name);
            if (name == "IHDR")
            {
                if (chunks.Count != 1 || length != 13
                    || BinaryPrimitives.ReadUInt32BigEndian(data) != expectedWidth
                    || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != expectedHeight
                    || data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InputValidationException($"Runtime PNG '{path}' has an unsupported IHDR profile.");
            }
            else if (name == "IDAT")
            {
                if (chunks.Count == 1 || chunks[^2] is not ("IHDR" or "IDAT"))
                    throw new InputValidationException($"Runtime PNG '{path}' has non-contiguous image data.");
                sawIdat = true;
                imageData.Write(data);
            }
            else if (name == "IEND")
            {
                if (!sawIdat || length != 0 || offset + 12 != png.Length)
                    throw new InputValidationException($"Runtime PNG '{path}' has an invalid end chunk.");
                break;
            }
            else
            {
                throw new InputValidationException($"Runtime PNG '{path}' contains forbidden metadata or chunk '{name}'.");
            }
            offset += 12 + length;
        }
        if (chunks.Count < 3 || chunks[0] != "IHDR" || chunks[^1] != "IEND")
            throw new InputValidationException($"Runtime PNG '{path}' has an incomplete structure.");
        ValidateScanlines(imageData.ToArray(), expectedWidth, expectedHeight, path);
    }

    private static void ValidateScanlines(byte[] zlib, int width, int height, string path)
    {
        var expectedLength = checked(height * (1 + width * 4));
        var scanlines = new byte[expectedLength + 1];
        var total = 0;
        try
        {
            using var input = new MemoryStream(zlib, writable: false);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            while (total < scanlines.Length)
            {
                var read = inflater.Read(scanlines, total, scanlines.Length - total);
                if (read == 0) break;
                total += read;
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InputValidationException($"Runtime PNG '{path}' has invalid zlib/DEFLATE image data: {ex.Message}");
        }
        if (total != expectedLength)
            throw new InputValidationException($"Runtime PNG '{path}' has an invalid decoded scanline length.");
        var stride = 1 + width * 4;
        for (var y = 0; y < height; y++)
            if (scanlines[y * stride] != 0)
                throw new InputValidationException($"Runtime PNG '{path}' uses unsupported scanline filter type {scanlines[y * stride]}.");
    }

    private static SKColor ParseColor(string value, string path)
    {
        if (value.Length != 7 || value[0] != '#')
            throw new InputValidationException($"Runtime PNG '{path}' received invalid backgroundColor '{value}'.");
        try
        {
            return new SKColor(
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16));
        }
        catch (FormatException)
        {
            throw new InputValidationException($"Runtime PNG '{path}' received invalid backgroundColor '{value}'.");
        }
    }

    private static byte[] EncodeFixedHuffmanZlib(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(Math.Max(256, data.Length / 4));
        output.WriteByte(0x78);
        output.WriteByte(0x01);
        var bits = new DeflateBitWriter(output);
        bits.WriteBits(1, 1); // BFINAL
        bits.WriteBits(1, 2); // BTYPE=01, fixed Huffman codes

        var head = ArrayPool<int>.Shared.Rent(1 << 16);
        try
        {
            head.AsSpan(0, 1 << 16).Fill(-1);
            var position = 0;
            while (position < data.Length)
            {
                var bestLength = 0;
                var bestDistance = 0;
                if (position + 2 < data.Length)
                {
                    var candidate = head[Hash(data, position)];
                    if (candidate >= 0 && position - candidate <= 32768)
                    {
                        var maximumLength = Math.Min(258, data.Length - position);
                        var length = 0;
                        while (length < maximumLength && data[candidate + length] == data[position + length]) length++;
                        if (length >= 3)
                        {
                            bestLength = length;
                            bestDistance = position - candidate;
                        }
                    }
                }

                InsertHash(data, position, head);
                if (bestLength >= 3)
                {
                    WriteLengthDistance(bits, bestLength, bestDistance);
                    for (var index = 1; index < bestLength; index++)
                        InsertHash(data, position + index, head);
                    position += bestLength;
                }
                else
                {
                    WriteFixedSymbol(bits, data[position]);
                    position++;
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(head);
        }
        WriteFixedSymbol(bits, 256);
        bits.Flush();
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, Adler32(data));
        output.Write(checksum);
        return output.ToArray();
    }

    private static void InsertHash(ReadOnlySpan<byte> data, int position, int[] head)
    {
        if (position + 2 >= data.Length) return;
        var hash = Hash(data, position);
        head[hash] = position;
    }

    private static int Hash(ReadOnlySpan<byte> data, int position)
        => ((data[position] * 251 + data[position + 1]) * 251 + data[position + 2]) & 0xffff;

    private static void WriteLengthDistance(DeflateBitWriter writer, int length, int distance)
    {
        var lengthIndex = Array.FindLastIndex(LengthBases, value => value <= length);
        WriteFixedSymbol(writer, 257 + lengthIndex);
        var lengthExtraBits = LengthExtraBits[lengthIndex];
        if (lengthExtraBits != 0)
            writer.WriteBits((uint)(length - LengthBases[lengthIndex]), lengthExtraBits);

        var distanceIndex = Array.FindLastIndex(DistanceBases, value => value <= distance);
        writer.WriteBits(ReverseBits((uint)distanceIndex, 5), 5);
        var distanceExtraBits = DistanceExtraBits[distanceIndex];
        if (distanceExtraBits != 0)
            writer.WriteBits((uint)(distance - DistanceBases[distanceIndex]), distanceExtraBits);
    }

    private static void WriteFixedSymbol(DeflateBitWriter writer, int symbol)
    {
        var (code, bitCount) = symbol switch
        {
            <= 143 => ((uint)(0x30 + symbol), 8),
            <= 255 => ((uint)(0x190 + symbol - 144), 9),
            <= 279 => ((uint)(symbol - 256), 7),
            _ => ((uint)(0xc0 + symbol - 280), 8)
        };
        writer.WriteBits(ReverseBits(code, bitCount), bitCount);
    }

    private static uint ReverseBits(uint value, int bitCount)
    {
        uint reversed = 0;
        for (var bit = 0; bit < bitCount; bit++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }
        return reversed;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length);
        output.Write(value);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(value, Crc32(type, data));
        output.Write(value);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint modulus = 65521;
        // RFC 1950 permits reducing modulo in bounded chunks. Avoiding one
        // integer division per RGBA byte materially reduces full-catalog PNG
        // encoding time while producing exactly the same checksum.
        const int maximumChunkLength = 5552;
        uint a = 1, b = 0;
        var offset = 0;
        while (offset < data.Length)
        {
            var length = Math.Min(maximumChunkLength, data.Length - offset);
            var end = offset + length;
            while (offset < end)
            {
                a += data[offset++];
                b += a;
            }
            a %= modulus;
            b %= modulus;
        }
        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in type) crc = UpdateCrc(crc, value);
        foreach (var value in data) crc = UpdateCrc(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        return crc;
    }

    private static readonly int[] LengthBases =
    [
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115,
        131, 163, 195, 227, 258
    ];

    private static readonly int[] LengthExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
    ];

    private static readonly int[] DistanceBases =
    [
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049,
        3073, 4097, 6145, 8193, 12289, 16385, 24577
    ];

    private static readonly int[] DistanceExtraBits =
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
    ];

    private sealed class DeflateBitWriter(Stream output)
    {
        private uint _buffer;
        private int _bitCount;

        public void WriteBits(uint value, int count)
        {
            _buffer |= value << _bitCount;
            _bitCount += count;
            while (_bitCount >= 8)
            {
                output.WriteByte((byte)_buffer);
                _buffer >>= 8;
                _bitCount -= 8;
            }
        }

        public void Flush()
        {
            if (_bitCount > 0) output.WriteByte((byte)_buffer);
            _buffer = 0;
            _bitCount = 0;
        }
    }
}
