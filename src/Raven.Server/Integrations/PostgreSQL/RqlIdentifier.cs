namespace Raven.Server.Integrations.PostgreSQL
{
    internal static class RqlIdentifier
    {
        // A bare RQL identifier (field, alias, load path) is spliced into query text unquoted, so it must be
        // identifier-shaped - letter/underscore then letters/digits/underscores, with Unicode letters to match
        // the RQL scanner. Names needing quoting (spaces, punctuation) are rejected; RQL can't quote identifiers.
        public static bool IsSafe(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            if (char.IsLetter(s[0]) == false && s[0] != '_')
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsLetterOrDigit(c) == false && c != '_')
                    return false;
            }

            return true;
        }
    }
}
