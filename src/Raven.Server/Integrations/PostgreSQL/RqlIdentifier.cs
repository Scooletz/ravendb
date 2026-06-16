namespace Raven.Server.Integrations.PostgreSQL
{
    internal static class RqlIdentifier
    {
        // A bare RQL identifier (field, alias, load path) is spliced into query text with no quoting,
        // so only a plain ASCII identifier is safe: letter/underscore first, then letters/digits/underscores.
        // Callers reject anything else (e.g. `Field With Space`) and fall through rather than emit bad RQL.
        public static bool IsSafe(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            if (char.IsAsciiLetter(s[0]) == false && s[0] != '_')
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsAsciiLetterOrDigit(c) == false && c != '_')
                    return false;
            }

            return true;
        }
    }
}
