namespace HelixExplorer.Core.Theming;

public static class AccentColorDefaults
{
    public const uint Light = 0xFF0078D4;
    public const uint Dark = 0xFF60CDFF;

    public static uint Resolve(uint? customArgb, bool isDarkTheme)
        => customArgb ?? (isDarkTheme ? Dark : Light);

    public static (byte A, byte R, byte G, byte B) ToComponents(uint argb)
        => ((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static uint FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;

        return Convert.ToUInt32(hex, 16);
    }
}
