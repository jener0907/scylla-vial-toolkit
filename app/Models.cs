using System.Text.Json.Serialization;

namespace ScyllaConfigurator;

public sealed class LayoutKey
{
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("col")] public int Col { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonIgnore] public ushort Keycode { get; set; }
}

public sealed record QuickKey(string Label, ushort Keycode)
{
    public override string ToString() => Label;
}

public sealed class SavedCombo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ushort Keycode { get; set; }

    public override string ToString() => $"{Name}  ·  {Description}";
}

public sealed record KeyChange(int Layer, int Row, int Col, ushort Before, ushort After);

public enum MacroActionType
{
    Tap,
    Down,
    Up,
    Delay
}

public sealed class MacroAction
{
    public MacroActionType Type { get; set; }
    public ushort Keycode { get; set; }
    public int DelayMs { get; set; }

    public string TypeLabel => Type switch
    {
        MacroActionType.Tap => "탭",
        MacroActionType.Down => "누름",
        MacroActionType.Up => "뗌",
        MacroActionType.Delay => "지연",
        _ => "동작"
    };

    public string Detail => Type == MacroActionType.Delay ? $"{DelayMs} ms" : KeyLabel(Keycode);

    public MacroAction Clone() => new() { Type = Type, Keycode = Keycode, DelayMs = DelayMs };

    private static string KeyLabel(ushort keycode)
    {
        if (keycode == 0) return "NO";
        if (keycode == 1) return "TRNS";
        if (keycode is >= 0x04 and <= 0x1D) return ((char)('A' + keycode - 0x04)).ToString();
        if (keycode is >= 0x1E and <= 0x27) return ((keycode - 0x1E + 1) % 10).ToString();
        return keycode switch
        {
            0x28 => "ENTER", 0x29 => "ESC", 0x2A => "BSPC", 0x2B => "TAB", 0x2C => "SPACE",
            0x4A => "HOME", 0x4C => "DEL", 0x4F => "→", 0x50 => "←", 0x51 => "↓", 0x52 => "↑",
            0xE0 => "LCTL", 0xE1 => "LSFT", 0xE2 => "LALT", 0xE3 => "LGUI", 0xE4 => "RALT", 0xE5 => "RSFT", 0xE6 => "RCTL", 0xE7 => "RGUI",
            _ => $"0x{keycode:X4}"
        };
    }
}
