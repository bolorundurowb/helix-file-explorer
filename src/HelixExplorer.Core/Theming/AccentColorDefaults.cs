namespace HelixExplorer.Core.Theming;

public readonly record struct SelectionTint(byte Alpha, bool Neutral);

public static class AccentColorDefaults
{
    /// <summary>Windows blue. Used when no custom swatch and no OS accent are available.</summary>
    public const uint Fallback = 0xFF0078D4;

    /// <summary>Black at this alpha on white composites to #F5F5F5.</summary>
    public const byte LightHoverAlpha = 0x0A;

    /// <summary>White lift on #1E1E1E that stays weaker than the selection tint.</summary>
    public const byte DarkHoverAlpha = 0x0C;

    public const byte LightNeutralSelectionAlpha = 0x20;
    public const byte DarkNeutralSelectionAlpha = 0x28;

    private const int MinAccentSelectionAlpha = 0x28;
    private const int MaxAccentSelectionAlpha = 0x70;

    public static uint Resolve(uint? customArgb, uint? systemAccentArgb)
        => customArgb ?? systemAccentArgb ?? Fallback;

    /// <summary>
    /// Windows stores DWM AccentColor as ABGR. A zero alpha byte means opaque, not transparent.
    /// An all-zero value is treated as missing.
    /// </summary>
    public static uint? FromAbgr(uint abgr)
    {
        if (abgr == 0)
            return null;

        var r = (byte)abgr;
        var g = (byte)(abgr >> 8);
        var b = (byte)(abgr >> 16);
        var a = (byte)(abgr >> 24);
        if (a == 0)
            a = 0xFF;

        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public static (byte A, byte R, byte G, byte B) ToComponents(uint argb)
        => ((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static uint FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;

        return Convert.ToUInt32(hex, 16);
    }

    /// <summary>
    /// Black or white, whichever has the higher contrast on <paramref name="backgroundArgb"/>.
    /// </summary>
    public static uint ForegroundFor(uint backgroundArgb)
    {
        var (_, r, g, b) = ToComponents(backgroundArgb);
        var luminance = RelativeLuminance(r, g, b);
        var black = (luminance + 0.05) / 0.05;
        var white = 1.05 / (luminance + 0.05);
        return black >= white ? 0xFF000000u : 0xFFFFFFFFu;
    }

    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        static double Channel(byte component)
        {
            var s = component / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    public static double ContrastRatio(uint firstArgb, uint secondArgb)
    {
        var (_, r1, g1, b1) = ToComponents(firstArgb);
        var (_, r2, g2, b2) = ToComponents(secondArgb);
        var lighter = Math.Max(RelativeLuminance(r1, g1, b1), RelativeLuminance(r2, g2, b2));
        var darker = Math.Min(RelativeLuminance(r1, g1, b1), RelativeLuminance(r2, g2, b2));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static (byte R, byte G, byte B) CompositeOver(
        byte backgroundR,
        byte backgroundG,
        byte backgroundB,
        byte foregroundR,
        byte foregroundG,
        byte foregroundB,
        byte foregroundAlpha)
    {
        byte Mix(byte back, byte fore)
            => (byte)((back * (255 - foregroundAlpha) + fore * foregroundAlpha + 127) / 255);

        return (Mix(backgroundR, foregroundR), Mix(backgroundG, foregroundG), Mix(backgroundB, foregroundB));
    }

    /// <summary>
    /// Accent tint that reads darker than hover on white, or a neutral darkening when the accent is too pale.
    /// On a dark surface the tint is a stronger separation from the background than hover.
    /// </summary>
    public static SelectionTint SelectionTintFor(uint accentArgb, bool isDark)
    {
        var (_, r, g, b) = ToComponents(accentArgb);
        if (isDark)
        {
            var backgroundL = RelativeLuminance(0x1E, 0x1E, 0x1E);
            var hover = CompositeOver(0x1E, 0x1E, 0x1E, 255, 255, 255, DarkHoverAlpha);
            var hoverDistance = Math.Abs(RelativeLuminance(hover.R, hover.G, hover.B) - backgroundL);
            for (var alpha = MinAccentSelectionAlpha; alpha <= MaxAccentSelectionAlpha; alpha++)
            {
                var tint = CompositeOver(0x1E, 0x1E, 0x1E, r, g, b, (byte)alpha);
                var distance = Math.Abs(RelativeLuminance(tint.R, tint.G, tint.B) - backgroundL);
                if (distance > hoverDistance + 0.02)
                    return new SelectionTint((byte)alpha, Neutral: false);
            }

            return new SelectionTint(DarkNeutralSelectionAlpha, Neutral: true);
        }

        var hoverLight = CompositeOver(255, 255, 255, 0, 0, 0, LightHoverAlpha);
        var hoverL = RelativeLuminance(hoverLight.R, hoverLight.G, hoverLight.B);
        for (var alpha = MinAccentSelectionAlpha; alpha <= MaxAccentSelectionAlpha; alpha++)
        {
            var tint = CompositeOver(255, 255, 255, r, g, b, (byte)alpha);
            if (RelativeLuminance(tint.R, tint.G, tint.B) < hoverL - 0.04)
                return new SelectionTint((byte)alpha, Neutral: false);
        }

        return new SelectionTint(LightNeutralSelectionAlpha, Neutral: true);
    }
}
