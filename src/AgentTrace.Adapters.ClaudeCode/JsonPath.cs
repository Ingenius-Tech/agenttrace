using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace AgentTrace.Adapters.ClaudeCode;

/// <summary>
/// Mimics, over <see cref="JsonElement"/>, the part of the Jackson API (<c>JsonNode</c>) that the Java adapter used,
/// so the parser could be ported almost line by line: <c>path()</c> never fails and returns an "absent" node
/// (<c>default(JsonElement)</c>, of kind <see cref="JsonValueKind.Undefined"/>), and <c>asText</c>, <c>asLong</c> and
/// <c>asBoolean</c> convert with the same rules as Jackson 2.17. <see cref="ToJsonString"/> reproduces <c>toString()</c>:
/// compact JSON, same key order, same escaping and numbers formatted as in Java.
/// </summary>
internal static class JsonPath
{
    /// <summary>
    /// Equivalent to <c>ObjectMapper.readTree(String)</c>: reads the first value of the text and ignores what follows;
    /// empty or blank text gives null (absent node). Invalid JSON throws <see cref="JsonException"/>.
    /// </summary>
    public static JsonDocument? Parse(string text)
    {
        if (text.AsSpan().Trim(" \t\r\n").IsEmpty) return null;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { MaxDepth = 1000 });
        reader.Read();
        return JsonDocument.ParseValue(ref reader);
    }

    /// <summary><c>path(key)</c>: the property value, or an absent node if this is not an object or lacks the property.</summary>
    public static JsonElement Path(this JsonElement node, string key) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out JsonElement value) ? value : default;

    public static bool IsMissing(this JsonElement node) => node.ValueKind == JsonValueKind.Undefined;

    public static bool IsNull(this JsonElement node) => node.ValueKind == JsonValueKind.Null;

    public static bool IsTextual(this JsonElement node) => node.ValueKind == JsonValueKind.String;

    public static bool IsArray(this JsonElement node) => node.ValueKind == JsonValueKind.Array;

    public static bool IsObject(this JsonElement node) => node.ValueKind == JsonValueKind.Object;

    /// <summary>Elements of an array, or <c>size()</c> of an array (0 if it is not one).</summary>
    public static int Size(this JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Array => node.GetArrayLength(),
        JsonValueKind.Object => Entries(node).Count,
        _ => 0,
    };

    /// <summary>First element of an array, or an absent node (<c>get(0)</c>).</summary>
    public static JsonElement First(this JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array) return default;
        foreach (JsonElement item in node.EnumerateArray()) return item;
        return default;
    }

    /// <summary>
    /// What a Jackson <c>for (JsonNode x : node)</c> iterates: the elements of an array, the VALUES of an
    /// object and nothing for other nodes.
    /// </summary>
    public static IEnumerable<JsonElement> Elements(this JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                return node.EnumerateArray().ToList();
            case JsonValueKind.Object:
                return Entries(node).Select(e => e.Value).ToList();
            default:
                return [];
        }
    }

    /// <summary><c>asText()</c>: text, number or boolean as text; "null" for null; empty for containers and absent nodes.</summary>
    public static string AsText(this JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.String => node.GetString() ?? "",
        JsonValueKind.Number => NumberText(node),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => "",
    };

    /// <summary><c>asText(defaultValue)</c>: like <c>asText()</c>, except that null and absent give the default value.</summary>
    public static string AsText(this JsonElement node, string defaultValue) =>
        node.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? defaultValue : node.AsText();

    /// <summary><c>asBoolean(defaultValue)</c>.</summary>
    public static bool AsBoolean(this JsonElement node, bool defaultValue)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                string v = JavaTrim(node.GetString() ?? "");
                return v == "true" || (v != "false" && defaultValue);
            case JsonValueKind.Number:
                return TryInteger(node, out BigInteger big) ? !big.IsZero : defaultValue;
            default:
                return defaultValue;
        }
    }

    /// <summary><c>asLong(defaultValue)</c>.</summary>
    public static long AsLong(this JsonElement node, long defaultValue)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.True:
                return 1;
            case JsonValueKind.False:
                return 0;
            case JsonValueKind.Number:
                return TryInteger(node, out BigInteger big) ? Wrap(big) : DoubleToLong(ParseDouble(node));
            case JsonValueKind.String:
                return ParseAsLong(node.GetString() ?? "", defaultValue);
            default:
                return defaultValue;
        }
    }

    /// <summary><c>canConvertToLong()</c>: a number that fits in a <c>long</c> (a decimal, if it is in range).</summary>
    public static bool CanConvertToLong(this JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Number) return false;
        if (TryInteger(node, out BigInteger big)) return big >= long.MinValue && big <= long.MaxValue;
        double d = ParseDouble(node);
        return d >= -9.223372036854775808E18 && d <= 9.223372036854775807E18;
    }

    /// <summary>
    /// Jackson's <c>toString()</c>: compact JSON with the same escaping and numbers formatted as in Java.
    /// An absent node gives "" (like <c>MissingNode</c>). Repeated keys are merged as in an <c>ObjectNode</c>.
    /// </summary>
    public static string ToJsonString(this JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Undefined) return "";
        var sb = new StringBuilder();
        Write(sb, node);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, JsonElement> entry in Entries(node))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Quote(sb, entry.Key);
                    sb.Append(':');
                    Write(sb, entry.Value);
                }
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                bool firstItem = true;
                foreach (JsonElement item in node.EnumerateArray())
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            case JsonValueKind.String:
                Quote(sb, node.GetString() ?? "");
                break;
            case JsonValueKind.Number:
                string number = NumberText(node);
                // Jackson writes NaN and Infinity (which do not exist in JSON) in quotes
                if (number is "NaN" or "Infinity" or "-Infinity") Quote(sb, number);
                else sb.Append(number);
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            default:
                sb.Append("null");
                break;
        }
    }

    /// <summary>
    /// Properties of an object as an <c>ObjectNode</c>: in order of appearance and, if a key repeats, with the
    /// last value at the position of the first.
    /// </summary>
    private static List<KeyValuePair<string, JsonElement>> Entries(JsonElement node)
    {
        var entries = new List<KeyValuePair<string, JsonElement>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty property in node.EnumerateObject())
        {
            if (index.TryGetValue(property.Name, out int at)) entries[at] = new(property.Name, property.Value);
            else
            {
                index[property.Name] = entries.Count;
                entries.Add(new(property.Name, property.Value));
            }
        }
        return entries;
    }

    /// <summary>Default Jackson escaping: only quotes, backslash and control characters; everything else as is.</summary>
    private static void Quote(StringBuilder sb, string text)
    {
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\f': sb.Append("\\f"); break;
                case '\r': sb.Append("\\r"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Text of a number as Jackson would give it: integers as is (BigInteger if large), decimals as <c>Double.toString</c>.</summary>
    private static string NumberText(JsonElement node) =>
        TryInteger(node, out BigInteger big) ? big.ToString(CultureInfo.InvariantCulture) : JavaDouble(ParseDouble(node));

    private static bool TryInteger(JsonElement node, out BigInteger value)
    {
        string raw = node.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0)
        {
            value = default;
            return false;
        }
        value = BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return true;
    }

    private static double ParseDouble(JsonElement node) =>
        double.Parse(node.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary><c>BigInteger.longValue()</c>: the low 64 bits.</summary>
    private static long Wrap(BigInteger big) => unchecked((long)(ulong)(big & ulong.MaxValue));

    /// <summary>Java's <c>(long) double</c> conversion: saturates and NaN gives 0.</summary>
    private static long DoubleToLong(double d)
    {
        if (double.IsNaN(d)) return 0;
        if (d >= 9.223372036854775807E18) return long.MaxValue;
        if (d <= -9.223372036854775808E18) return long.MinValue;
        return (long)d;
    }

    /// <summary>Jackson's <c>NumberInput.parseAsLong</c>: trims, accepts an integer or else a decimal that is truncated.</summary>
    private static long ParseAsLong(string text, long defaultValue)
    {
        string s = JavaTrim(text);
        if (s.Length == 0) return defaultValue;
        if (s[0] == '+') s = s[1..];
        int start = s.Length > 0 && s[0] == '-' ? 1 : 0;
        bool digits = true;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9')
            {
                digits = false;
                break;
            }
        }
        if (digits)
        {
            return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed) ? parsed : defaultValue;
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? DoubleToLong(d) : defaultValue;
    }

    /// <summary>Java's <c>String.trim()</c>: removes every character less than or equal to the space.</summary>
    private static string JavaTrim(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && s[start] <= ' ') start++;
        while (end > start && s[end - 1] <= ' ') end--;
        return s[start..end];
    }

    /// <summary>
    /// Java's <c>Double.toString</c>: decimal between 1e-3 and 1e7 (always with a decimal part) and scientific notation
    /// <c>d.dddE±n</c> outside that range. The digits are the shortest round-trip ones, as in Java 19+.
    /// </summary>
    internal static string JavaDouble(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";

        string r = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int exp10 = 0;
        int e = r.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exp10 = int.Parse(r[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..e];
        }
        int dot = r.IndexOf('.');
        string digits = dot >= 0 ? r.Remove(dot, 1) : r;
        int pointAt = (dot >= 0 ? dot : r.Length) + exp10;
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..].TrimEnd('0');
        if (digits.Length == 0) digits = "0";
        pointAt -= lead;
        int sci = pointAt - 1; // the value is d.ddd × 10^sci

        var sb = new StringBuilder();
        if (value < 0) sb.Append('-');
        if (sci >= -3 && sci < 7)
        {
            if (sci >= 0)
            {
                sb.Append(digits.Length > pointAt ? digits[..pointAt] : digits.PadRight(pointAt, '0'));
                sb.Append('.');
                sb.Append(digits.Length > pointAt ? digits[pointAt..] : "0");
            }
            else
            {
                sb.Append("0.").Append('0', -sci - 1).Append(digits);
            }
        }
        else
        {
            sb.Append(digits[0]).Append('.').Append(digits.Length > 1 ? digits[1..] : "0");
            sb.Append('E').Append(sci.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
