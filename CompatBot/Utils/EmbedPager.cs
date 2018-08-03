using System;
using System.Collections.Generic;
using System.Text;
using CompatApiClient.Utils;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    internal class EmbedPager
    {
        private const int MaxFieldLength = 1024;
        private const int MaxTitleSize = 256;
        private const int MaxFields = 25;

        public IEnumerable<DiscordEmbed> BreakInEmbeds(DiscordEmbedBuilder builder, IEnumerable<string> lines, int maxLinesPerField = 10)
        {
            var fieldCount = 0;
            foreach (var field in BreakInFieldContent(lines, maxLinesPerField))
            {
                if (fieldCount == MaxFields)
                {
                    yield return builder.Build();
                    builder.ClearFields();
                    fieldCount = 0;
                }
                builder.AddField(field.title.Trim(MaxTitleSize).ToUpperInvariant(), field.content, true);
                fieldCount++;
            }
            if (fieldCount > 0)
                yield return builder.Build();
        }

        private IEnumerable<(string title, string content)> BreakInFieldContent(IEnumerable<string> lines, int maxLinesPerField = 10)
        {
            if (maxLinesPerField < 1)
                throw new ArgumentException("Expected a number greater than 0, but was " + maxLinesPerField, nameof(maxLinesPerField));

            var buffer = new StringBuilder();
            var lineCount = 0;
            string firstLine = null;
            string lastLine = null;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(firstLine))
                    firstLine = line;

                if (lineCount == maxLinesPerField)
                {
                    yield return (MakeTitle(firstLine, lastLine), buffer.ToString());
                    buffer.Clear();
                    lineCount = 0;
                    firstLine = line;
                }

                if (buffer.Length + line.Length + Environment.NewLine.Length > MaxFieldLength)
                {
                    if (buffer.Length + line.Length > MaxFieldLength)
                    {
                        if (buffer.Length == 0)
                            yield return (MakeTitle(line, line), line.Trim(MaxFieldLength));
                        else
                        {
                            yield return (MakeTitle(firstLine, lastLine), buffer.ToString());
                            buffer.Clear().Append(line);
                            lineCount = 1;
                            firstLine = line;
                        }
                    }
                    else
                    {
                        yield return (MakeTitle(firstLine, line), buffer.Append(line).ToString());
                        buffer.Clear();
                        lineCount = 0;
                    }
                }
                else
                {
                    if (buffer.Length > 0)
                        buffer.AppendLine();
                    buffer.Append(line);
                    lineCount++;
                    lastLine = line;
                }
            }
            if (buffer.Length > 0)
                yield return (MakeTitle(firstLine, lastLine), buffer.ToString());
        }

        private static string MakeTitle(string first, string last)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last))
                return first + last;

            if (first == last)
                return first;

            if (last.StartsWith(first))
                return $"{first} - {last}";

            var commonPrefix = "";
            var maxPrefixSize = Math.Min(Math.Min(first.Length, last.Length), MaxTitleSize/2);
            for (var i = 0; i < maxPrefixSize; i++)
            {
                if (first[i] == last[i])
                    commonPrefix += first[i];
                else
                    return $"{commonPrefix}{first[i]}-{commonPrefix}{last[i]}";
            }
            return commonPrefix;
        }
    }
}
