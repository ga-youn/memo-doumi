using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal static class RoundtripMain
    {
        static string Roundtrip(Form form, string markdown)
        {
            var box = new RichTextBox();
            box.Dock = DockStyle.Fill;
            form.Controls.Add(box);
            try
            {
                Markdown.Fill(box, new NoteData { Text = markdown, Md = true });
                return Markdown.FromRich(box);
            }
            finally
            {
                form.Controls.Remove(box);
                box.Dispose();
            }
        }

        [STAThread]
        static int Main()
        {
            App.Scale = 1f;
            var cases = new List<string>
            {
                "", "보통 글자", "**굵게**", "*기울임*", "~~취소선~~", "<u>밑줄</u>",
                "앞 **굵게** 가운데 *기울임* 뒤 ~~취소~~ 끝 <u>밑줄</u>",
                "***굵고 기울임*** 그리고 **굵게만** *기울임만*",
                "**~~<u>세 가지</u>~~** 보통 ***~~<u>네 가지</u>~~***",
                "별\\*표 물결\\~표 역슬래시\\\\ \\<u>글자 그대로\\</u>",
                "- 목록 하나\n- **굵은** 목록\n보통 줄\n\\- 목록 아님",
                "첫 줄\n\n셋째 줄 **굵게**\n\n\n여섯째 줄",
                "  앞 공백 **굵게  ** 뒤 공백  ",
                "한 글자씩 **가***나*~~다~~<u>라</u>마",
                "이모지 😀 **굵은 😀 이모지** 끝",
                "English **bold** and *italic* with tab\there",
                "http://example.com 과 **https://example.com/굵은-링크**",
                "줄 끝 굵게**굵게**\n**줄 처음 굵게** 다음\n*전체 기울임 줄*",
            };

            var rnd = new Random(12345);
            var sb = new StringBuilder();
            string[] open = { "", "", "", "**", "*", "~~", "<u>" }, close = { "", "", "", "**", "*", "~~", "</u>" };
            for (int i = 0; i < 400; i++)
            {
                int k = rnd.Next(open.Length);
                sb.Append(open[k]).Append("낱말").Append(i).Append(close[k]).Append(rnd.Next(7) == 0 ? "\n" : " ");
                if (rnd.Next(25) == 0) sb.Append("\n- 목록 줄 ").Append(i).Append('\n');
            }
            cases.Add(sb.ToString().TrimEnd());
            sb.Length = 0;
            for (int i = 0; i < 400; i++) sb.Append("benchmark text ").Append(i % 40 == 39 ? "\n" : "");
            cases.Add(sb.ToString());

            var form = new Form();
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            var result = new StringBuilder();
            int unstable = 0;
            for (int i = 0; i < cases.Count; i++)
            {

                string once = Roundtrip(form, cases[i]);
                string twice = Roundtrip(form, once);
                if (once != twice) unstable++;
                result.Append("#").Append(i).Append(once == cases[i] ? " same" : " changed").Append(once == twice ? " stable" : " UNSTABLE").Append('\n');
                result.Append(once).Append("\n----\n");
            }
            form.Dispose();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roundtrip.txt"), result.ToString(), new UTF8Encoding(false));
            return unstable;
        }
    }
}
