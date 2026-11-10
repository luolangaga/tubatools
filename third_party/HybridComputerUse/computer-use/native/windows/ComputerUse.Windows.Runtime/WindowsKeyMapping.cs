namespace ComputerUse.Windows.Runtime;

internal static class WindowsKeyMapping
{
    internal static ushort[] ResolveChord(IReadOnlyList<string> specifications)
    {
        var keys = new List<ushort>();
        foreach (var specification in specifications)
        {
            var tokens = specification.Split(
                '+',
                StringSplitOptions.TrimEntries);
            if (tokens.Length == 0 || tokens.Any(string.IsNullOrEmpty))
            {
                throw new InvalidOperationException(
                    $"unsupported_key:{specification}");
            }
            keys.AddRange(tokens.Select(KeyCode));
        }
        return keys.ToArray();
    }

    internal static ushort KeyCode(string name)
    {
        var trimmed = name.Trim();
        if (trimmed == "-")
        {
            return 0xBD;
        }
        var normalized = trimmed.ToUpperInvariant().Replace("-", "_");
        return normalized switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "SUPER" or "WIN" or "META" => 0x5B,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" or "SPACEBAR" => 0x20,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "LEFT" or "ARROWLEFT" => 0x25,
            "UP" or "ARROWUP" => 0x26,
            "RIGHT" or "ARROWRIGHT" => 0x27,
            "DOWN" or "ARROWDOWN" => 0x28,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGE_UP" or "PAGEUP" => 0x21,
            "PAGE_DOWN" or "PAGEDOWN" => 0x22,
            ";" or "SEMICOLON" => 0xBA,
            "=" or "EQUAL" or "EQUALS" => 0xBB,
            "," or "COMMA" => 0xBC,
            "MINUS" => 0xBD,
            "." or "PERIOD" or "DOT" => 0xBE,
            "/" or "SLASH" => 0xBF,
            "`" or "BACKTICK" or "GRAVE" => 0xC0,
            "[" or "LEFT_BRACKET" or "BRACKET_LEFT" => 0xDB,
            "\\" or "BACKSLASH" => 0xDC,
            "]" or "RIGHT_BRACKET" or "BRACKET_RIGHT" => 0xDD,
            "'" or "QUOTE" or "APOSTROPHE" => 0xDE,
            // Only ASCII letters and digits share their virtual-key code.
            // Casting any other character would press an unrelated key.
            _ when trimmed.Length == 1 &&
                trimmed[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' =>
                unchecked((ushort)char.ToUpperInvariant(normalized[0])),
            _ when normalized.StartsWith('F') &&
                int.TryParse(normalized[1..], out var number) &&
                number is >= 1 and <= 24 =>
                unchecked((ushort)(0x70 + number - 1)),
            _ => throw new InvalidOperationException(
                $"unsupported_key:{name}"),
        };
    }
}
