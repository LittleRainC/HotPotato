using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Chardin
{
    public enum TutorialAdvance
    {
        ClickBubble,
        ClickPass,
        ClickStuff,
        ClickSnuff
    }

    public sealed class TutorialDialogueLine
    {
        public string Id;
        public int Order;
        public string Group;
        public string Speaker;
        public string Text;
        public TutorialAdvance Advance;
        public string Event;
        public string NextId;
    }

    /// <summary>解析 tutorial CSV（支持引号内逗号）。</summary>
    public static class TutorialDialogueCsv
    {
        public static List<TutorialDialogueLine> LoadFromResources(string resourcesPath = "Dialogue/tutorial_l1")
        {
            var asset = Resources.Load<TextAsset>(resourcesPath);
            if (asset == null)
            {
                Debug.LogError("[Tutorial] Missing Resources/" + resourcesPath + ".csv");
                return new List<TutorialDialogueLine>();
            }

            return Parse(asset.text);
        }

        public static List<TutorialDialogueLine> Parse(string csv)
        {
            var lines = new List<TutorialDialogueLine>();
            if (string.IsNullOrEmpty(csv))
                return lines;

            string[] rawLines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (rawLines.Length < 2)
                return lines;

            for (int i = 1; i < rawLines.Length; i++)
            {
                string row = rawLines[i].Trim();
                if (string.IsNullOrEmpty(row))
                    continue;

                List<string> cols = SplitCsvRow(row);
                if (cols.Count < 9)
                    continue;

                if (cols[8].Trim() != "1")
                    continue;

                var line = new TutorialDialogueLine
                {
                    Id = cols[0].Trim(),
                    Order = ParseInt(cols[1]),
                    Group = cols[2].Trim(),
                    Speaker = cols[3].Trim(),
                    Text = cols[4],
                    Advance = ParseAdvance(cols[5]),
                    Event = cols[6].Trim(),
                    NextId = cols[7].Trim()
                };
                lines.Add(line);
            }

            lines.Sort((a, b) => a.Order.CompareTo(b.Order));
            return lines;
        }

        static TutorialAdvance ParseAdvance(string raw)
        {
            string key = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (key == "click_pass") return TutorialAdvance.ClickPass;
            if (key == "click_stuff") return TutorialAdvance.ClickStuff;
            if (key == "click_snuff") return TutorialAdvance.ClickSnuff;
            return TutorialAdvance.ClickBubble;
        }

        static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s.Trim(), out v) ? v : 0;
        }

        static List<string> SplitCsvRow(string row)
        {
            var cols = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < row.Length; i++)
            {
                char c = row[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < row.Length && row[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    cols.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }

                sb.Append(c);
            }

            cols.Add(sb.ToString());
            return cols;
        }
    }
}
