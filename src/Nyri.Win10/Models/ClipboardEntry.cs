namespace Nyri.Win10.Models;

/// <summary>A complete text clipboard value retained only for this session.</summary>
public sealed record ClipboardEntry(Guid Id, string Text, DateTimeOffset CopiedAt)
{
    public string Preview
    {
        get
        {
            var singleLine = Text.ReplaceLineEndings(" ").Replace('\t', ' ');
            if (singleLine.Length <= 120) return singleLine;
            var prefixLength = 119;
            // Do not leave half of a UTF-16 surrogate pair before the ellipsis.
            if (char.IsHighSurrogate(singleLine[prefixLength - 1])) prefixLength--;
            return singleLine[..prefixLength] + "…";
        }
    }

    public string DisplayTime => CopiedAt.ToLocalTime().ToString("HH:mm");
}
