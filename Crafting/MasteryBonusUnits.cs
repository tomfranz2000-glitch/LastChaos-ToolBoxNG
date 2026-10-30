using System.Globalization;

namespace LastChaos_ToolBoxNG.Crafting;

/// <summary>Mastery-only effect units. Stored integers remain the wire/database contract.</summary>
public static class MasteryBonusUnits
{
    public const int MovementSpeed = 1000, AttackIntervalReduction = 1001, PveDoubleDamageChance = 1002;
    public const int ChannelCount = 23;
    public static bool Scaled(int effect) => effect is MovementSpeed or AttackIntervalReduction or PveDoubleDamageChance;
    public static int Maximum(int effect) => effect switch { AttackIntervalReduction => 9000, PveDoubleDamageChance => 10000, _ => 32767 };
    public static int ChannelMaximum(int channel) => channel switch { 21 => 9000, 22 => 10000, _ => 32767 };
    public static int Decimals(int effect) => Scaled(effect) ? 2 : 0;
    public static string Unit(int effect) => effect switch {
        MovementSpeed => "speed units", AttackIntervalReduction => "% shorter interval", PveDoubleDamageChance => "% chance (PvE)", _ => "points"
    };
    public static decimal Display(int effect, long stored) => Scaled(effect) ? stored / 100m : stored;
    public static int Store(int effect, decimal displayed)
    {
        if (displayed < Display(effect,1) || displayed > Display(effect,Maximum(effect)))
            throw new FormatException($"Enter {Display(effect, 1):0.##}–{Display(effect, Maximum(effect)):0.##} {Unit(effect)}.");
        decimal stored = Scaled(effect) ? displayed * 100 : displayed;
        if (stored != decimal.Truncate(stored))
            throw new FormatException(Scaled(effect) ? "Use at most two decimal places for this bonus." : "This bonus needs a whole number of points.");
        return (int)stored;
    }
    public static decimal Parse(int effect, string text, CultureInfo? culture = null)
    {
        text = text.Trim();
        if (effect is AttackIntervalReduction or PveDoubleDamageChance) text = text.TrimEnd('%').TrimEnd();
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        // No thousands separators: a decimal point must not silently become a grouped integer on comma-decimal systems.
        if (decimal.TryParse(text, style, culture ?? CultureInfo.CurrentCulture, out decimal value) ||
            decimal.TryParse(text, style, CultureInfo.InvariantCulture, out value)) return value;
        throw new FormatException("Enter a number using a decimal point or your usual decimal separator.");
    }
    public static string Format(int effect, long stored)
    {
        string amount = Display(effect, stored).ToString(Scaled(effect) ? "0.##" : "N0", CultureInfo.CurrentCulture);
        return effect switch {
            MovementSpeed => $"+{amount} Movement Speed",
            AttackIntervalReduction => $"{amount}% shorter Attack Interval",
            PveDoubleDamageChance => $"+{amount}% PvE Double Damage Chance",
            _ => $"+{amount} {CraftingRules.Effects.GetValueOrDefault(effect, $"Unknown effect {effect}")}"
        };
    }
}
