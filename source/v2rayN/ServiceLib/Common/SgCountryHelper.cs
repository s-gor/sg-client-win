using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ServiceLib.Common;

/// <summary>
/// Resolves SG Client country metadata. A structured ISO alpha-2 code has
/// priority. Legacy/third-party profiles are accepted only when the country is
/// written explicitly at the beginning, for example [US], [ᴜs], 【FR】, 🇩🇪 or [🇩🇪].
/// Ordinary names such as sg-admin and de-server are never treated as countries.
/// </summary>
public static class SgCountryHelper
{
    private static readonly HashSet<string> IsoAlpha2Codes = new(
        ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ " +
         "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR " +
         "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO " +
         "JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR " +
         "MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO " +
         "RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW " +
         "TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> RussianNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AM"] = "Армения", ["AT"] = "Австрия", ["AU"] = "Австралия", ["AZ"] = "Азербайджан",
            ["BE"] = "Бельгия", ["BG"] = "Болгария", ["BR"] = "Бразилия", ["BY"] = "Беларусь",
            ["CA"] = "Канада", ["CH"] = "Швейцария", ["CL"] = "Чили", ["CN"] = "Китай",
            ["CO"] = "Колумбия", ["CY"] = "Кипр", ["CZ"] = "Чехия", ["DE"] = "Германия",
            ["DK"] = "Дания", ["EE"] = "Эстония", ["EG"] = "Египет", ["ES"] = "Испания",
            ["FI"] = "Финляндия", ["FR"] = "Франция", ["GB"] = "Великобритания", ["GE"] = "Грузия",
            ["GR"] = "Греция", ["HK"] = "Гонконг", ["HR"] = "Хорватия", ["HU"] = "Венгрия",
            ["ID"] = "Индонезия", ["IE"] = "Ирландия", ["IL"] = "Израиль", ["IN"] = "Индия",
            ["IS"] = "Исландия", ["IT"] = "Италия", ["JP"] = "Япония", ["KG"] = "Кыргызстан",
            ["KR"] = "Южная Корея", ["KZ"] = "Казахстан", ["KH"] = "Камбоджа", ["LA"] = "Лаос",
            ["LK"] = "Шри-Ланка", ["LT"] = "Литва", ["LU"] = "Люксембург", ["LV"] = "Латвия",
            ["MD"] = "Молдова", ["ME"] = "Черногория", ["MK"] = "Северная Македония",
            ["MM"] = "Мьянма", ["MX"] = "Мексика", ["MY"] = "Малайзия", ["NL"] = "Нидерланды",
            ["NO"] = "Норвегия", ["NZ"] = "Новая Зеландия", ["PH"] = "Филиппины", ["PK"] = "Пакистан",
            ["PL"] = "Польша", ["PT"] = "Португалия", ["RO"] = "Румыния", ["RS"] = "Сербия",
            ["RU"] = "Россия", ["SE"] = "Швеция", ["SG"] = "Сингапур", ["SI"] = "Словения",
            ["SK"] = "Словакия", ["TH"] = "Таиланд", ["TR"] = "Турция", ["TW"] = "Тайвань",
            ["UA"] = "Украина", ["US"] = "США", ["UZ"] = "Узбекистан", ["VN"] = "Вьетнам",
            ["ZA"] = "ЮАР", ["AE"] = "ОАЭ", ["AR"] = "Аргентина", ["SA"] = "Саудовская Аравия"
        };

    private static readonly Regex LegacySgBrandPrefixRegex = new(
        @"^[\s\p{Cf}]*sg-(?:admin|client|panel|node|awg)(?:[/\s-]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<char, char> CountryLetterMap =
        new Dictionary<char, char>
        {
            // IPA/small-cap forms commonly used by subscription generators.
            ['ᴀ'] = 'A', ['ʙ'] = 'B', ['ᴄ'] = 'C', ['ᴅ'] = 'D', ['ᴇ'] = 'E',
            ['ꜰ'] = 'F', ['ғ'] = 'F', ['ɢ'] = 'G', ['ʜ'] = 'H', ['ɪ'] = 'I',
            ['ᴊ'] = 'J', ['ᴋ'] = 'K', ['ʟ'] = 'L', ['ᴍ'] = 'M', ['ɴ'] = 'N',
            ['ᴏ'] = 'O', ['ᴘ'] = 'P', ['ǫ'] = 'Q', ['ʀ'] = 'R', ['ꜱ'] = 'S',
            ['ᴛ'] = 'T', ['ᴜ'] = 'U', ['ᴠ'] = 'V', ['ᴡ'] = 'W', ['ˣ'] = 'X',
            ['ʏ'] = 'Y', ['ᴢ'] = 'Z',

            // Cyrillic and Greek homoglyphs seen in decorative labels.
            ['А'] = 'A', ['а'] = 'A', ['Α'] = 'A', ['α'] = 'A',
            ['В'] = 'B', ['в'] = 'B', ['Β'] = 'B', ['β'] = 'B',
            ['С'] = 'C', ['с'] = 'C', ['Ϲ'] = 'C', ['ϲ'] = 'C',
            ['Е'] = 'E', ['е'] = 'E', ['Ε'] = 'E', ['ε'] = 'E',
            ['Ғ'] = 'F',
            ['Н'] = 'H', ['н'] = 'H', ['Η'] = 'H', ['η'] = 'H',
            ['І'] = 'I', ['і'] = 'I', ['Ι'] = 'I', ['ι'] = 'I',
            ['Ј'] = 'J', ['ј'] = 'J',
            ['К'] = 'K', ['к'] = 'K', ['Κ'] = 'K', ['κ'] = 'K',
            ['М'] = 'M', ['м'] = 'M', ['Μ'] = 'M', ['μ'] = 'M',
            ['Ν'] = 'N', ['ν'] = 'N',
            ['О'] = 'O', ['о'] = 'O', ['Ο'] = 'O', ['ο'] = 'O',
            ['Р'] = 'P', ['р'] = 'P', ['Ρ'] = 'P', ['ρ'] = 'P',
            ['Ѕ'] = 'S', ['ѕ'] = 'S',
            ['Т'] = 'T', ['т'] = 'T', ['Τ'] = 'T', ['τ'] = 'T',
            ['У'] = 'Y', ['у'] = 'Y', ['Υ'] = 'Y', ['υ'] = 'Y',
            ['Х'] = 'X', ['х'] = 'X', ['Χ'] = 'X', ['χ'] = 'X',
            ['Ζ'] = 'Z', ['ζ'] = 'Z'
        };

    public static string NormalizeCode(string? value)
    {
        var code = value?.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant() ?? string.Empty;
        code = code switch
        {
            "UK" => "GB",
            "EL" => "GR",
            _ => code
        };
        return code.Length == 2 && IsoAlpha2Codes.Contains(code) ? code : string.Empty;
    }

    public static bool IsKnownCode(string? value) => NormalizeCode(value).Length == 2;

    public static string ResolveCode(string? structuredCode, string? remarks)
    {
        var code = NormalizeCode(structuredCode);
        if (code.Length == 2)
        {
            return code;
        }

        return TryReadLeadingCountryMarker(remarks, out code, out _) ? code : string.Empty;
    }

    public static string CleanRemarks(string? remarks, string? countryCode)
    {
        var original = remarks?.Trim() ?? string.Empty;
        var code = ResolveCode(countryCode, original);
        if (original.Length == 0)
        {
            return "Без названия";
        }
        if (code.Length != 2)
        {
            return original;
        }

        if (TryReadLeadingCountryMarker(original, out var markerCode, out var markerLength)
            && string.Equals(markerCode, code, StringComparison.OrdinalIgnoreCase))
        {
            var cleaned = original[markerLength..].TrimStart();
            return cleaned.Length > 0 ? cleaned : original;
        }

        return original;
    }

    public static bool IsLegacySgBrandPrefix(string? remarks) =>
        LegacySgBrandPrefixRegex.IsMatch(remarks ?? string.Empty);

    public static string GetRussianName(string? countryCode)
    {
        var code = NormalizeCode(countryCode);
        if (code.Length != 2)
        {
            return "Страна не определена";
        }
        return RussianNames.TryGetValue(code, out var name) ? name : code;
    }

    public static string GetFilterLabel(string? countryCode)
    {
        var code = NormalizeCode(countryCode);
        return code.Length == 2 ? $"{GetRussianName(code)} · {code}" : "Страна не определена";
    }

    private static bool TryReadLeadingCountryMarker(string? text, out string code, out int consumedLength)
    {
        if (TryReadLeadingFlag(text, out code, out consumedLength))
        {
            return true;
        }

        if (TryReadLeadingBracketFlag(text, out code, out consumedLength))
        {
            return true;
        }

        return TryReadLeadingBracketCode(text, out code, out consumedLength);
    }

    private static bool TryReadLeadingBracketFlag(string? text, out string code, out int consumedLength)
    {
        code = string.Empty;
        consumedLength = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text;
        var start = SkipIgnorable(value, 0);
        var scanLimit = Math.Min(value.Length, start + 32);
        var openIndex = -1;
        var expectedClose = '\0';

        for (var index = start; index < scanLimit; index++)
        {
            var ch = value[index];
            if (IsIgnorable(ch))
            {
                continue;
            }

            var close = GetClosingBracket(ch);
            if (close != '\0' || char.GetUnicodeCategory(ch) == UnicodeCategory.OpenPunctuation)
            {
                openIndex = index;
                expectedClose = close;
                break;
            }

            if (char.IsLetterOrDigit(ch))
            {
                return false;
            }
        }

        if (openIndex < 0)
        {
            return false;
        }

        var closeIndex = -1;
        for (var index = openIndex + 1; index < Math.Min(value.Length, openIndex + 18); index++)
        {
            var ch = value[index];
            if ((expectedClose != '\0' && ch == expectedClose)
                || (expectedClose == '\0' && char.GetUnicodeCategory(ch) == UnicodeCategory.ClosePunctuation))
            {
                closeIndex = index;
                break;
            }
        }

        if (closeIndex < 0
            || !TryDecodeRegionalIndicatorFlag(value[(openIndex + 1)..closeIndex], out code))
        {
            return false;
        }

        var next = SkipIgnorable(value, closeIndex + 1);
        if (next < value.Length && IsLeadingSeparator(value[next]))
        {
            next = SkipIgnorable(value, next + 1);
        }
        consumedLength = next;
        return true;
    }

    private static bool TryDecodeRegionalIndicatorFlag(string value, out string code)
    {
        code = string.Empty;
        var index = SkipIgnorable(value, 0);
        if (index + 1 >= value.Length || !char.IsSurrogatePair(value, index))
        {
            return false;
        }

        var first = char.ConvertToUtf32(value, index);
        index = SkipIgnorable(value, index + 2);
        if (index + 1 >= value.Length || !char.IsSurrogatePair(value, index))
        {
            return false;
        }

        var second = char.ConvertToUtf32(value, index);
        index = SkipIgnorable(value, index + 2);
        if (index != value.Length
            || first is < 0x1F1E6 or > 0x1F1FF
            || second is < 0x1F1E6 or > 0x1F1FF)
        {
            return false;
        }

        code = NormalizeCode(string.Concat(
            (char)('A' + first - 0x1F1E6),
            (char)('A' + second - 0x1F1E6)));
        return code.Length == 2;
    }

    private static bool TryReadLeadingBracketCode(string? text, out string code, out int consumedLength)
    {
        code = string.Empty;
        consumedLength = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text;
        var start = SkipIgnorable(value, 0);
        var scanLimit = Math.Min(value.Length, start + 32);
        var openIndex = -1;
        var expectedClose = '\0';

        for (var index = start; index < scanLimit; index++)
        {
            var ch = value[index];
            if (IsIgnorable(ch))
            {
                continue;
            }

            var close = GetClosingBracket(ch);
            if (close != '\0' || char.GetUnicodeCategory(ch) == UnicodeCategory.OpenPunctuation)
            {
                openIndex = index;
                expectedClose = close;
                break;
            }

            // Decorations before an explicit marker are allowed. A plain word is not.
            if (char.IsLetterOrDigit(ch))
            {
                return false;
            }
        }

        if (openIndex < 0)
        {
            return false;
        }

        var closeIndex = -1;
        for (var index = openIndex + 1; index < Math.Min(value.Length, openIndex + 18); index++)
        {
            var ch = value[index];
            if ((expectedClose != '\0' && ch == expectedClose)
                || (expectedClose == '\0' && char.GetUnicodeCategory(ch) == UnicodeCategory.ClosePunctuation))
            {
                closeIndex = index;
                break;
            }
        }

        if (closeIndex < 0 || !TryNormalizeMarker(value[(openIndex + 1)..closeIndex], out code))
        {
            return false;
        }

        var next = SkipIgnorable(value, closeIndex + 1);
        if (next < value.Length && IsLeadingSeparator(value[next]))
        {
            next = SkipIgnorable(value, next + 1);
        }
        consumedLength = next;
        return true;
    }

    private static bool TryNormalizeMarker(string marker, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        Span<char> letters = stackalloc char[2];
        var count = 0;
        foreach (var ch in marker.Normalize(NormalizationForm.FormKC))
        {
            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.Format
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Control
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol)
            {
                continue;
            }

            var normalized = NormalizeCountryLetter(ch);
            if (normalized == '\0')
            {
                // An explicit two-letter marker must not silently swallow a real letter.
                if (category is UnicodeCategory.UppercaseLetter
                    or UnicodeCategory.LowercaseLetter
                    or UnicodeCategory.TitlecaseLetter
                    or UnicodeCategory.ModifierLetter
                    or UnicodeCategory.OtherLetter)
                {
                    return false;
                }
                continue;
            }

            if (count >= 2)
            {
                return false;
            }
            letters[count++] = normalized;
        }

        if (count != 2)
        {
            return false;
        }

        code = NormalizeCode(new string(letters));
        return code.Length == 2;
    }

    private static char NormalizeCountryLetter(char value)
    {
        if (value is >= 'a' and <= 'z')
        {
            return char.ToUpperInvariant(value);
        }
        if (value is >= 'A' and <= 'Z')
        {
            return value;
        }
        if (CountryLetterMap.TryGetValue(value, out var mapped))
        {
            return mapped;
        }

        // Most full-width/styled Latin letters collapse through compatibility
        // normalization. This fallback also handles ordinary accented Latin letters.
        var decomposed = value.ToString().Normalize(NormalizationForm.FormKD);
        foreach (var ch in decomposed)
        {
            if (ch is >= 'a' and <= 'z')
            {
                return char.ToUpperInvariant(ch);
            }
            if (ch is >= 'A' and <= 'Z')
            {
                return ch;
            }
        }
        return '\0';
    }

    private static bool TryReadLeadingFlag(string? text, out string code, out int consumedLength)
    {
        code = string.Empty;
        consumedLength = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text;
        var index = SkipIgnorable(value, 0);
        if (index + 3 >= value.Length
            || !char.IsSurrogatePair(value, index)
            || !char.IsSurrogatePair(value, index + 2))
        {
            return false;
        }

        var first = char.ConvertToUtf32(value, index);
        var second = char.ConvertToUtf32(value, index + 2);
        if (first is < 0x1F1E6 or > 0x1F1FF || second is < 0x1F1E6 or > 0x1F1FF)
        {
            return false;
        }

        code = NormalizeCode(string.Concat(
            (char)('A' + first - 0x1F1E6),
            (char)('A' + second - 0x1F1E6)));
        if (code.Length != 2)
        {
            return false;
        }

        index = SkipIgnorable(value, index + 4);
        if (index < value.Length && IsLeadingSeparator(value[index]))
        {
            index = SkipIgnorable(value, index + 1);
        }
        consumedLength = index;
        return true;
    }

    private static int SkipIgnorable(string value, int index)
    {
        while (index < value.Length && IsIgnorable(value[index]))
        {
            index++;
        }
        return index;
    }

    private static bool IsIgnorable(char value)
    {
        if (char.IsWhiteSpace(value) || value is '\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\uFE0E' or '\uFE0F')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(value);
        return category is UnicodeCategory.Format
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static bool IsLeadingSeparator(char value) => "-–—|:·/".Contains(value);

    private static char GetClosingBracket(char value) => value switch
    {
        '[' => ']',
        '［' => '］',
        '【' => '】',
        '〔' => '〕',
        '〖' => '〗',
        '⟦' => '⟧',
        '{' => '}',
        '(' => ')',
        _ => '\0'
    };
}
