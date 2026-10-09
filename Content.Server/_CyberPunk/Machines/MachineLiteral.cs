using System.Collections;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._CyberPunk.Machines;

/// <summary>
/// Values passed between programs and machines, as text the way Wire's <c>repr</c> writes them: <c>None</c>,
/// <c>True</c>, <c>False</c>, whole numbers, <c>"text"</c>, <c>[lists]</c> and <c>{"dicts": 1}</c>. Machines'
/// own values are written this way for programs to read, and what programs send is read back into the types a
/// machine's UI messages take.
/// </summary>
public static class MachineLiteral
{
    /// <summary>The most text a value can take, written out.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>How deep values nest before the rest is left out.</summary>
    public const int MaxDepth = 8;

    /// <summary>The most items written from one list or dict.</summary>
    public const int MaxItems = 256;

    /// <summary>
    /// A dict as read: its keys and values in order.
    /// </summary>
    public sealed class Dict : List<KeyValuePair<object?, object?>>
    {
        public bool TryGet(string key, out object? value)
        {
            foreach (var (k, v) in this)
            {
                if (k is string s && Same(s, key))
                {
                    value = v;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }

    #region Reading

    /// <summary>
    /// Reads a value: null, a bool, a long, a string, a <see cref="List{T}"/> or a <see cref="Dict"/>.
    /// </summary>
    /// <exception cref="FormatException">It isn't a value.</exception>
    public static object? Parse(string text)
    {
        var at = 0;
        var value = ParseValue(text, ref at, 0);
        SkipSpace(text, ref at);
        if (at != text.Length)
            throw new FormatException($"unexpected {Describe(text, at)}");

        return value;
    }

    private static object? ParseValue(string text, ref int at, int depth)
    {
        if (depth > 32)
            throw new FormatException("nested too deep");

        SkipSpace(text, ref at);
        if (at >= text.Length)
            throw new FormatException("missing a value");

        if (Word(text, ref at, "None"))
            return null;
        if (Word(text, ref at, "True"))
            return true;
        if (Word(text, ref at, "False"))
            return false;

        var c = text[at];
        if (c == '"')
            return ParseString(text, ref at);

        if (c == '[')
        {
            at++;
            var list = new List<object?>();
            SkipSpace(text, ref at);
            if (at < text.Length && text[at] == ']')
            {
                at++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue(text, ref at, depth + 1));
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    continue;
                }

                Expect(text, ref at, ']');
                return list;
            }
        }

        if (c == '{')
        {
            at++;
            var dict = new Dict();
            SkipSpace(text, ref at);
            if (at < text.Length && text[at] == '}')
            {
                at++;
                return dict;
            }

            while (true)
            {
                var key = ParseValue(text, ref at, depth + 1);
                SkipSpace(text, ref at);
                Expect(text, ref at, ':');
                dict.Add(new KeyValuePair<object?, object?>(key, ParseValue(text, ref at, depth + 1)));
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    continue;
                }

                Expect(text, ref at, '}');
                return dict;
            }
        }

        var start = at;
        if (c == '-')
            at++;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at > start && char.IsAsciiDigit(text[at - 1])
            && long.TryParse(text.AsSpan(start, at - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        at = start;
        throw new FormatException($"unexpected {Describe(text, at)}");
    }

    private static string ParseString(string text, ref int at)
    {
        at++;
        var output = new StringBuilder();
        while (at < text.Length)
        {
            var c = text[at++];
            if (c == '"')
                return output.ToString();

            if (c != '\\')
            {
                output.Append(c);
                continue;
            }

            if (at >= text.Length)
                break;

            var e = text[at++];
            output.Append(e switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '0' => '\0',
                _ => e,
            });
        }

        throw new FormatException("text with no closing \"");
    }

    private static void SkipSpace(string text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }
    }

    private static bool Word(string text, ref int at, string word)
    {
        if (string.CompareOrdinal(text, at, word, 0, word.Length) != 0)
            return false;

        at += word.Length;
        return true;
    }

    private static void Expect(string text, ref int at, char c)
    {
        if (at >= text.Length || text[at] != c)
            throw new FormatException($"expected '{c}' but found {Describe(text, at)}");

        at++;
    }

    private static string Describe(string text, int at)
    {
        return at >= text.Length ? "the end" : $"'{text[at]}'";
    }

    #endregion

    #region Writing

    /// <summary>
    /// Writes a machine's value for a program, as far as it goes: numbers with fractions are rounded, enums
    /// and prototype ids are text, entities are their numbers, and any other object is a dict of its public
    /// fields and properties. Past <see cref="MaxDepth"/>, or past <see cref="MaxLength"/> in all, the rest is
    /// left out.
    /// </summary>
    /// <param name="entity">Turns an entity into its number, which programs can send back.</param>
    public static string Write(object? value, Func<EntityUid, int> entity)
    {
        var output = new StringBuilder();
        WriteValue(output, value, entity, 0);
        return output.ToString();
    }

    private static void WriteValue(StringBuilder output, object? value, Func<EntityUid, int> entity, int depth)
    {
        if (output.Length > MaxLength)
        {
            output.Append("None");
            return;
        }

        switch (value)
        {
            case null:
                output.Append("None");
                return;
            case bool b:
                output.Append(b ? "True" : "False");
                return;
            case string s:
                WriteString(output, s);
                return;
            case char c:
                WriteString(output, c.ToString());
                return;
            case Enum e:
                WriteString(output, e.ToString());
                return;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                output.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case float or double or decimal:
                output.Append(((long) Math.Round(System.Convert.ToDouble(value, CultureInfo.InvariantCulture))).ToString(CultureInfo.InvariantCulture));
                return;
            case FixedPoint2 f:
                output.Append(((long) Math.Round(f.Double())).ToString(CultureInfo.InvariantCulture));
                return;
            case NetEntity n:
                output.Append(n.Id.ToString(CultureInfo.InvariantCulture));
                return;
            case EntityUid uid:
                output.Append(entity(uid).ToString(CultureInfo.InvariantCulture));
                return;
            case TimeSpan t:
                output.Append(((long) t.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
                return;
            case Color color:
                WriteString(output, color.ToHex());
                return;
            case Vector2 v:
                WriteValue(output, new[] { v.X, v.Y }, entity, depth);
                return;
            case Vector2i v:
                WriteValue(output, new[] { v.X, v.Y }, entity, depth);
                return;
            case Type or Delegate or IntPtr or MemberInfo:
                output.Append("None");
                return;
        }

        var type = value.GetType();
        if (IsProtoId(type))
        {
            WriteString(output, value.ToString() ?? "");
            return;
        }

        if (depth >= MaxDepth)
        {
            output.Append("None");
            return;
        }

        if (value is IDictionary dictionary)
        {
            output.Append('{');
            var n = 0;
            foreach (DictionaryEntry pair in dictionary)
            {
                if (n == MaxItems)
                    break;

                if (n++ > 0)
                    output.Append(", ");

                WriteKey(output, pair.Key, entity);
                output.Append(": ");
                WriteValue(output, pair.Value, entity, depth + 1);
            }

            output.Append('}');
            return;
        }

        if (value is IEnumerable items)
        {
            output.Append('[');
            var n = 0;
            foreach (var item in items)
            {
                if (n == MaxItems)
                    break;

                if (n++ > 0)
                    output.Append(", ");

                WriteValue(output, item, entity, depth + 1);
            }

            output.Append(']');
            return;
        }

        output.Append('{');
        var first = true;
        foreach (var (name, member) in Members(type))
        {
            object? memberValue;
            try
            {
                memberValue = member switch
                {
                    FieldInfo field => field.GetValue(value),
                    PropertyInfo property => property.GetValue(value),
                    _ => null,
                };
            }
            catch (Exception)
            {
                continue;
            }

            if (!first)
                output.Append(", ");

            first = false;
            WriteString(output, name);
            output.Append(": ");
            WriteValue(output, memberValue, entity, depth + 1);
        }

        output.Append('}');
    }

    private static void WriteKey(StringBuilder output, object? key, Func<EntityUid, int> entity)
    {
        switch (key)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong or NetEntity or EntityUid or bool:
                WriteValue(output, key, entity, MaxDepth);
                return;
            default:
                WriteString(output, key?.ToString() ?? "None");
                return;
        }
    }

    private static void WriteString(StringBuilder output, string s)
    {
        output.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':
                    output.Append("\\\"");
                    break;
                case '\\':
                    output.Append("\\\\");
                    break;
                case '\n':
                    output.Append("\\n");
                    break;
                case '\t':
                    output.Append("\\t");
                    break;
                case '\r':
                    output.Append("\\r");
                    break;
                case '\0':
                    output.Append("\\0");
                    break;
                default:
                    output.Append(c);
                    break;
            }
        }

        output.Append('"');
    }

    /// <summary>
    /// The public instance fields and readable properties of a type that a program gets to see, by name. The
    /// ones every UI message and component state have, which say nothing about the machine, are left out.
    /// </summary>
    private static IEnumerable<(string Name, MemberInfo Member)> Members(Type type)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!Hidden(field.DeclaringType))
                yield return (field.Name, field);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0 && !Hidden(property.DeclaringType))
                yield return (property.Name, property);
        }
    }

    private static bool Hidden(Type? declaring)
    {
        return declaring == typeof(BoundUserInterfaceMessage)
               || declaring == typeof(BaseBoundUserInterfaceEvent)
               || declaring == typeof(EntityEventArgs)
               || declaring == typeof(ComponentState)
               || declaring == typeof(BoundUserInterfaceState)
               || declaring == typeof(object);
    }

    private static bool IsProtoId(Type type)
    {
        return type == typeof(EntProtoId)
               || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(ProtoId<>)
                                         || type.GetGenericTypeDefinition() == typeof(EntProtoId<>));
    }

    #endregion

    #region Converting

    /// <summary>
    /// Turns a value a program sent into <paramref name="type"/>.
    /// </summary>
    /// <param name="entity">Turns an entity's number back into the entity, if there is one.</param>
    /// <exception cref="FormatException">It can't be one; the message says why.</exception>
    public static object? Convert(object? value, Type type, Func<int, NetEntity?> entity)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return value == null ? null : Convert(value, inner, entity);

        if (value == null)
        {
            if (!type.IsValueType)
                return null;

            throw new FormatException($"needs {Describe(type)}, not None");
        }

        if (type == typeof(object))
            return value;

        if (type == typeof(string))
            return value as string ?? throw Wrong(value, type);

        if (type == typeof(bool))
            return value as bool? ?? throw Wrong(value, type);

        if (type.IsEnum)
        {
            if (value is string name && Enum.TryParse(type, name, true, out var parsed) && Enum.IsDefined(type, parsed!))
                return parsed;

            if (value is long n && Enum.IsDefined(type, System.Convert.ChangeType(n, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture)))
                return Enum.ToObject(type, n);

            throw new FormatException($"needs {Describe(type)}");
        }

        if (value is long number)
        {
            try
            {
                if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                    || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte)
                    || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                {
                    return System.Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                }
            }
            catch (OverflowException)
            {
                throw new FormatException($"{number} is out of range");
            }

            if (type == typeof(FixedPoint2))
                return FixedPoint2.New((int) Math.Clamp(number, int.MinValue, int.MaxValue));

            if (type == typeof(TimeSpan))
                return TimeSpan.FromMilliseconds(number);

            if (type == typeof(NetEntity))
                return entity((int) Math.Clamp(number, int.MinValue, int.MaxValue)) ?? throw new FormatException($"there's no entity {number}");
        }

        if (value is string text)
        {
            if (IsProtoId(type))
                return Activator.CreateInstance(type, text);

            if (type == typeof(Color))
                return Color.TryFromHex(text, out var color) ? color : throw new FormatException($"\"{text}\" isn't a color like \"#ff8800\"");
        }

        if (value is List<object?> list)
        {
            if (type == typeof(Vector2) && list.Count == 2)
                return new Vector2((float) Number(list[0]), (float) Number(list[1]));

            if (type == typeof(Vector2i) && list.Count == 2)
                return new Vector2i((int) Number(list[0]), (int) Number(list[1]));

            if (type.IsArray)
            {
                var element = type.GetElementType()!;
                var array = Array.CreateInstance(element, list.Count);
                for (var i = 0; i < list.Count; i++)
                {
                    array.SetValue(Convert(list[i], element, entity), i);
                }

                return array;
            }

            if (type.IsGenericType && type.GetGenericArguments().Length == 1
                && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(HashSet<>)
                    || type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) || type.GetGenericTypeDefinition() == typeof(IList<>)
                    || type.GetGenericTypeDefinition() == typeof(IEnumerable<>) || type.GetGenericTypeDefinition() == typeof(ICollection<>)
                    || type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)))
            {
                var element = type.GetGenericArguments()[0];
                var concrete = type.GetGenericTypeDefinition() == typeof(HashSet<>)
                    ? typeof(HashSet<>).MakeGenericType(element)
                    : typeof(List<>).MakeGenericType(element);
                var collection = Activator.CreateInstance(concrete)!;
                var add = concrete.GetMethod("Add")!;
                foreach (var item in list)
                {
                    add.Invoke(collection, new[] { Convert(item, element, entity) });
                }

                return collection;
            }
        }

        if (value is Dict dict && type.IsGenericType && type.GetGenericArguments().Length == 2
            && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                || type.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
        {
            var args = type.GetGenericArguments();
            var concrete = typeof(Dictionary<,>).MakeGenericType(args);
            var dictionary = (IDictionary) Activator.CreateInstance(concrete)!;
            foreach (var (k, v) in dict)
            {
                dictionary[Convert(k, args[0], entity)!] = Convert(v, args[1], entity);
            }

            return dictionary;
        }

        throw Wrong(value, type);
    }

    private static double Number(object? value)
    {
        return value is long n ? n : throw new FormatException("needs a number");
    }

    private static FormatException Wrong(object value, Type type)
    {
        return new FormatException($"needs {Describe(type)}, not {Kind(value)}");
    }

    private static string Kind(object value)
    {
        return value switch
        {
            bool => "True or False",
            long => "a number",
            string => "text",
            List<object?> => "a list",
            Dict => "a dict",
            _ => "that",
        };
    }

    /// <summary>
    /// What a program sends for a type, as a manual would say it.
    /// </summary>
    public static string Describe(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return Describe(inner) + " or None";

        if (type == typeof(string))
            return "text";
        if (type == typeof(bool))
            return "True or False";
        if (type.IsEnum)
            return "one of " + string.Join(", ", Enum.GetNames(type).Select(n => $"\"{n}\""));
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            return "a whole number";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal) || type == typeof(FixedPoint2))
            return "a number";
        if (type == typeof(TimeSpan))
            return "milliseconds";
        if (type == typeof(NetEntity))
            return "an entity's number";
        if (IsProtoId(type))
            return "a prototype id";
        if (type == typeof(Color))
            return "a color like \"#ff8800\"";
        if (type == typeof(Vector2) || type == typeof(Vector2i))
            return "[x, y]";
        if (type.IsArray)
            return "a list of " + Describe(type.GetElementType()!);
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return "a list of " + Describe(type.GetGenericArguments()[0]);
        if (type.IsGenericType && type.GetGenericArguments().Length == 2)
            return "a dict of " + Describe(type.GetGenericArguments()[1]);

        return type.Name;
    }

    /// <summary>
    /// Whether a name a program gave matches a member's: case and underscores don't matter, so <c>item_id</c>
    /// matches <c>ItemId</c>.
    /// </summary>
    public static bool Same(string given, string name)
    {
        return string.Equals(given.Replace("_", ""), name.Replace("_", ""), StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
