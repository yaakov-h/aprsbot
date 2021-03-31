using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace AprsBot
{
    record AprsPacket(AprsHeader Header, string BodyText)
    {
        // e.g.: "VK2BSD-5>APFII0,qAC,APRSFI::VK2BSD-XX:test message{21E3C";
        public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out AprsPacket packet)
        {
            var indexOfSeparator = text.IndexOf(':');
            if (indexOfSeparator < 0)
            {
                packet = default;
                return false;
            }

            var headerText = text[0..indexOfSeparator];
            var bodyText = text[(indexOfSeparator + 1)..];

            var indexOfArrow = headerText.IndexOf('>');
            if (indexOfArrow < 0)
            {
                packet = default;
                return false;
            }

            var fromCall = headerText[..indexOfArrow];
            var path = headerText[(indexOfArrow + 1)..].Split(',');

            var header = new AprsHeader(fromCall.ToString(), path);
            packet = new AprsPacket(header, bodyText.ToString());
            return true;
        }
    }
}
