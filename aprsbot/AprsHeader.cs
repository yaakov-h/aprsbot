using System.Collections.Immutable;

namespace AprsBot
{
    record AprsHeader(string FromCall, ImmutableArray<string> Path);
}
