using System.Globalization;

namespace DesktopFamiliar.Services.Chat;

public static class ChatBubbleTiming
{
    private const int MaxPageLength = 110;

    // Bound individual bubbles even for unbroken words, emoji and explicit newlines.
    public static IEnumerable<string> Pages(string reply)
    {
        var elements = StringInfo.GetTextElementEnumerator(reply.Trim());
        var page = new List<string>();
        var breaks = 0;
        var atSentenceEnd = false;
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var terminator = IsTerminator(element);
            var continuesEnding = terminator || IsClosingMark(element) || element is "\u00a0" or "\u202f";
            if (page.Count == MaxPageLength || breaks >= 3 || (page.Count >= 32 && atSentenceEnd && !continuesEnding))
            {
                var end = page.Count == MaxPageLength ? FindLengthBreak(page, continuesEnding) : page.Count;
                var text = string.Concat(page.Take(end)).Trim();
                if (text.Length > 0) yield return text;
                page.RemoveRange(0, end);
                breaks = page.Count(part => part.Contains('\n'));
            }
            page.Add(element);
            if (element.Contains('\n')) breaks++;
            atSentenceEnd = terminator || (atSentenceEnd && continuesEnding);
        }
        var remaining = string.Concat(page).Trim();
        if (remaining.Length > 0) yield return remaining;
    }

    private static bool IsTerminator(string element) => element is "." or "!" or "?" or "。" or "！" or "？" or "…";

    private static bool IsClosingMark(string element) => element is "\"" or "'"
        || CharUnicodeInfo.GetUnicodeCategory(element, 0) is UnicodeCategory.ClosePunctuation or UnicodeCategory.FinalQuotePunctuation;

    private static int FindLengthBreak(List<string> page, bool nextIsPunctuation)
    {
        var end = page.Count;
        if (nextIsPunctuation)
        {
            // Carry some text with the punctuation if it cannot fit on this page.
            var lastContent = page.FindLastIndex(part => !IsTerminator(part) && !IsClosingMark(part) && !string.IsNullOrWhiteSpace(part));
            if (lastContent > 0) end = lastContent;
        }
        var space = page.FindLastIndex(end - 1, string.IsNullOrWhiteSpace);
        return space >= 60 ? space + 1 : end;
    }

    public static TimeSpan ReadingTime(string text) => TimeSpan.FromSeconds(1.8 + new StringInfo(text).LengthInTextElements / 13d);
}
