using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public class AIDrivenFW_FileFilter
{
    string[] pattern;
    public List<Regex> regexList { private set; get; }

    public AIDrivenFW_FileFilter(IEnumerable<string> globs)
    {
        pattern = globs.ToArray();
        regexList = globs
            .Select(g => AIDriven_GlobUtility.GlobToRegex(g))
            .ToList();
    }

    public bool Match(string fileName)
    {
        foreach (var regex in regexList)
        {
            if (regex.IsMatch(fileName))
                return true;
        }

        return false;
    }

    public string GetGlobsAsString()
    {
        return string.Join(", ", pattern.Select(r => r.ToString()));
    }
}
