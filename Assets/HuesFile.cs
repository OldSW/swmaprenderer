using System.Runtime.InteropServices;

namespace SwMapRenderer.Assets;

/// <summary>
/// hues.mul: 375 groups of 8 hues. Each hue is a 32-entry gradient that replaces a sprite's
/// grayscale ramp. CentrED uploads these as a 512x1024 texture and samples it in the shader;
/// the CPU path indexes the table directly, which is the same lookup without the texture hop.
/// </summary>
public sealed class HuesFile
{
    public const int ColorsPerHue = 32;

    private readonly uint[] _colors;   // HueCount * ColorsPerHue, RGBA
    private readonly string[] _names;

    public int HueCount { get; }

    public HuesFile(string path)
    {
        var raw = File.ReadAllBytes(path);

        // group: int32 header + 8 * (32 * uint16 table + uint16 start + uint16 end + char[20] name)
        const int entrySize = ColorsPerHue * 2 + 2 + 2 + 20;
        const int groupSize = 4 + 8 * entrySize;

        int groups = raw.Length / groupSize;
        HueCount = groups * 8;

        _colors = new uint[HueCount * ColorsPerHue];
        _names = new string[HueCount];

        for (int g = 0; g < groups; g++)
        {
            int groupOffset = g * groupSize + 4;
            for (int e = 0; e < 8; e++)
            {
                int offset = groupOffset + e * entrySize;
                int hue = g * 8 + e;

                var table = MemoryMarshal.Cast<byte, ushort>(raw.AsSpan(offset, ColorsPerHue * 2));
                for (int i = 0; i < ColorsPerHue; i++)
                {
                    // Hue table entries carry the A1R5G5B5 high bit set; it is not alpha here.
                    _colors[hue * ColorsPerHue + i] = Color16.ToRgbaOpaque((ushort)(table[i] & 0x7FFF));
                }

                _names[hue] = ReadName(raw.AsSpan(offset + ColorsPerHue * 2 + 4, 20));
            }
        }
    }

    private static string ReadName(ReadOnlySpan<byte> span)
    {
        int end = span.IndexOf((byte)0);
        if (end < 0) end = span.Length;
        return System.Text.Encoding.ASCII.GetString(span[..end]);
    }

    /// <param name="hue">Zero-based hue index (i.e. the stored tile hue minus one).</param>
    /// <param name="index">Position in the 32-step gradient.</param>
    public uint GetColor(int hue, int index)
    {
        if (hue < 0 || hue >= HueCount)
            return 0xFF000000;

        if (index < 0) index = 0; else if (index >= ColorsPerHue) index = ColorsPerHue - 1;
        return _colors[hue * ColorsPerHue + index];
    }

    public string GetName(int hue) => hue >= 0 && hue < HueCount ? _names[hue] : "Out Of Range";
}
