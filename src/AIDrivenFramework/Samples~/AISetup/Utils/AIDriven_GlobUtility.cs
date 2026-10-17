using System.Text.RegularExpressions;

public static class AIDriven_GlobUtility
{
    public static Regex GlobToRegex(string glob)
    {
        string pattern = Regex.Escape(glob)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");

        pattern = "^" + pattern + "$";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
