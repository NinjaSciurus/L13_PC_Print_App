namespace L13.Core.Bluetooth;

/// <summary>Helpers for parsing a Bluetooth MAC into the ulong Winsock expects.</summary>
public static class BluetoothAddress
{
    /// <summary>
    /// Accepts "55:55:09:22:3F:9B", "55-55-09-22-3F-9B" or "555509223F9B".
    /// </summary>
    public static ulong Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Bluetooth address is empty.", nameof(address));

        var hex = address.Replace(":", "").Replace("-", "").Replace(" ", "").Trim();
        if (hex.Length != 12)
            throw new FormatException($"Expected 12 hex digits, got '{address}'.");

        return Convert.ToUInt64(hex, 16);
    }
}
