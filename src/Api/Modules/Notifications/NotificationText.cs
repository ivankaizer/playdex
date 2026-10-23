using System.Globalization;
using System.Text;

namespace Api.Modules.Notifications;

/// <summary>Makes stored values safe to show in a notification line.</summary>
public static class NotificationText
{
    public const int MaxTitleLength = 120;
    public const int MaxPlatformLength = 60;

    /// <summary>
    /// One line of plain text: control characters become spaces (so a title cannot start a fake second line),
    /// invisible formatting characters such as bidi overrides are dropped, whitespace is collapsed, and the result
    /// is cut to <paramref name="maxLength"/> characters with an ellipsis.
    /// </summary>
    public static string Clean(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            // Zero-width joiners hold emoji sequences together; other format characters only hide or reorder text.
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format &&
                character is not '‌' and not '‍')
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(character);
        }

        if (builder.Length <= maxLength)
        {
            return builder.ToString();
        }

        var cut = maxLength - 1;
        if (char.IsHighSurrogate(builder[cut - 1]))
        {
            cut--;
        }

        return builder.ToString(0, cut).TrimEnd() + "…";
    }
}
