using System;
using System.Collections.Generic;
using System.Text;

namespace AIDrivenFW.Core
{
    internal static class ProcessArgumentParser
    {
        internal static IReadOnlyList<string> Parse(string arguments)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return result;
            }

            var current = new StringBuilder();
            char quote = '\0';
            bool tokenStarted = false;

            for (int index = 0; index < arguments.Length; index++)
            {
                char character = arguments[index];

                if (character == '\\' && index + 1 < arguments.Length)
                {
                    char next = arguments[index + 1];
                    if (next == '\'' || next == '"' || next == '\\')
                    {
                        current.Append(next);
                        tokenStarted = true;
                        index++;
                        continue;
                    }
                }

                if (character == '\'' || character == '"')
                {
                    if (quote == '\0')
                    {
                        quote = character;
                        tokenStarted = true;
                        continue;
                    }

                    if (quote == character)
                    {
                        quote = '\0';
                        continue;
                    }
                }

                if (char.IsWhiteSpace(character) && quote == '\0')
                {
                    if (tokenStarted)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        tokenStarted = false;
                    }
                    continue;
                }

                current.Append(character);
                tokenStarted = true;
            }

            if (quote != '\0')
            {
                throw new FormatException("Process arguments contain an unterminated quoted value.");
            }

            if (tokenStarted)
            {
                result.Add(current.ToString());
            }

            return result;
        }
    }
}
