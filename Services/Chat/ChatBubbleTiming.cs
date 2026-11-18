using System.Globalization;

namespace DesktopFamiliar.Services.Chat;

public static class ChatBubbleTiming
{
    // Bound individual bubbles even for unbroken words, emoji and explicit newlines.
    public static IEnumerable<string> Pages(string reply)
    {
        var elements = StringInfo.GetTextElementEnumerator(reply.Trim());
        var page = new List<string>();
        var breaks = 0;
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            page.Add(element);
            if (element.Contains('\n')) breaks++;
            if (page.Count >= 110 || breaks >= 3 || (page.Count >= 32 && element is "." or "!" or "?" or "。" or "！" or "？"))
            {
                var end = page.Count;
                if (page.Count >= 110)
                {
                    var space = page.FindLastIndex(string.IsNullOrWhiteSpace);
                    if (space >= 60) end = space + 1;
                }
                var text = string.Concat(page.Take(end)).Trim();
                if (text.Length > 0) yield return text;
                page.RemoveRange(0, end);
                breaks = 0;
            }
        }
        var remaining = string.Concat(page).Trim();
        if (remaining.Length > 0) yield return remaining;
    }

    public static TimeSpan ReadingTime(string text) => TimeSpan.FromSeconds(1.8 + new StringInfo(text).LengthInTextElements / 13d);
}
