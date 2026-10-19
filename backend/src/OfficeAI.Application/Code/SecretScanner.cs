using System.Text.RegularExpressions;

namespace OfficeAI.Application.Code;

/// <param name="Rule">What it looks like; the matched value itself is never returned.</param>
public record SecretFinding(string File, int Line, string Rule);

/// <summary>
/// Static check for credentials written into code. Scans only the lines a change adds, so existing history is not re-reported.
/// Deliberately simple and deterministic: a hit blocks the merge request until the value is moved to configuration.
/// </summary>
public static partial class SecretScanner
{
    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("private-key", PrivateKey()),
        ("aws-access-key", Aws()),
        ("anthropic-key", Anthropic()),
        ("openai-key", OpenAi()),
        ("github-token", GitHub()),
        ("gitlab-token", GitLab()),
        ("google-api-key", Google()),
        ("slack-token", Slack()),
        ("hardcoded-credential", Assignment()),
    ];

    /// <summary>Scans a unified diff (git diff output) and reports added lines that look like secrets.</summary>
    public static List<SecretFinding> ScanDiff(string diff)
    {
        var findings = new List<SecretFinding>();
        string? file = null;
        var line = 0;
        foreach (var raw in diff.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("+++ "))
            {
                file = l[4..] is "/dev/null" ? null : l[4..].StartsWith("b/") ? l[6..] : l[4..];
                continue;
            }
            if (l.StartsWith("@@"))
            {
                var m = HunkHeader().Match(l);
                line = m.Success ? int.Parse(m.Groups[1].Value) - 1 : 0;
                continue;
            }
            if (file is null || l.StartsWith("--- ")) continue;
            if (l.StartsWith('+'))
            {
                line++;
                var text = l[1..];
                foreach (var (rule, pattern) in Rules)
                    if (pattern.IsMatch(text) && !IsPlaceholder(text))
                    {
                        findings.Add(new SecretFinding(file, line, rule));
                        break;
                    }
            }
            else if (!l.StartsWith('-')) line++;
        }
        return findings;
    }

    /// <summary>Example values and environment lookups are not secrets.</summary>
    private static bool IsPlaceholder(string text) => Placeholder().IsMatch(text);

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)")]
    private static partial Regex HunkHeader();
    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY")]
    private static partial Regex PrivateKey();
    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex Aws();
    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9_-]{20,}")]
    private static partial Regex Anthropic();
    [GeneratedRegex(@"\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}")]
    private static partial Regex OpenAi();
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})")]
    private static partial Regex GitHub();
    [GeneratedRegex(@"\bglpat-[A-Za-z0-9_-]{20,}")]
    private static partial Regex GitLab();
    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{35}\b")]
    private static partial Regex Google();
    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}")]
    private static partial Regex Slack();
    [GeneratedRegex(@"(?i)\b(?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token|client[_-]?secret)\b[""']?\s*[:=]\s*[""'][^""'\s]{8,}[""']")]
    private static partial Regex Assignment();
    [GeneratedRegex(@"(?i)(?:example|placeholder|changeme|change-me|your[_-]|<[^>]+>|\$\{|process\.env|environment\.getenv|os\.environ|getenv\(|xxxx|\*\*\*\*|dummy|test[_-]?(?:key|secret|password))")]
    private static partial Regex Placeholder();
}
