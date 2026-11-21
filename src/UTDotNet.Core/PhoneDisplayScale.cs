using System.Globalization;

namespace UTDotNet.Core;

public static class PhoneDisplayScale
{
    /// <summary>Lomiri's base grid is 8 pixels. Return an absolute Avalonia scale, avoiding double scaling.</summary>
    public static double? FromGridUnit(string? value, LinuxMobileEnvironment environment)
    {
        if (environment != LinuxMobileEnvironment.UbuntuTouch ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var grid) ||
            !double.IsFinite(grid) || grid < 8 || grid > 48) return null;
        return grid / 8;
    }
}
