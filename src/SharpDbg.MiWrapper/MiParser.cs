namespace SharpDbg.MiWrapper;

/// <summary>
/// Parses a single GDB/MI command line into its components.
/// Format: [token] "-" command [" " options/args...]
/// e.g.  "3-break-insert --source foo.cs --line 42"
///       "7-exec-run"
/// </summary>
internal sealed class MiCommand
{
    public string Token { get; init; } = "";
    public string Name { get; init; } = "";   // e.g. "break-insert"
    public string Raw { get; init; } = "";    // everything after the command name
    public Dictionary<string, string> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; init; } = new();

    public static MiCommand Parse(string line)
    {
        line = line.Trim();
        var token = "";
        // Token = leading digits
        var i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (i > 0 && i < line.Length && line[i] == '-')
        {
            token = line[..i];
            line = line[(i + 1)..];
        }
        else if (line.StartsWith('-'))
        {
            line = line[1..];
        }

        // Command name = up to first space
        var sp = line.IndexOf(' ');
        string name, rest;
        if (sp < 0) { name = line; rest = ""; }
        else { name = line[..sp]; rest = line[(sp + 1)..].Trim(); }

        // Parse options: --key[=value] or --key "value" positional
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pos = new List<string>();
        ParseArgs(rest, opts, pos);

        return new MiCommand { Token = token, Name = name, Raw = rest, Options = opts, Positional = pos };
    }

    private static void ParseArgs(string s, Dictionary<string, string> opts, List<string> pos)
    {
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;

            if (s[i] == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                // --key or --key=value or --key "value"
                i += 2;
                var keyStart = i;
                while (i < s.Length && s[i] != '=' && !char.IsWhiteSpace(s[i])) i++;
                var key = s[keyStart..i];
                if (i < s.Length && s[i] == '=')
                {
                    i++;
                    var val = ReadToken(s, ref i);
                    opts[key] = val;
                }
                else
                {
                    // Check if next token is a value (doesn't start with --)
                    var saved = i;
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    if (i < s.Length && !(s[i] == '-' && i + 1 < s.Length && s[i + 1] == '-'))
                    {
                        // Treat as flag-only option (boolean true)
                        opts[key] = "true";
                        i = saved;
                    }
                    else
                    {
                        opts[key] = "true";
                        i = saved;
                    }
                }
            }
            else
            {
                pos.Add(ReadToken(s, ref i));
            }
        }
    }

    private static string ReadToken(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) return "";
        if (s[i] == '"')
        {
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i]); }
                else sb.Append(s[i]);
                i++;
            }
            if (i < s.Length) i++; // closing "
            return sb.ToString();
        }
        var start = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        return s[start..i];
    }
}
