using System;
using System.Diagnostics.CodeAnalysis;

namespace AprsBot
{
    static class AprsPacketExtensions
    {
        public static bool TryParseAprsMessage(this AprsPacket packet, [NotNullWhen(true)] out AprsMessage message)
        {
            var indexOfMessage = packet.BodyText.IndexOf(':', startIndex: 1);
            if (indexOfMessage < 0)
            {
                Console.WriteLine($"Invalid APRS message (missing text): {packet.BodyText}");
                message = null;
                return false;
            }

            var indexOfId = packet.BodyText.IndexOf('{', startIndex: indexOfMessage + 1);
            if (indexOfId < 0)
            {
                Console.WriteLine($"Invalid APRS message (missing id): {packet.BodyText}");
                message = null;
                return false;
            }

            var toCall = packet.BodyText[1..indexOfMessage];
            var text = packet.BodyText[(indexOfMessage + 1)..indexOfId];
            var id = packet.BodyText[(indexOfId + 1)..];

            message = new AprsMessage(toCall, text, id);
            return true;
        }
    }
}
