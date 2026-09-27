using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using OneTimeSecret.Core;
using OneTimeSecret.Ots;

namespace OneTimeSecret.Ui
{
    /// <summary>
    /// The per-share dialog. Owns the request so it can drive its own progress state and
    /// cancellation; stays free of KeePass references by taking the client and the passphrase
    /// generator as callbacks.
    /// </summary>
    internal sealed class ShareForm : Form
    {
        private const string MaskChar = "•";

        private readonly IDictionary<string, string> m_resolvedFields;
        private readonly Func<string> m_generatePassphrase;
        private readonly Func<IOtsClient> m_createClient;
        private readonly string m_targetHost;

        private readonly CheckBox m_title, m_userName, m_password, m_url, m_notes, m_includeMessage;
        private readonly TextBox m_message, m_template, m_preview, m_customTtl, m_recipient, m_manualPassphrase;
        private readonly CheckBox m_showPreview;
        private readonly ComboBox m_ttl;
        private readonly RadioButton m_passNone, m_passGenerate, m_passManual, m_passFromDatabase;
        private readonly Label m_progress;
        private readonly Button m_share, m_close;
        private readonly bool m_messageAfterFields;
        private readonly string m_storedPassphrase;

        private CancellationTokenSource m_cancellation;
        private bool m_sending;
        private bool m_suppressRegenerate;

        public ShareForm(
            IDictionary<string, string> resolvedFields,
            TemplateFields initialFields,
            string initialMessage,
            string initialTemplate,
            int initialTtlSeconds,
            PassphraseMode initialPassphraseMode,
            string storedPassphrase,
            string storedPassphraseWarning,
            string targetHost,
            Func<string> generatePassphrase,
            Func<IOtsClient> createClient)
        {
            if (resolvedFields == null) throw new ArgumentNullException("resolvedFields");
            if (generatePassphrase == null) throw new ArgumentNullException("generatePassphrase");
            if (createClient == null) throw new ArgumentNullException("createClient");

            m_resolvedFields = resolvedFields;
            m_generatePassphrase = generatePassphrase;
            m_createClient = createClient;
            m_targetHost = targetHost ?? string.Empty;
            m_messageAfterFields = initialFields.MessageAfterFields;

            Text = "Share with OneTimeSecret";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(620, 610);

            int y = 12;

            Controls.Add(Header("Content", y)); y += 24;

            Controls.Add(Caption("Include:", y));
            m_title = FieldBox("Title", 110, y, initialFields.Title);
            m_userName = FieldBox("User name", 175, y, initialFields.UserName);
            m_password = FieldBox("Password", 278, y, initialFields.Password);
            m_url = FieldBox("URL", 375, y, initialFields.Url);
            m_notes = FieldBox("Notes", 437, y, initialFields.Notes);
            m_includeMessage = FieldBox("Message", 510, y, initialFields.Message);
            y += 28;

            Controls.Add(Caption("Message:", y));
            m_message = new TextBox
            {
                Text = initialMessage ?? string.Empty,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                Bounds = new Rectangle(110, y - 3, 494, 46)
            };
            m_message.TextChanged += OnMessageTextChanged;
            Controls.Add(m_message);
            y += 50;

            Controls.Add(Hint("Text here replaces {MESSAGE} in the template below. It is " +
                "inserted as written - braces in it are not treated as placeholders.", y, 494));
            y += 32;

            Controls.Add(Caption("Template:", y));
            m_template = new TextBox
            {
                Text = initialTemplate ?? string.Empty,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                Bounds = new Rectangle(110, y - 3, 494, 58)
            };
            m_template.TextChanged += delegate { RefreshPreview(); };
            Controls.Add(m_template);
            m_template.Select(0, 0);
            y += 62;

            Controls.Add(Hint("Editable - but changing a checkbox or the message above rewrites it.", y, 494));
            y += 26;

            Controls.Add(Caption("Preview:", y));
            m_preview = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(110, y - 3, 494, 58)
            };
            Controls.Add(m_preview);
            y += 62;

            m_showPreview = new CheckBox
            {
                Text = "Show the exact text that will be sent",
                Bounds = new Rectangle(110, y, 494, 22)
            };
            m_showPreview.CheckedChanged += delegate { RefreshPreview(); };
            Controls.Add(m_showPreview);
            y += 34;

            Controls.Add(Header("Delivery", y)); y += 24;

            Controls.Add(Caption("Lifetime:", y));
            m_ttl = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Bounds = new Rectangle(110, y - 3, 150, 23)
            };
            foreach (TtlOption option in TtlOptions.Presets()) m_ttl.Items.Add(option);
            m_ttl.Items.Add("custom (seconds)");
            m_ttl.SelectedIndexChanged += delegate { m_customTtl.Enabled = IsCustomTtl(); };
            Controls.Add(m_ttl);

            m_customTtl = new TextBox { Bounds = new Rectangle(270, y - 3, 100, 23), Enabled = false };
            Controls.Add(m_customTtl);
            SelectTtl(initialTtlSeconds);
            y += 32;

            Controls.Add(Caption("Passphrase:", y));
            m_passNone = Radio("None", 110, y, initialPassphraseMode == PassphraseMode.None);
            m_passGenerate = Radio("Generate", 180, y, initialPassphraseMode == PassphraseMode.Generate);
            m_passManual = Radio("Enter:", 280, y, initialPassphraseMode == PassphraseMode.Manual);
            m_manualPassphrase = new TextBox
            {
                Bounds = new Rectangle(350, y - 3, 254, 23),
                Enabled = initialPassphraseMode == PassphraseMode.Manual,
                UseSystemPasswordChar = true
            };
            Controls.Add(m_manualPassphrase);
            m_passManual.CheckedChanged += delegate { m_manualPassphrase.Enabled = m_passManual.Checked; };
            y += 26;

            // Offered only when Options actually names an entry, and disabled if that entry
            // could not be read - a passphrase option that silently yields nothing would be
            // worse than not offering it.
            m_passFromDatabase = Radio("From database", 110, y,
                initialPassphraseMode == PassphraseMode.Entry && storedPassphrase != null);
            m_passFromDatabase.Width = 130;
            m_passFromDatabase.Enabled = storedPassphrase != null;
            m_storedPassphrase = storedPassphrase;

            Controls.Add(new Label
            {
                Text = (storedPassphrase != null)
                    ? "reused across shares - weaker than generating one"
                    : (storedPassphraseWarning ?? "no entry configured in Options"),
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(250, y, 354, 20)
            });
            y += 26;

            Controls.Add(Hint("A passphrase is never part of the link - send it separately.", y, 494));
            y += 30;

            Controls.Add(Caption("Recipient:", y));
            m_recipient = new TextBox { Bounds = new Rectangle(110, y - 3, 254, 23) };
            Controls.Add(m_recipient);
            Controls.Add(new Label
            {
                Text = "optional - OneTimeSecret e-mails the link",
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(372, y, 232, 20)
            });
            y += 32;

            Controls.Add(new Label
            {
                Text = "Will be sent to " + m_targetHost,
                Font = new Font(SystemFonts.DialogFont, FontStyle.Bold),
                Bounds = new Rectangle(14, y, 590, 20)
            });
            y += 26;

            m_progress = new Label
            {
                Text = string.Empty,
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(14, y, 590, 34)
            };
            Controls.Add(m_progress);

            m_share = new Button { Text = "Share", Bounds = new Rectangle(438, 572, 80, 26) };
            m_share.Click += OnShare;
            Controls.Add(m_share);

            m_close = new Button { Text = "Cancel", Bounds = new Rectangle(524, 572, 80, 26) };
            m_close.Click += OnCloseClick;
            Controls.Add(m_close);

            AcceptButton = m_share;
            CancelButton = m_close;

            foreach (CheckBox box in new[] { m_title, m_userName, m_password, m_url, m_notes })
                box.CheckedChanged += delegate { Regenerate(); };

            m_includeMessage.CheckedChanged += OnIncludeMessageChanged;

            RefreshPreview();
        }

        /// <summary>Null until a share has succeeded.</summary>
        public ShareResult Result { get; private set; }

        /// <summary>The passphrase actually used, or null when none was set.</summary>
        public string PassphraseUsed { get; private set; }

        public TemplateFields ResultFields
        {
            get
            {
                return new TemplateFields
                {
                    Title = m_title.Checked,
                    UserName = m_userName.Checked,
                    Password = m_password.Checked,
                    Url = m_url.Checked,
                    Notes = m_notes.Checked,
                    Message = m_includeMessage.Checked,
                    MessageAfterFields = m_messageAfterFields,
                };
            }
        }

        public string ResultMessage { get { return m_message.Text; } }
        public string ResultTemplate { get { return m_template.Text; } }
        public int ResultTtlSeconds { get; private set; }

        private void OnMessageTextChanged(object sender, EventArgs e)
        {
            // Typing a note obviously means you want it included; making you tick a box first
            // would just be a trap you fall into once and then swear at.
            if (!m_includeMessage.Checked && m_message.Text.Trim().Length > 0)
            {
                m_includeMessage.Checked = true;   // this regenerates via CheckedChanged
                return;
            }

            Regenerate();
        }

        private void OnIncludeMessageChanged(object sender, EventArgs e)
        {
            // The box deliberately stays enabled when unticked: typing is what ticks it, so
            // greying it out would make the checkbox impossible to reach by typing.
            Regenerate();
        }

        private void Regenerate()
        {
            if (m_suppressRegenerate) return;

            m_suppressRegenerate = true;
            try
            {
                m_template.Text = TemplateBuilder.Build(ResultFields);
                m_template.Select(0, 0);
            }
            finally
            {
                m_suppressRegenerate = false;
            }

            RefreshPreview();
        }

        /// <summary>
        /// Entry values plus the per-share message. The message is supplied as a substituted
        /// value, so it is inserted verbatim - braces in it are not rescanned.
        /// </summary>
        private IDictionary<string, string> CompositionValues()
        {
            var values = new Dictionary<string, string>(m_resolvedFields, StringComparer.OrdinalIgnoreCase);
            values[SecretComposer.FieldMessage] = SecretComposer.NormalizeNewLines(m_message.Text.Trim());
            return values;
        }

        private void RefreshPreview()
        {
            string composed = SecretComposer.Compose(m_template.Text, CompositionValues());

            if (m_showPreview.Checked)
            {
                m_preview.Text = composed;
                return;
            }

            // Mask per line, so the shape of the secret is still legible without the values.
            string[] lines = composed.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
                lines[i] = new string(MaskChar[0], lines[i].Length);

            m_preview.Text = string.Join("\r\n", lines);
        }

        private bool IsCustomTtl()
        {
            return m_ttl.SelectedItem as string == "custom (seconds)";
        }

        private void SelectTtl(int seconds)
        {
            for (int i = 0; i < m_ttl.Items.Count; i++)
            {
                var option = m_ttl.Items[i] as TtlOption;
                if (option != null && option.Seconds == seconds) { m_ttl.SelectedIndex = i; return; }
            }

            m_ttl.SelectedIndex = m_ttl.Items.Count - 1;
            m_customTtl.Enabled = true;
            m_customTtl.Text = seconds.ToString(CultureInfo.InvariantCulture);
        }

        private void OnShare(object sender, EventArgs e)
        {
            if (m_sending) return;

            int ttlSeconds;
            if (IsCustomTtl())
            {
                if (!int.TryParse(m_customTtl.Text.Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out ttlSeconds) || ttlSeconds <= 0)
                {
                    Complain("Enter the custom lifetime as a whole number of seconds.");
                    m_customTtl.Focus();
                    return;
                }
            }
            else
            {
                var option = m_ttl.SelectedItem as TtlOption;
                ttlSeconds = (option != null) ? option.Seconds : TtlOptions.DefaultSeconds;
            }

            string composed = SecretComposer.Compose(m_template.Text, CompositionValues());
            if (string.IsNullOrEmpty(composed.Trim()))
            {
                Complain("There is nothing to share - select a field or write a message.");
                return;
            }

            string passphrase = null;
            if (m_passGenerate.Checked)
            {
                try
                {
                    passphrase = m_generatePassphrase();
                }
                catch (Exception ex)
                {
                    Complain("Could not generate a passphrase: " + ex.Message);
                    return;
                }
            }
            else if (m_passManual.Checked)
            {
                passphrase = m_manualPassphrase.Text;
                if (string.IsNullOrEmpty(passphrase))
                {
                    Complain("Enter a passphrase, or choose None.");
                    m_manualPassphrase.Focus();
                    return;
                }
            }
            else if (m_passFromDatabase.Checked)
            {
                passphrase = m_storedPassphrase;
                if (string.IsNullOrEmpty(passphrase))
                {
                    Complain("The configured passphrase entry could not be read. Pick another " +
                        "entry in the OneTimeSecret options, or choose a different option.");
                    return;
                }
            }

            string recipient = m_recipient.Text.Trim();
            ResultTtlSeconds = ttlSeconds;

            EnterSendingState();

            var request = new ShareRequest(composed, ttlSeconds, passphrase, recipient);
            m_cancellation = new CancellationTokenSource();
            CancellationToken token = m_cancellation.Token;
            string passphraseForResult = passphrase;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ShareResult result = m_createClient().Share(request, token);
                    Post(delegate
                    {
                        Result = result;
                        PassphraseUsed = passphraseForResult;
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                }
                catch (OperationCanceledException)
                {
                    // The user already closed the form; nothing to report.
                }
                catch (OtsException ex)
                {
                    string message = ex.Message;
                    Post(delegate { LeaveSendingState(message); });
                }
                catch (Exception ex)
                {
                    string message = "Unexpected error while sharing (" + ex.GetType().Name + "): " + ex.Message;
                    Post(delegate { LeaveSendingState(message); });
                }
            });
        }

        private void EnterSendingState()
        {
            m_sending = true;
            m_share.Enabled = false;
            SetInputsEnabled(false);

            // "Cancel" would be a lie from here on: the request is on the wire and we cannot
            // know whether the server already stored the secret.
            m_close.Text = "Close";
            m_progress.Text = "Sending to " + m_targetHost + "...\r\n" +
                "Closing now stops us waiting, but cannot un-create a secret the server may " +
                "already hold - that one would expire on its own, with no receipt link.";
        }

        private void LeaveSendingState(string error)
        {
            m_sending = false;
            m_share.Enabled = true;
            SetInputsEnabled(true);
            m_close.Text = "Cancel";
            m_progress.Text = string.Empty;

            DisposeCancellation();
            Complain(error);
        }

        private void SetInputsEnabled(bool enabled)
        {
            foreach (Control control in Controls)
            {
                if (control == m_close || control == m_progress) continue;
                control.Enabled = enabled;
            }

            if (enabled)
            {
                m_customTtl.Enabled = IsCustomTtl();
                m_manualPassphrase.Enabled = m_passManual.Checked;
            }
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            if (m_sending && m_cancellation != null)
            {
                try { m_cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (m_sending && m_cancellation != null)
            {
                try { m_cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            DisposeCancellation();
            base.OnFormClosed(e);
        }

        private void DisposeCancellation()
        {
            if (m_cancellation == null) return;

            try { m_cancellation.Dispose(); }
            catch (Exception) { }
            m_cancellation = null;
        }

        /// <summary>Marshals onto the UI thread, tolerating the form having closed meanwhile.</summary>
        private void Post(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void Complain(string message)
        {
            MessageBox.Show(this, message, "Share with OneTimeSecret",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static Label Header(string text, int top)
        {
            return new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DialogFont, FontStyle.Bold),
                Bounds = new Rectangle(14, top, 590, 20)
            };
        }

        private static Label Caption(string text, int top)
        {
            return new Label { Text = text, Bounds = new Rectangle(14, top, 92, 20) };
        }

        private static Label Hint(string text, int top, int width)
        {
            return new Label
            {
                Text = text,
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(110, top, width, 28)
            };
        }

        private CheckBox FieldBox(string text, int left, int top, bool checkedState)
        {
            var box = new CheckBox
            {
                Text = text,
                Checked = checkedState,
                Bounds = new Rectangle(left, top - 2, text.Length * 8 + 26, 22)
            };
            Controls.Add(box);
            return box;
        }

        private RadioButton Radio(string text, int left, int top, bool checkedState)
        {
            var radio = new RadioButton
            {
                Text = text,
                Checked = checkedState,
                Bounds = new Rectangle(left, top - 2, text.Length * 8 + 26, 22)
            };
            Controls.Add(radio);
            return radio;
        }
    }
}
