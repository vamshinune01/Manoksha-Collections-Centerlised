using System.Security.Cryptography;

namespace Manoksha.SharedKernel;

/// <summary>
/// Server-generated, time-ordered UUID version 7 (RFC 9562). Used for every entity primary key so that
/// identifiers are immutable, never derived from business data, and index-friendly.
/// </summary>
public static class Uuid7
{
    private static readonly object Gate = new();
    private static long _lastMs;
    private static ushort _counter;

    public static Guid NewGuid() => NewGuid(DateTimeOffset.UtcNow);

    public static Guid NewGuid(DateTimeOffset timestamp)
    {
        long ms = timestamp.ToUnixTimeMilliseconds();
        ushort counter;
        lock (Gate)
        {
            if (ms <= _lastMs)
            {
                ms = _lastMs;
                _counter++;
                if (_counter > 0x0FFF)
                {
                    // Counter overflow within one millisecond: advance the logical clock.
                    ms = ++_lastMs;
                    _counter = 0;
                }
            }
            else
            {
                _lastMs = ms;
                _counter = (ushort)RandomNumberGenerator.GetInt32(0, 0x0400);
            }
            counter = _counter;
        }

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[8..]);
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        bytes[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));
        bytes[7] = (byte)counter;
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));
        return new Guid(bytes, bigEndian: true);
    }
}
