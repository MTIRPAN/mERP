using System.Text;

namespace Merp.SharedKernel.Notifications;

/// <summary>
/// The <c>{name}</c> placeholders of a message a user wrote, e.g. a personalized candidate e-mail (D58): the same form
/// as the localized texts, so a text and the values of its notification fit together.
/// </summary>
public static class MessagePlaceholders
{
    /// <summary>The names of the placeholders in <paramref name="text"/>, in order, each once.</summary>
    public static IReadOnlyList<string> Names(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var names = new List<string>();
        Walk(text, name =>
        {
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }

            return null;
        });
        return names;
    }

    /// <summary>Replaces the placeholders with the values; unknown placeholders are left as they are.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        return Walk(text, name => values.TryGetValue(name, out var value) ? value : null);
    }

    private static string Walk(string text, Func<string, string?> replace)
    {
        var builder = new StringBuilder(text.Length + 16);
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf('{', position);
            var close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                builder.Append(text, position, text.Length - position);
                break;
            }

            builder.Append(text, position, open - position);
            var placeholder = text.Substring(open, close - open + 1);
            var name = placeholder[1..^1];

            // A brace that does not close a simple name is text, e.g. "{ ok }"
            var value = name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? replace(name) : null;
            builder.Append(value ?? placeholder);
            position = close + 1;
        }

        return builder.ToString();
    }
}
