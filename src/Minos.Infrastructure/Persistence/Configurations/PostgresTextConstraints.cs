namespace Minos.Infrastructure.Persistence.Configurations;

internal static class PostgresTextConstraints
{
    // Match .NET trimming and UTF-16 limits, including non-BMP profile/problem text.
    internal const string Whitespace = "E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000'";

    internal static string Utf16Length(string column) =>
        $"(length({column}) + length(regexp_replace({column}, U&'[^\\+010000-\\+10FFFF]', '', 'g')))";
}
