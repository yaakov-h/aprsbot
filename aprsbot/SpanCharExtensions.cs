using System;
using System.Collections.Immutable;

namespace AprsBot
{
    static class SpanCharExtensions
    {
        public static ImmutableArray<string> Split(this ReadOnlySpan<char> text, char delimiter)
        {
            var builder = ImmutableArray.CreateBuilder<string>(initialCapacity: GetNumberOfOccurences(text, delimiter) + 1);

            while (text.Length > 0)
            {
                var nextDelimiter = text.IndexOf(delimiter);
                if (nextDelimiter < 0)
                {
                    builder.Add(text.ToString());
                    text = text[^0..];
                }
                else
                {
                    builder.Add(text[..nextDelimiter].ToString());
                    text = text[(nextDelimiter + 1)..];
                }
            }

            return builder.MoveToImmutable();
        }

        static int GetNumberOfOccurences(ReadOnlySpan<char> text, char character)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == character)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
