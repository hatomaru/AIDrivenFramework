using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// UnityのJsonUtilityでは扱えない任意形状のJSON Schema用の小さなJSON DOM。
    /// </summary>
    internal static class StructuredOutputJson
    {
        internal static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhiteSpace();
            if (!parser.IsAtEnd)
            {
                throw parser.Error("Unexpected trailing characters.");
            }
            return value;
        }

        internal static string Serialize(object value)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        internal static string Quote(string value)
        {
            var builder = new StringBuilder();
            WriteString(builder, value ?? string.Empty);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    return;
                case string text:
                    WriteString(builder, text);
                    return;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    return;
                case IDictionary<string, object> map:
                    builder.Append('{');
                    bool firstProperty = true;
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        if (!firstProperty) builder.Append(',');
                        firstProperty = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        WriteValue(builder, pair.Value);
                    }
                    builder.Append('}');
                    return;
                case IEnumerable sequence when value is not string:
                    builder.Append('[');
                    bool firstItem = true;
                    foreach (object item in sequence)
                    {
                        if (!firstItem) builder.Append(',');
                        firstItem = false;
                        WriteValue(builder, item);
                    }
                    builder.Append(']');
                    return;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case float single:
                    if (float.IsNaN(single) || float.IsInfinity(single)) throw new FormatException("JSON does not support non-finite numbers.");
                    builder.Append(single.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double number:
                    if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("JSON does not support non-finite numbers.");
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal decimalNumber:
                    builder.Append(decimalNumber.ToString(CultureInfo.InvariantCulture));
                    return;
                default:
                    throw new FormatException($"Unsupported JSON value type: {value.GetType().FullName}.");
            }
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string source;
            private int index;

            internal Parser(string source)
            {
                this.source = source;
            }

            internal bool IsAtEnd => index >= source.Length;

            internal object ParseValue()
            {
                SkipWhiteSpace();
                if (IsAtEnd) throw Error("A JSON value was expected.");

                return source[index] switch
                {
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    '"' => ParseString(),
                    't' => ParseLiteral("true", true),
                    'f' => ParseLiteral("false", false),
                    'n' => ParseLiteral("null", null),
                    '-' => ParseNumber(),
                    >= '0' and <= '9' => ParseNumber(),
                    _ => throw Error($"Unexpected character '{source[index]}'.")
                };
            }

            internal void SkipWhiteSpace()
            {
                while (!IsAtEnd && char.IsWhiteSpace(source[index])) index++;
            }

            internal FormatException Error(string message)
            {
                return new FormatException($"{message} (position {index})");
            }

            private Dictionary<string, object> ParseObject()
            {
                index++;
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhiteSpace();
                if (Consume('}')) return result;

                while (true)
                {
                    SkipWhiteSpace();
                    if (IsAtEnd || source[index] != '"') throw Error("An object property name was expected.");
                    string key = ParseString();
                    SkipWhiteSpace();
                    Expect(':');
                    if (result.ContainsKey(key)) throw Error($"Duplicate property '{key}'.");
                    result.Add(key, ParseValue());
                    SkipWhiteSpace();
                    if (Consume('}')) return result;
                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                index++;
                var result = new List<object>();
                SkipWhiteSpace();
                if (Consume(']')) return result;

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhiteSpace();
                    if (Consume(']')) return result;
                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (!IsAtEnd)
                {
                    char character = source[index++];
                    if (character == '"') return builder.ToString();
                    if (character < 0x20) throw Error("Unescaped control character in string.");
                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (IsAtEnd) throw Error("Incomplete escape sequence.");
                    char escaped = source[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u': builder.Append(ParseUnicodeEscape()); break;
                        default: throw Error($"Unsupported escape sequence '\\{escaped}'.");
                    }
                }
                throw Error("Unterminated string.");
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > source.Length) throw Error("Incomplete unicode escape.");
                string digits = source.Substring(index, 4);
                if (!ushort.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
                {
                    throw Error("Invalid unicode escape.");
                }
                index += 4;
                return (char)value;
            }

            private object ParseNumber()
            {
                int start = index;
                if (source[index] == '-') index++;
                if (IsAtEnd) throw Error("Incomplete number.");

                if (source[index] == '0')
                {
                    index++;
                }
                else
                {
                    if (!char.IsDigit(source[index])) throw Error("Invalid number.");
                    while (!IsAtEnd && char.IsDigit(source[index])) index++;
                }

                bool floatingPoint = false;
                if (!IsAtEnd && source[index] == '.')
                {
                    floatingPoint = true;
                    index++;
                    if (IsAtEnd || !char.IsDigit(source[index])) throw Error("Invalid fraction.");
                    while (!IsAtEnd && char.IsDigit(source[index])) index++;
                }

                if (!IsAtEnd && (source[index] == 'e' || source[index] == 'E'))
                {
                    floatingPoint = true;
                    index++;
                    if (!IsAtEnd && (source[index] == '+' || source[index] == '-')) index++;
                    if (IsAtEnd || !char.IsDigit(source[index])) throw Error("Invalid exponent.");
                    while (!IsAtEnd && char.IsDigit(source[index])) index++;
                }

                string token = source.Substring(start, index - start);
                if (!floatingPoint && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) return integer;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return number;
                throw Error("Invalid number.");
            }

            private object ParseLiteral(string literal, object value)
            {
                if (index + literal.Length > source.Length ||
                    !string.Equals(source.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                {
                    throw Error($"Expected '{literal}'.");
                }
                index += literal.Length;
                return value;
            }

            private bool Consume(char expected)
            {
                if (!IsAtEnd && source[index] == expected)
                {
                    index++;
                    return true;
                }
                return false;
            }

            private void Expect(char expected)
            {
                if (!Consume(expected)) throw Error($"Expected '{expected}'.");
            }
        }
    }
}
