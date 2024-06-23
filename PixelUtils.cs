namespace ArknightsWorkshop;

public static class PixelUtils
{
    public static byte[] Rgb565ToBgra32(ReadOnlySpan<byte> data)
    {
        if ((data.Length % 2) != 0) 
            throw new ArgumentException("Has invalid length for 2-byte pixels", nameof(data));
        var ret = new byte[data.Length * 2];
        for(var i = 0; i < data.Length / 2; i++)
        {
            var color = (ushort)((data[2 * i + 1] << 8) | data[2 * i]);
            ret[4 * i + 0] = (byte)((color & 0b0000000000011111) << 3); // B
            ret[4 * i + 1] = (byte)((color & 0b0000011111100000) >> 3); // G
            ret[4 * i + 2] = (byte)((color & 0b1111100000000000) >> 8); // R
            ret[4 * i + 3] = 255; // A
        }
        return ret;
    }
}