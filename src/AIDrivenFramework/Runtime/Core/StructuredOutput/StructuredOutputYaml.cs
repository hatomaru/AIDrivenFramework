using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// JSON Schemaで一般的に使われるYAMLのマッピング、シーケンス、フロー表記をJSON DOMへ変換する。
    /// </summary>
    internal static class StructuredOutputYaml
    {
        internal static object Parse(string yaml)
        {
            if (yaml == null) throw new ArgumentNullException(nameof(yaml));

            List<Line> lines = Tokenize(yaml);
            if (lines.Count == 0) throw new FormatException("The YAML document is empty.");
            if (lines[0].Text == "---") lines.RemoveAt(0);
            if (lines.Count == 0) throw new FormatException("The YAML document is empty.");

            int index = 0;
            object result = ParseNode(lines, ref index, lines[0].Indent);
            if (index != lines.Count)
            {
                throw new FormatException($"Unexpected YAML content on line {lines[index].Number}.");
            }
            return result;
        }

        private static object ParseNode(List<Line> lines, ref int index, int indent)
        {
            if (index >= lines.Count || lines[index].Indent != indent)
            {
                throw new FormatException("Invalid YAML indentation.");
            }

            return lines[index].Text.StartsWith("-", StringComparison.Ordinal)
                ? ParseSequence(lines, ref index, indent)
                : ParseMapping(lines, ref index, indent);
        }

        private static Dictionary<string, object> ParseMapping(List<Line> lines, ref int index, int indent)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            while (index < lines.Count && lines[index].Indent == indent && !lines[index].Text.StartsWith("-", StringComparison.Ordinal))
            {
                Line line = lines[index];
                SplitMapping(line.Text, line.Number, out string rawKey, out string rawValue);
                string key = ParseKey(rawKey, line.Number);
                if (result.ContainsKey(key)) throw new FormatException($"Duplicate YAML key '{key}' on line {line.Number}.");
                index++;

                object value;
                if (rawValue.Length > 0)
                {
                    value = ParseScalarOrFlow(rawValue, line.Number);
                }
                else if (index < lines.Count && lines[index].Indent > indent)
                {
                    value = ParseNode(lines, ref index, lines[index].Indent);
                }
                else
                {
                    value = null;
                }
                result.Add(key, value);
            }
            return result;
        }

        private static List<object> ParseSequence(List<Line> lines, ref int index, int indent)
        {
            var result = new List<object>();
            while (index < lines.Count && lines[index].Indent == indent && lines[index].Text.StartsWith("-", StringComparison.Ordinal))
            {
                Line line = lines[index];
                string remainder = line.Text.Length == 1 ? string.Empty : line.Text.Substring(1).TrimStart();
                index++;

                if (remainder.Length == 0)
                {
                    if (index < lines.Count && lines[index].Indent > indent)
                    {
                        result.Add(ParseNode(lines, ref index, lines[index].Indent));
                    }
                    else
                    {
                        result.Add(null);
                    }
                    continue;
                }

                if (FindMappingColon(remainder) >= 0)
                {
                    var item = new Dictionary<string, object>(StringComparer.Ordinal);
                    ParseSequenceMappingEntry(item, remainder, line.Number, lines, ref index, indent);
                    if (index < lines.Count && lines[index].Indent > indent)
                    {
                        int childIndent = lines[index].Indent;
                        Dictionary<string, object> continuation = ParseMapping(lines, ref index, childIndent);
                        foreach (KeyValuePair<string, object> pair in continuation)
                        {
                            if (item.ContainsKey(pair.Key)) throw new FormatException($"Duplicate YAML key '{pair.Key}'.");
                            item.Add(pair.Key, pair.Value);
                        }
                    }
                    result.Add(item);
                }
                else
                {
                    result.Add(ParseScalarOrFlow(remainder, line.Number));
                    if (index < lines.Count && lines[index].Indent > indent)
                    {
                        throw new FormatException($"A scalar sequence item cannot contain nested content (line {lines[index].Number}).");
                    }
                }
            }
            return result;
        }

        private static void ParseSequenceMappingEntry(
            Dictionary<string, object> target,
            string text,
            int lineNumber,
            List<Line> lines,
            ref int index,
            int sequenceIndent)
        {
            SplitMapping(text, lineNumber, out string rawKey, out string rawValue);
            string key = ParseKey(rawKey, lineNumber);
            object value;
            if (rawValue.Length > 0)
            {
                value = ParseScalarOrFlow(rawValue, lineNumber);
            }
            else if (index < lines.Count && lines[index].Indent > sequenceIndent)
            {
                value = ParseNode(lines, ref index, lines[index].Indent);
            }
            else
            {
                value = null;
            }
            target.Add(key, value);
        }

        private static List<Line> Tokenize(string yaml)
        {
            string normalized = yaml.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] sourceLines = normalized.Split('\n');
            var result = new List<Line>();

            for (int i = 0; i < sourceLines.Length; i++)
            {
                string source = sourceLines[i];
                if (source.IndexOf('\t') >= 0) throw new FormatException($"Tabs are not supported for YAML indentation (line {i + 1}).");
                int indent = 0;
                while (indent < source.Length && source[indent] == ' ') indent++;
                string text = StripComment(source.Substring(indent)).TrimEnd();
                if (string.IsNullOrWhiteSpace(text) || text == "...") continue;
                if (text.StartsWith("%", StringComparison.Ordinal)) continue;
                if (text.StartsWith("*", StringComparison.Ordinal) || text.StartsWith("!", StringComparison.Ordinal))
                {
                    throw new FormatException($"YAML aliases and tags are not supported (line {i + 1}).");
                }
                int mappingColon = FindMappingColon(text);
                string mappingValue = mappingColon < 0 ? string.Empty : text.Substring(mappingColon + 1).Trim();
                if (mappingValue == "|" || mappingValue == ">" || mappingValue.StartsWith("|-", StringComparison.Ordinal) ||
                    mappingValue.StartsWith(">-", StringComparison.Ordinal) || mappingValue.StartsWith("|+", StringComparison.Ordinal) ||
                    mappingValue.StartsWith(">+", StringComparison.Ordinal))
                {
                    throw new FormatException($"YAML block scalars are not supported in JSON Schema sources (line {i + 1}).");
                }
                result.Add(new Line(indent, text, i + 1));
            }
            return result;
        }

        private static string StripComment(string text)
        {
            char quote = '\0';
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (quote == '"' && escaped)
                {
                    escaped = false;
                    continue;
                }
                if (quote == '"' && character == '\\')
                {
                    escaped = true;
                    continue;
                }
                if ((character == '\'' || character == '"'))
                {
                    if (quote == '\0') quote = character;
                    else if (quote == character) quote = '\0';
                    continue;
                }
                if (character == '#' && quote == '\0' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                {
                    return text.Substring(0, i);
                }
            }
            return text;
        }

        private static void SplitMapping(string text, int lineNumber, out string key, out string value)
        {
            int colon = FindMappingColon(text);
            if (colon < 0) throw new FormatException($"A YAML mapping entry was expected on line {lineNumber}.");
            key = text.Substring(0, colon).Trim();
            value = text.Substring(colon + 1).Trim();
            if (key.Length == 0) throw new FormatException($"A YAML mapping key was expected on line {lineNumber}.");
        }

        private static int FindMappingColon(string text)
        {
            char quote = '\0';
            int squareDepth = 0;
            int curlyDepth = 0;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (quote == '"' && escaped) { escaped = false; continue; }
                if (quote == '"' && character == '\\') { escaped = true; continue; }
                if (character == '\'' || character == '"')
                {
                    if (quote == '\0') quote = character;
                    else if (quote == character) quote = '\0';
                    continue;
                }
                if (quote != '\0') continue;
                if (character == '[') squareDepth++;
                else if (character == ']') squareDepth--;
                else if (character == '{') curlyDepth++;
                else if (character == '}') curlyDepth--;
                else if (character == ':' && squareDepth == 0 && curlyDepth == 0 &&
                         (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]))) return i;
            }
            return -1;
        }

        private static string ParseKey(string token, int lineNumber)
        {
            object parsed = ParseScalarOrFlow(token, lineNumber);
            if (parsed is string key) return key;
            return Convert.ToString(parsed, CultureInfo.InvariantCulture);
        }

        private static object ParseScalarOrFlow(string token, int lineNumber)
        {
            token = token.Trim();
            if (token.Length == 0) return null;
            if (token[0] == '[' || token[0] == '{') return new FlowParser(token, lineNumber).Parse();
            if (token[0] == '"') return StructuredOutputJson.Parse(token);
            if (token[0] == '\'')
            {
                if (token.Length < 2 || token[token.Length - 1] != '\'') throw new FormatException($"Unterminated single-quoted scalar on line {lineNumber}.");
                return token.Substring(1, token.Length - 2).Replace("''", "'");
            }

            if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase) || token == "~") return null;
            if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) return integer;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return number;
            return token;
        }

        private readonly struct Line
        {
            internal Line(int indent, string text, int number)
            {
                Indent = indent;
                Text = text;
                Number = number;
            }
            internal int Indent { get; }
            internal string Text { get; }
            internal int Number { get; }
        }

        private sealed class FlowParser
        {
            private readonly string source;
            private readonly int lineNumber;
            private int index;

            internal FlowParser(string source, int lineNumber)
            {
                this.source = source;
                this.lineNumber = lineNumber;
            }

            internal object Parse()
            {
                object value = ParseValue();
                SkipSpaces();
                if (index != source.Length) throw Error("Unexpected flow-style content.");
                return value;
            }

            private object ParseValue()
            {
                SkipSpaces();
                if (index >= source.Length) throw Error("A flow-style value was expected.");
                if (source[index] == '[') return ParseArray();
                if (source[index] == '{') return ParseObject();
                if (source[index] == '"' || source[index] == '\'') return ParseQuoted();

                int start = index;
                while (index < source.Length && source[index] != ',' && source[index] != ']' && source[index] != '}') index++;
                string token = source.Substring(start, index - start).Trim();
                return ParseScalarOrFlow(token, lineNumber);
            }

            private List<object> ParseArray()
            {
                index++;
                var values = new List<object>();
                SkipSpaces();
                if (Consume(']')) return values;
                while (true)
                {
                    values.Add(ParseValue());
                    SkipSpaces();
                    if (Consume(']')) return values;
                    Expect(',');
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                index++;
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipSpaces();
                if (Consume('}')) return values;
                while (true)
                {
                    SkipSpaces();
                    string key = source[index] == '"' || source[index] == '\''
                        ? (string)ParseQuoted()
                        : ParseBareKey();
                    SkipSpaces();
                    Expect(':');
                    if (values.ContainsKey(key)) throw Error($"Duplicate key '{key}'.");
                    values.Add(key, ParseValue());
                    SkipSpaces();
                    if (Consume('}')) return values;
                    Expect(',');
                }
            }

            private object ParseQuoted()
            {
                char quote = source[index++];
                var builder = new StringBuilder();
                while (index < source.Length)
                {
                    char character = source[index++];
                    if (character == quote)
                    {
                        if (quote == '\'' && index < source.Length && source[index] == '\'')
                        {
                            builder.Append('\'');
                            index++;
                            continue;
                        }
                        return builder.ToString();
                    }
                    if (quote == '"' && character == '\\' && index < source.Length)
                    {
                        char escaped = source[index++];
                        builder.Append(escaped switch
                        {
                            'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', _ => escaped
                        });
                    }
                    else builder.Append(character);
                }
                throw Error("Unterminated quoted value.");
            }

            private string ParseBareKey()
            {
                int start = index;
                while (index < source.Length && source[index] != ':') index++;
                if (index >= source.Length) throw Error("A ':' was expected.");
                string key = source.Substring(start, index - start).Trim();
                if (key.Length == 0) throw Error("An object key was expected.");
                return key;
            }

            private void SkipSpaces()
            {
                while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
            }

            private bool Consume(char character)
            {
                if (index < source.Length && source[index] == character) { index++; return true; }
                return false;
            }

            private void Expect(char character)
            {
                SkipSpaces();
                if (!Consume(character)) throw Error($"Expected '{character}'.");
            }

            private FormatException Error(string message)
            {
                return new FormatException($"{message} (YAML line {lineNumber}, position {index})");
            }
        }
    }
}
