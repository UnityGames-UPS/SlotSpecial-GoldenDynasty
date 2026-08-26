using System.Text;

public static class SpriteTextFormatter
{
    // Single definition of how money is written across the game. "0.00" rather than "0.###"
    // because # drops trailing zeros — a win of 1 rendered as a bare "1" and 0.5 as "0.5",
    // so the decimals appeared and vanished depending on the amount.
    // Deliberately NOT used for the free-games multiplier ("X5" reads better than "X5.00")
    // or the paytable in SymbolInfoCard, which both stay compact.
    public const string MoneyFormat = "0.00";

    // Money formatted for the sprite-digit fonts. Plain-text money displays should use
    // ToString(MoneyFormat) directly so they still share the same definition.
    public static string ToSpriteMoney(double amount)
    {
        return ToSpriteDigits(amount.ToString(MoneyFormat));
    }

    // Converts plain formatted number text (e.g. "4.8") into TMP sprite-asset tags
    // (e.g. "<sprite=4><sprite=10><sprite=8>") for fonts that render digits as sprites.
    // <sprite=0>..<sprite=9> are the digits, <sprite=10> is the decimal point.
    public static string ToSpriteDigits(string numberText)
    {
        var sb = new StringBuilder(numberText.Length * 10);
        foreach (char c in numberText)
        {
            if (c == '.')
            {
                sb.Append("<sprite=10>");
            }
            else if (c >= '0' && c <= '9')
            {
                sb.Append("<sprite=").Append(c - '0').Append('>');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
