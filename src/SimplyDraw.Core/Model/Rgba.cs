namespace SimplyDraw.Core.Model;

public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static Rgba Black { get; } = new(28, 25, 23, 255);

    public static Rgba Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Black;

        var text = hex.Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        try
        {
            return text.Length switch
            {
                3 => new Rgba(Expand(text[0]), Expand(text[1]), Expand(text[2]), 255),
                6 => new Rgba(ParseByte(text, 0), ParseByte(text, 2), ParseByte(text, 4), 255),
                8 => new Rgba(ParseByte(text, 0), ParseByte(text, 2), ParseByte(text, 4), ParseByte(text, 6)),
                _ => Black
            };
        }
        catch (FormatException)
        {
            return Black;
        }
    }

    public string ToHex() => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public static Rgba FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var match = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new Rgba(ToByte(red + match), ToByte(green + match), ToByte(blue + match), 255);
    }

    private static byte Expand(char nibble) => Convert.ToByte(new string(nibble, 2), 16);

    private static byte ParseByte(string text, int index) => Convert.ToByte(text.Substring(index, 2), 16);

    private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);
}
