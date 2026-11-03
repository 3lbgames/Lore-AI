using System;
using System.Text;

public static class TextWrapUtility
{
    /// <summary>
    /// Inserts line breaks roughly every <paramref name="wordsPerLine"/> words.
    /// Words are spread evenly across lines so you never get a lonely word on the last line
    /// (e.g. 11 words at 5 per line becomes 4/4/3 instead of 5/5/1).
    /// Existing line breaks in the input are kept.
    /// </summary>
    public static string WrapEveryNWords(string input, int wordsPerLine = 5)
    {
        if (string.IsNullOrWhiteSpace(input) || wordsPerLine < 1)
            return input;

        var sb = new StringBuilder(input.Length + 16);
        string[] paragraphs = input.Replace("\r\n", "\n").Split('\n');

        for (int p = 0; p < paragraphs.Length; p++)
        {
            if (p > 0) sb.Append('\n');

            string[] words = paragraphs[p].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            int lineCount = (words.Length + wordsPerLine - 1) / wordsPerLine;
            int baseSize = words.Length / lineCount;
            int extra = words.Length % lineCount; // first 'extra' lines get one more word

            int w = 0;
            for (int line = 0; line < lineCount; line++)
            {
                if (line > 0) sb.Append('\n');

                int count = baseSize + (line < extra ? 1 : 0);
                for (int i = 0; i < count; i++, w++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(words[w]);
                }
            }
        }

        return sb.ToString();
    }
}
