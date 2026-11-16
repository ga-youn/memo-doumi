using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal static class LinkPolicy
    {

        public static string SafeUrl(string text)
        {
            if (text == null) return null;
            text = text.Trim();
            if (text.Length == 0 || text.Length > 2048) return null;
            foreach (char c in text) if (char.IsControl(c)) return null;
            if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) text = "http://" + text;

            bool http = text.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            bool https = text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!http && !https) return null;

            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            if (uri.IsFile || uri.IsUnc || string.IsNullOrEmpty(uri.Host)) return null;
            if (uri.Host.EndsWith(".", StringComparison.Ordinal)) return null;
            return uri.AbsoluteUri;
        }
    }

    internal class Notice : Form
    {
        public const string SaveKey = "save", LoadKey = "load", LinkKey = "link", ImportKey = "import";

        readonly Label label = new Label();
        readonly Button retry = new Button(), close = new Button();
        readonly List<string> keys = new List<string>();
        readonly Dictionary<string, string> messages = new Dictionary<string, string>();
        readonly Func<bool> exiting;

        public Action Retry;

        public Action Dismissed;

        public int ShownCount;

        public Notice(Func<bool> exiting)
        {
            this.exiting = exiting;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            MaximizeBox = MinimizeBox = false;
            Text = "메모도우미 알림";
            Font = App.MenuFont;
            BackColor = Color.White;

            label.AutoSize = true;
            label.UseMnemonic = false;
            label.MaximumSize = new Size(App.S(340), 0);
            label.Location = new Point(App.S(12), App.S(10));
            retry.Text = "다시 저장";
            retry.AutoSize = true;
            retry.Click += (s, e) => { if (Retry != null) Retry(); };
            close.Text = "닫기";
            close.AutoSize = true;
            close.Click += (s, e) => Dismiss();
            Controls.Add(label);
            Controls.Add(retry);
            Controls.Add(close);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public bool Has(string key) { return messages.ContainsKey(key); }
        public string MessageOf(string key) { string m; return messages.TryGetValue(key, out m) ? m : null; }

        public void Put(string key, string message)
        {
            if (!messages.ContainsKey(key)) keys.Add(key);
            messages[key] = message;
            Render();
            if (!Visible)
            {
                ShownCount++;
                Show();
            }
        }

        public void Remove(string key)
        {
            if (!messages.Remove(key)) return;
            keys.Remove(key);
            if (keys.Count == 0) Hide();
            else Render();
        }

        void Dismiss()
        {
            keys.Clear();
            messages.Clear();
            Hide();
            if (Dismissed != null) Dismissed();
        }

        void Render()
        {
            var sb = new StringBuilder();
            foreach (string k in keys)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(messages[k]);
            }
            label.Text = sb.ToString();
            retry.Visible = messages.ContainsKey(SaveKey);

            int pad = App.S(12), width = Math.Max(label.PreferredWidth, App.S(240));
            int y = label.Bottom + pad;
            close.Location = new Point(pad + width - close.Width, y);
            retry.Location = new Point(close.Left - retry.Width - App.S(6), y);
            ClientSize = new Size(width + pad * 2, close.Bottom + pad);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - App.S(16), Math.Max(wa.Top, wa.Bottom - Height - App.S(130)));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !exiting())
            {
                e.Cancel = true;
                Dismiss();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
