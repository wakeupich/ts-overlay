using System.Text;

namespace TsOverlay.Services;

/// <summary>Разбор ответов TS3 ClientQuery: экранирование, пары ключ=значение, список элементов.</summary>
public static class Ts3Protocol
{
    public static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                continue;
            }

            var n = value[++i];
            sb.Append(n switch
            {
                's' => ' ',
                'p' => '|',
                '/' => '/',
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                'f' => '\f',
                'a' => '\a',
                'b' => '\b',
                _ => n,
            });
        }

        return sb.ToString();
    }

    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '/': sb.Append("\\/"); break;
                case ' ': sb.Append("\\s"); break;
                case '|': sb.Append("\\p"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    public static Dictionary<string, string> ParseFields(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var eq = token.IndexOf('=');
            if (eq < 0)
            {
                result[token] = "";
                continue;
            }

            var key = token[..eq];
            var value = token[(eq + 1)..];
            result[key] = Unescape(value);
        }

        return result;
    }

    /// <summary>Возвращает элементы списка (разделитель «|») и код ошибки из завершающей строки.</summary>
    public static (List<Dictionary<string, string>> Items, int ErrorId, string ErrorMessage) ParseResponse(string response)
    {
        var items = new List<Dictionary<string, string>>();
        var errorId = 0;
        var errorMessage = "";

        var dataLines = new List<string>();
        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim('\r', ' ');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("error id=", StringComparison.Ordinal))
            {
                var fields = ParseFields(line);
                if (fields.TryGetValue("id", out var id))
                {
                    int.TryParse(id, out errorId);
                }

                fields.TryGetValue("msg", out errorMessage);
                continue;
            }

            dataLines.Add(line);
        }

        if (dataLines.Count > 0)
        {
            var joined = string.Join(" ", dataLines);
            foreach (var item in joined.Split('|'))
            {
                if (item.Trim().Length == 0)
                {
                    continue;
                }

                items.Add(ParseFields(item.Trim()));
            }
        }

        return (items, errorId, errorMessage ?? "");
    }

    public static int Int(Dictionary<string, string> fields, string key, int fallback = 0)
    {
        if (fields.TryGetValue(key, out var raw) && int.TryParse(raw, out var value))
        {
            return value;
        }

        return fallback;
    }

    public static bool Bool(Dictionary<string, string> fields, string key)
        => Int(fields, key) != 0;

    public static string Str(Dictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var raw) ? raw : "";

    /// <summary>Кодовая страница TS3 ClientQuery — UTF-8, но старые сборки могут прислать cp1251.</summary>
    public static Encoding WireEncoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
}
