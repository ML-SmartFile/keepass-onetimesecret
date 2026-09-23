using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneTimeSecret.Ui
{
    /// <summary>
    /// Shows the share link and the receipt link after a successful share.
    /// Both stay selectable and re-copyable: the clipboard auto-clears after a few
    /// seconds, and a link lost before it is pasted would leave a live secret online
    /// with no way to burn it.
    ///
    /// Deliberately free of KeePass references - the caller supplies the copy action so
    /// clipboard handling (and its auto-clear countdown) lives in one place.
    /// </summary>
    internal sealed class ResultForm : Form
    {
        private readonly Action<string> m_copy;

        public ResultForm(string shareUrl, string receiptUrl, Action<string> copy)
        {
            if (copy == null) throw new ArgumentNullException("copy");
            m_copy = copy;

            Text = "OneTimeSecret";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 250);

            Controls.Add(MakeLabel("One-time link - copied to the clipboard:", 12, true));
            TextBox shareBox = MakeUrlBox(shareUrl, 34);
            Controls.Add(shareBox);
            Controls.Add(MakeCopyButton(34, delegate { CopyAndFlash(shareBox); }));

            Controls.Add(MakeLabel(
                "Opens once. Anyone holding it can read the secret.", 66, false));

            Controls.Add(MakeLabel("Receipt link - keep this to burn the secret early:", 108, true));
            TextBox receiptBox = MakeUrlBox(receiptUrl, 130);
            Controls.Add(receiptBox);
            Controls.Add(MakeCopyButton(130, delegate { CopyAndFlash(receiptBox); }));

            Controls.Add(MakeLabel(
                "The clipboard clears itself shortly. Copy again from here if you need to.",
                162, false));

            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Bounds = new Rectangle(460, 200, 85, 27)
            };
            Controls.Add(close);

            AcceptButton = close;
            CancelButton = close;

            shareBox.Select(0, shareUrl.Length);
            ActiveControl = shareBox;
        }

        private void CopyAndFlash(TextBox box)
        {
            m_copy(box.Text);
            box.Select(0, box.Text.Length);
            box.Focus();
        }

        private static Label MakeLabel(string text, int top, bool bold)
        {
            return new Label
            {
                Text = text,
                Bounds = new Rectangle(14, top, 530, 18),
                Font = bold
                    ? new Font(SystemFonts.DialogFont, FontStyle.Bold)
                    : SystemFonts.DialogFont
            };
        }

        private static TextBox MakeUrlBox(string url, int top)
        {
            return new TextBox
            {
                Text = url ?? string.Empty,
                ReadOnly = true,
                BackColor = SystemColors.Window,
                Bounds = new Rectangle(14, top, 440, 23)
            };
        }

        private Button MakeCopyButton(int top, EventHandler onClick)
        {
            var button = new Button
            {
                Text = "Copy",
                Bounds = new Rectangle(460, top - 1, 85, 25)
            };
            button.Click += onClick;
            return button;
        }
    }
}
