using System;
using System.Drawing;
using System.Windows.Forms;
using OneTimeSecret.Core;

namespace OneTimeSecret.Ui
{
    /// <summary>
    /// Shows the share link, the passphrase when one was set, and the receipt link.
    /// Everything stays selectable and re-copyable: the clipboard auto-clears after a few
    /// seconds, and a link lost before it is pasted would leave a live secret online with
    /// no way to burn it.
    ///
    /// Deliberately free of KeePass references - the caller supplies the copy action so
    /// clipboard handling (and its auto-clear countdown) lives in one place.
    /// </summary>
    internal sealed class ResultForm : Form
    {
        private readonly Action<string> m_copy;

        public ResultForm(string shareUrl, string receiptUrl, string passphrase,
            int requestedTtlSeconds, int effectiveTtlSeconds, Action<string> copy)
        {
            if (copy == null) throw new ArgumentNullException("copy");
            m_copy = copy;

            bool hasPassphrase = !string.IsNullOrEmpty(passphrase);
            bool ttlShortened = effectiveTtlSeconds > 0 && requestedTtlSeconds > 0
                && effectiveTtlSeconds < requestedTtlSeconds;

            Text = "OneTimeSecret";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;

            int y = 12;

            // The warning leads rather than trails: missing it is exactly how someone ends up
            // without the only copy of a live link.
            var warning = new Label
            {
                Text = "The clipboard clears itself in a few seconds. Copy anything you still " +
                       "need from here before closing this window.",
                Font = new Font(SystemFonts.DialogFont, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 60, 0),
                AutoSize = false,
                Bounds = new Rectangle(14, y, 546, 34)
            };
            Controls.Add(warning);
            y += 42;

            if (effectiveTtlSeconds > 0)
            {
                // The server clamps silently, so the requested lifetime is not necessarily the
                // one the link got. Saying so here is the difference between the user knowing
                // when their link dies and merely believing they know.
                var lifetime = new Label
                {
                    Text = ttlShortened
                        ? "Expires in " + TtlOptions.Describe(effectiveTtlSeconds) +
                          " - shortened by the server from the " +
                          TtlOptions.Describe(requestedTtlSeconds) + " you asked for."
                        : "Expires in " + TtlOptions.Describe(effectiveTtlSeconds) + ".",
                    Font = ttlShortened
                        ? new Font(SystemFonts.DialogFont, FontStyle.Bold)
                        : SystemFonts.DialogFont,
                    ForeColor = ttlShortened ? Color.FromArgb(160, 60, 0) : SystemColors.ControlText,
                    AutoSize = false,
                    Bounds = new Rectangle(14, y, 546, 32)
                };
                Controls.Add(lifetime);
                // Same 32-tall box either way now that AutoSize is off - the previous
                // shortcut of a smaller bump for the unshortened case put the next control
                // 8px inside this label's own box.
                y += 38;
            }

            Controls.Add(Section("One-time link - already on the clipboard:", y)); y += 22;
            TextBox shareBox = ValueBox(shareUrl, y);
            Controls.Add(shareBox);
            Controls.Add(CopyButton(y, shareBox));
            y += 28;
            Controls.Add(Hint("Opens once. Anyone holding it can read the secret.", y)); y += 28;

            TextBox passphraseBox = null;
            if (hasPassphrase)
            {
                Controls.Add(Section("Passphrase - required to open the link:", y)); y += 22;
                passphraseBox = ValueBox(passphrase, y);
                Controls.Add(passphraseBox);
                Controls.Add(CopyButton(y, passphraseBox));
                y += 28;
                Controls.Add(Hint("Not part of the link. Send it by a different channel, or the " +
                    "passphrase protects nothing.", y));
                y += 40;
            }

            Controls.Add(Section("Receipt link - keep this to burn the secret early:", y)); y += 22;
            TextBox receiptBox = ValueBox(receiptUrl, y);
            Controls.Add(receiptBox);
            Controls.Add(CopyButton(y, receiptBox));
            y += 36;

            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Bounds = new Rectangle(475, y, 85, 27)
            };
            Controls.Add(close);

            ClientSize = new Size(574, y + 40);
            AcceptButton = close;
            CancelButton = close;

            // Caret at the start, not a full selection: selecting the whole URL parks the
            // caret at the end and scrolls the box, so the link reads as truncated.
            shareBox.Select(0, 0);
            ActiveControl = shareBox;
        }

        private static Label Section(string text, int top)
        {
            return new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DialogFont, FontStyle.Bold),
                AutoSize = false,
                Bounds = new Rectangle(14, top, 546, 18)
            };
        }

        private static Label Hint(string text, int top)
        {
            return new Label
            {
                Text = text,
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Bounds = new Rectangle(14, top, 546, 28)
            };
        }

        private static TextBox ValueBox(string value, int top)
        {
            return new TextBox
            {
                Text = value ?? string.Empty,
                ReadOnly = true,
                BackColor = SystemColors.Window,
                Bounds = new Rectangle(14, top, 456, 23)
            };
        }

        private Button CopyButton(int top, TextBox source)
        {
            var button = new Button { Text = "Copy", Bounds = new Rectangle(475, top - 1, 85, 25) };
            button.Click += delegate
            {
                // Each copy replaces the last, which is why they are separate buttons: the
                // link and the passphrase must never end up in the same paste.
                m_copy(source.Text);
                source.Select(0, source.Text.Length);
                source.Focus();
            };
            return button;
        }
    }
}
