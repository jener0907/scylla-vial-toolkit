namespace ScyllaConfigurator;

public static class MacroCodec
{
    private const byte Prefix = 1;
    private const byte Tap = 1;
    private const byte Down = 2;
    private const byte Up = 3;
    private const byte Delay = 4;

    public static byte[] Encode(string source)
    {
        var result = new List<byte>();
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                var end = source.IndexOf('}', i + 1);
                if (end < 0) throw new FormatException("닫히지 않은 { 토큰이 있습니다.");
                EncodeToken(source[(i + 1)..end], result);
                i = end;
            }
            else
            {
                EncodeCharacter(source[i], result);
            }
        }
        return result.ToArray();
    }

    public static string Decode(byte[] buffer, int slot)
    {
        var start = 0;
        for (var current = 0; current < slot; current++)
        {
            var end = Array.IndexOf(buffer, (byte)0, start);
            if (end < 0) return "";
            start = end + 1;
        }
        var stop = Array.IndexOf(buffer, (byte)0, start);
        if (stop < 0) stop = buffer.Length;

        var result = new System.Text.StringBuilder();
        for (var i = start; i < stop; i++)
        {
            if (buffer[i] != Prefix)
            {
                result.Append(buffer[i] is >= 32 and <= 126 ? (char)buffer[i] : '?');
                continue;
            }
            if (++i >= stop) break;
            var action = buffer[i];
            if (action is Tap or Down or Up)
            {
                if (++i >= stop) break;
                AppendToken(result, buffer[i], action);
            }
            else if ((action is 5 or 6 or 7) && i + 2 < stop)
            {
                var key = (ushort)(buffer[++i] | (buffer[++i] << 8));
                if (key > 0xFF00) key = (ushort)((key & 0xFF) << 8);
                AppendToken(result, key, action);
            }
            else if (action == Delay && i + 2 < stop)
            {
                var ms = (buffer[++i] - 1) + (buffer[++i] - 1) * 255;
                result.Append($"{{DELAY:{ms}}}");
            }
        }
        return result.ToString();
    }

    public static byte[] EncodeActions(IReadOnlyList<MacroAction> actions)
    {
        var output = new List<byte>();
        foreach (var action in actions)
        {
            if (action.Type == MacroActionType.Delay)
            {
                if (action.DelayMs is < 1 or > 65025) throw new FormatException("지연 시간은 1~65025 ms 사이여야 합니다.");
                var d0 = (byte)(action.DelayMs % 255 + 1);
                var d1 = (byte)(action.DelayMs / 255 + 1);
                output.AddRange([Prefix, Delay, d0, d1]);
                continue;
            }

            var encodedAction = action.Type switch
            {
                MacroActionType.Tap => Tap,
                MacroActionType.Down => Down,
                MacroActionType.Up => Up,
                _ => throw new InvalidOperationException("지원하지 않는 매크로 동작입니다.")
            };
            AddKey(output, action.Keycode, encodedAction);
        }
        return output.ToArray();
    }

    public static List<MacroAction> DecodeActions(byte[] buffer, int slot)
    {
        var (start, stop) = GetSlotBounds(buffer, slot);
        var result = new List<MacroAction>();
        for (var i = start; i < stop; i++)
        {
            if (buffer[i] != Prefix)
            {
                if (TryAscii((char)buffer[i], out var key, out var shifted))
                {
                    if (shifted) result.Add(new MacroAction { Type = MacroActionType.Down, Keycode = 0xE1 });
                    result.Add(new MacroAction { Type = MacroActionType.Tap, Keycode = key });
                    if (shifted) result.Add(new MacroAction { Type = MacroActionType.Up, Keycode = 0xE1 });
                }
                continue;
            }
            if (++i >= stop) break;
            var action = buffer[i];
            if (action is Tap or Down or Up)
            {
                if (++i >= stop) break;
                result.Add(new MacroAction { Type = ToActionType(action), Keycode = buffer[i] });
            }
            else if (action is 5 or 6 or 7 && i + 2 < stop)
            {
                var key = (ushort)(buffer[++i] | (buffer[++i] << 8));
                if (key > 0xFF00) key = (ushort)((key & 0xFF) << 8);
                result.Add(new MacroAction { Type = ToActionType((byte)(action - 4)), Keycode = key });
            }
            else if (action == Delay && i + 2 < stop)
            {
                var ms = (buffer[++i] - 1) + (buffer[++i] - 1) * 255;
                result.Add(new MacroAction { Type = MacroActionType.Delay, DelayMs = ms });
            }
        }
        return result;
    }

    private static MacroActionType ToActionType(byte action) => action switch
    {
        Tap => MacroActionType.Tap,
        Down => MacroActionType.Down,
        Up => MacroActionType.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static (int Start, int Stop) GetSlotBounds(byte[] buffer, int slot)
    {
        var start = 0;
        for (var current = 0; current < slot; current++)
        {
            var end = Array.IndexOf(buffer, (byte)0, start);
            if (end < 0) return (buffer.Length, buffer.Length);
            start = end + 1;
        }
        var stop = Array.IndexOf(buffer, (byte)0, start);
        return (start, stop < 0 ? buffer.Length : stop);
    }

    private static void AppendToken(System.Text.StringBuilder output, ushort key, byte action)
    {
        var suffix = action == Down ? "_DOWN" : action == Up ? "_UP" : "";
        if (action == Tap && key is >= 0x04 and <= 0x1D)
        {
            output.Append((char)('a' + key - 0x04));
            return;
        }
        if (action == Tap && key is >= 0x1E and <= 0x27)
        {
            output.Append(key == 0x27 ? '0' : (char)('1' + key - 0x1E));
            return;
        }
        var name = key switch
        {
            0x28 => "ENTER", 0x29 => "ESC", 0x2A => "BACKSPACE", 0x2B => "TAB", 0x2C => "SPACE",
            0x4C => "DEL", 0x49 => "INSERT", 0x4A => "HOME", 0x4D => "END", 0x4B => "PAGEUP", 0x4E => "PAGEDOWN",
            0x4F => "RIGHT", 0x50 => "LEFT", 0x51 => "DOWN", 0x52 => "UP",
            0xE0 => "CTRL", 0xE1 => "SHIFT", 0xE2 => "ALT", 0xE3 => "WIN",
            0xE4 => "RCTRL", 0xE5 => "RSHIFT", 0xE6 => "RALT", 0xE7 => "RWIN",
            _ => $"KC:0x{key:X4}"
        };
        output.Append('{').Append(name).Append(suffix).Append('}');
    }

    private static void EncodeToken(string token, List<byte> output)
    {
        var value = token.Trim().ToUpperInvariant();
        if (value.StartsWith("DELAY:", StringComparison.Ordinal))
        {
            if (!int.TryParse(value[6..], out var ms) || ms < 1 || ms > 65025)
                throw new FormatException("DELAY는 1~65025 사이의 숫자여야 합니다.");
            var d0 = (byte)(ms % 255 + 1);
            var d1 = (byte)(ms / 255 + 1);
            output.AddRange([Prefix, Delay, d0, d1]);
            return;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ushort chordKey = 0;
        var hasChordKey = parts.Length > 1 && (TryModifier(parts[^1], out chordKey) || TryNamedKey(parts[^1], out chordKey));
        if (hasChordKey)
        {
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!TryModifier(parts[i], out var modifier)) throw new FormatException($"알 수 없는 조합키: {parts[i]}");
                AddKey(output, modifier, Down);
            }
            AddKey(output, chordKey, Tap);
            for (var i = parts.Length - 2; i >= 0; i--)
            {
                TryModifier(parts[i], out var modifier);
                AddKey(output, modifier, Up);
            }
            return;
        }

        if (TryModifier(value, out var mod)) { AddKey(output, mod, value.EndsWith("_DOWN") ? Down : Up); return; }
        if (TryNamedKey(value, out var key)) { AddKey(output, key, Tap); return; }
        throw new FormatException($"알 수 없는 매크로 토큰: {{{token}}}");
    }

    private static void EncodeCharacter(char character, List<byte> output)
    {
        if (character == '\r') return;
        if (character == '\n') { AddKey(output, 0x28, Tap); return; }
        if (character == '\t') { AddKey(output, 0x2B, Tap); return; }
        if (!TryAscii(character, out var key, out var shifted))
            throw new FormatException($"지원하지 않는 문자: {character}");
        if (shifted) AddKey(output, 0xE1, Down);
        AddKey(output, key, Tap);
        if (shifted) AddKey(output, 0xE1, Up);
    }

    private static bool TryModifier(string value, out ushort key)
    {
        var down = value.EndsWith("_DOWN", StringComparison.Ordinal);
        var up = value.EndsWith("_UP", StringComparison.Ordinal);
        var name = down ? value[..^5] : up ? value[..^3] : value;
        key = name switch
        {
            "CTRL" or "LCTRL" => 0xE0,
            "SHIFT" or "LSHIFT" => 0xE1,
            "ALT" or "LALT" => 0xE2,
            "WIN" or "GUI" or "LWIN" or "LGUI" => 0xE3,
            "RCTRL" => 0xE4,
            "RSHIFT" => 0xE5,
            "RALT" => 0xE6,
            "RWIN" or "RGUI" => 0xE7,
            _ => 0
        };
        return key != 0 && (down || up || !value.Contains('_'));
    }

    private static bool TryNamedKey(string value, out ushort key)
    {
        key = value switch
        {
            "ENTER" or "RETURN" => 0x28,
            "ESC" or "ESCAPE" => 0x29,
            "BACKSPACE" or "BSPC" => 0x2A,
            "TAB" => 0x2B,
            "SPACE" => 0x2C,
            "DELETE" or "DEL" => 0x4C,
            "INSERT" => 0x49,
            "HOME" => 0x4A,
            "END" => 0x4D,
            "PAGEUP" => 0x4B,
            "PAGEDOWN" => 0x4E,
            "UP" => 0x52,
            "DOWN" => 0x51,
            "LEFT" => 0x50,
            "RIGHT" => 0x4F,
            "F1" => 0x3A, "F2" => 0x3B, "F3" => 0x3C, "F4" => 0x3D, "F5" => 0x3E, "F6" => 0x3F,
            "F7" => 0x40, "F8" => 0x41, "F9" => 0x42, "F10" => 0x43, "F11" => 0x44, "F12" => 0x45,
            _ => 0
        };
        return key != 0;
    }

    private static bool TryAscii(char character, out ushort key, out bool shifted)
    {
        shifted = false;
        if (character is >= 'a' and <= 'z') { key = (ushort)(0x04 + character - 'a'); return true; }
        if (character is >= 'A' and <= 'Z') { key = (ushort)(0x04 + character - 'A'); shifted = true; return true; }
        if (character is >= '1' and <= '9') { key = (ushort)(0x1E + character - '1'); return true; }
        if (character == '0') { key = 0x27; return true; }
        key = character switch
        {
            ' ' => 0x2C, '-' => 0x2D, '=' => 0x2E, '[' => 0x2F, ']' => 0x30, '\\' => 0x31,
            ';' => 0x33, '\'' => 0x34, '`' => 0x35, ',' => 0x36, '.' => 0x37, '/' => 0x38,
            '!' => 0x1E, '@' => 0x1F, '#' => 0x20, '$' => 0x21, '%' => 0x22, '^' => 0x23,
            '&' => 0x24, '*' => 0x25, '(' => 0x26, ')' => 0x27, '_' => 0x2D, '+' => 0x2E,
            '{' => 0x2F, '}' => 0x30, '|' => 0x31, ':' => 0x33, '"' => 0x34, '~' => 0x35,
            '<' => 0x36, '>' => 0x37, '?' => 0x38, _ => 0
        };
        shifted = character is >= '!' and <= '~' && "!@#$%^&*()_+{}|:\"~<>?".Contains(character);
        return key != 0;
    }

    private static void AddKey(List<byte> output, ushort keycode, byte action)
    {
        if (keycode <= 0xFF)
        {
            output.AddRange([Prefix, action, (byte)keycode]);
            return;
        }
        var encoded = (keycode & 0xFF00) == 0 ? (ushort)(0xFF00 | (keycode >> 8)) : keycode;
        output.AddRange([Prefix, (byte)(action + 4), (byte)encoded, (byte)(encoded >> 8)]);
    }
}
