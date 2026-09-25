namespace Azunote;

internal static class ExternalToolExists
{
    public static string? FirstFailure(
        IReadOnlyList<string> rules,
        ExternalToolContext context)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            if (!RuleMatches(rule, context))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool RuleMatches(string rule, ExternalToolContext context)
    {
        var matched = false;
        foreach (var alternative in SplitAlternatives(rule))
        {
            if (alternative.Length == 0)
            {
                continue;
            }

            var negated = alternative[0] == '!';
            var body = (negated ? alternative[1..] : alternative).Trim();
            if (body.Length == 0)
            {
                continue;
            }

            var found = Probe(body, context);
            if (negated)
            {
                found = !found;
            }

            if (found)
            {
                matched = true;
                break;
            }
        }

        return matched;
    }

    private static IEnumerable<string> SplitAlternatives(string rule)
    {
        var start = 0;
        var depth = 0;
        for (var i = 0; i < rule.Length; i++)
        {
            if (rule[i] == '$' && i + 1 < rule.Length && rule[i + 1] == '{')
            {
                depth++;
                i++;
                continue;
            }

            if (rule[i] == '}' && depth > 0)
            {
                depth--;
                continue;
            }

            if (rule[i] != '|' || depth != 0)
            {
                continue;
            }

            yield return rule[start..i].Trim();
            start = i + 1;
        }

        yield return rule[start..].Trim();
    }

    private static bool Probe(string body, ExternalToolContext context)
    {
        var sameDirectory = body.StartsWith("./", StringComparison.Ordinal)
            || body.StartsWith(".\\", StringComparison.Ordinal);
        if (sameDirectory)
        {
            body = body[2..].TrimStart('/', '\\');
        }

        if (sameDirectory)
        {
            if (body.Contains("${", StringComparison.Ordinal))
            {
                body = context.Expand(body);
            }

            return context.DocumentDirname is { } documentDirectory
                && !string.IsNullOrWhiteSpace(body)
                && (Path.IsPathRooted(body)
                    ? ProbePath(body)
                    : MatchesIn(documentDirectory, body));
        }

        if (body.Contains("${", StringComparison.Ordinal))
        {
            var expanded = context.Expand(body);
            return !string.IsNullOrWhiteSpace(expanded) && ProbePath(expanded);
        }

        if (Path.IsPathRooted(body))
        {
            return ProbePath(body);
        }

        var directory = context.DocumentDirname;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (MatchesIn(directory, body))
            {
                return true;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return false;
    }

    private static bool ProbePath(string path)
    {
        if (ContainsGlob(path))
        {
            var split = path.IndexOfAny(['*', '?', '[']);
            var separator = path.LastIndexOfAny(['\\', '/'], split);
            if (separator < 0)
            {
                return false;
            }

            return MatchesIn(path[..separator], path[(separator + 1)..]);
        }

        return File.Exists(path) || Directory.Exists(path);
    }

    private static bool MatchesIn(string directory, string relative)
    {
        if (ContainsGlob(relative))
        {
            return WorkspaceFolderResolver.MatchesPattern(directory, relative);
        }

        var fullPath = Path.Combine(directory, relative);
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    private static bool ContainsGlob(string value) =>
        value.IndexOfAny(['*', '?', '[']) >= 0;
}
