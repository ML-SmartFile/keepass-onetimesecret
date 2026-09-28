using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using OneTimeSecret.Core;

namespace OneTimeSecret.Ui
{
    /// <summary>What the entry picker hands back: the UUID to persist and a label to show.</summary>
    internal sealed class CredentialSelection
    {
        public CredentialSelection(string uuidHex, string display)
        {
            UuidHex = uuidHex;
            Display = display;
        }

        public string UuidHex { get; private set; }
        public string Display { get; private set; }
    }

    /// <summary>
    /// A fallback entry reference, in and out of the dialog. On the way in, StatusText is
    /// the rendered read-only line (may say "not currently open" - a live check the caller
    /// already did); on the way out it is unused. An empty DatabaseUuidHex means no
    /// fallback is configured.
    /// </summary>
    internal sealed class FallbackState
    {
        public FallbackState(string databaseUuidHex, string entryUuidHex, string display, string statusText)
        {
            DatabaseUuidHex = databaseUuidHex ?? string.Empty;
            EntryUuidHex = entryUuidHex ?? string.Empty;
            Display = display ?? string.Empty;
            StatusText = statusText ?? "(none)";
        }

        public string DatabaseUuidHex { get; private set; }
        public string EntryUuidHex { get; private set; }
        public string Display { get; private set; }
        public string StatusText { get; private set; }
    }

    /// <summary>
    /// The global settings dialog. KeePass-free: the caller injects the entry picker and
    /// maps these properties to and from CustomConfig.
    /// </summary>
    internal sealed class OptionsForm : Form
    {
        private static readonly string[] Regions = { "eu", "ca", "nz", "uk", "us" };

        private readonly Func<IWin32Window, CredentialSelection> m_pickEntry;

        private readonly ComboBox m_region;
        private readonly TextBox m_baseUrl;
        private readonly ComboBox m_apiVersion;
        private readonly ComboBox m_ttl;
        private readonly TextBox m_customTtl;
        private readonly TextBox m_template;
        private readonly CheckBox m_allowInsecureHttp;
        private readonly CheckBox m_writeBackReceipt;
        private readonly ComboBox m_passphraseMode;
        private readonly TextBox m_credentialDisplay;
        private readonly TextBox m_credentialFallbackStatus;
        private readonly Button m_credentialFallbackSet, m_credentialFallbackClear;
        private readonly Label m_passphraseEntryLabel, m_passphraseEntryHint;
        private readonly TextBox m_passphraseEntryDisplay;
        private readonly Button m_passphraseEntrySelect, m_passphraseEntryClear;
        private readonly Label m_passphraseFallbackLabel;
        private readonly TextBox m_passphraseFallbackStatus;
        private readonly Button m_passphraseFallbackSet, m_passphraseFallbackClear;
        private readonly Button m_ok, m_cancel;
        private readonly int m_passphraseEntryTop;
        private readonly int m_contentBottomWithEntry, m_contentBottomWithoutEntry;

        private string m_credentialUuidHex;
        private string m_passphraseEntryUuidHex;
        private string m_credentialFallbackDatabaseUuidHex, m_credentialFallbackEntryUuidHex, m_credentialFallbackDisplay;
        private string m_passphraseFallbackDatabaseUuidHex, m_passphraseFallbackEntryUuidHex, m_passphraseFallbackDisplay;

        private readonly bool m_activeDatabaseOpen;
        private readonly string m_activeDatabaseUuidHex;
        private readonly string m_activeDatabaseDisplayName;

        /// <summary>Wraps a mode so the combo shows a readable label.</summary>
        private sealed class ModeItem
        {
            public ModeItem(PassphraseMode mode) { Mode = mode; }
            public PassphraseMode Mode { get; private set; }
            public override string ToString() { return PassphraseModes.Describe(Mode); }
        }

        public OptionsForm(
            string baseUrl,
            string apiVersion,
            int defaultTtlSeconds,
            string template,
            bool allowInsecureHttp,
            bool writeBackReceipt,
            PassphraseMode passphraseMode,
            string credentialUuidHex,
            string credentialDisplay,
            string passphraseEntryUuidHex,
            string passphraseEntryDisplay,
            FallbackState credentialFallback,
            FallbackState passphraseFallback,
            bool activeDatabaseOpen,
            string activeDatabaseUuidHex,
            string activeDatabaseDisplayName,
            Func<IWin32Window, CredentialSelection> pickEntry)
        {
            if (pickEntry == null) throw new ArgumentNullException("pickEntry");
            if (credentialFallback == null) throw new ArgumentNullException("credentialFallback");
            if (passphraseFallback == null) throw new ArgumentNullException("passphraseFallback");
            m_pickEntry = pickEntry;
            m_credentialUuidHex = credentialUuidHex ?? string.Empty;
            m_passphraseEntryUuidHex = passphraseEntryUuidHex ?? string.Empty;
            m_credentialFallbackDatabaseUuidHex = credentialFallback.DatabaseUuidHex;
            m_credentialFallbackEntryUuidHex = credentialFallback.EntryUuidHex;
            m_credentialFallbackDisplay = credentialFallback.Display;
            m_passphraseFallbackDatabaseUuidHex = passphraseFallback.DatabaseUuidHex;
            m_passphraseFallbackEntryUuidHex = passphraseFallback.EntryUuidHex;
            m_passphraseFallbackDisplay = passphraseFallback.Display;
            m_activeDatabaseOpen = activeDatabaseOpen;
            m_activeDatabaseUuidHex = activeDatabaseUuidHex ?? string.Empty;
            m_activeDatabaseDisplayName = activeDatabaseDisplayName ?? string.Empty;

            Text = "OneTimeSecret Options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            // Height is set by LayOutTail once the content is known.
            ClientSize = new Size(600, 500);

            int y = 14;

            Controls.Add(Header("Server", y)); y += 26;

            Controls.Add(Label("Region preset:", y));
            m_region = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Bounds = new Rectangle(160, y - 3, 150, 23)
            };
            m_region.Items.Add("(custom)");
            foreach (string region in Regions) m_region.Items.Add(region);
            m_region.SelectedIndexChanged += OnRegionChanged;
            Controls.Add(m_region);
            y += 30;

            Controls.Add(Label("Base URL:", y));
            m_baseUrl = new TextBox { Text = baseUrl ?? string.Empty, Bounds = new Rectangle(160, y - 3, 424, 23) };
            Controls.Add(m_baseUrl);
            y += 30;

            m_allowInsecureHttp = new CheckBox
            {
                Text = "Allow plain http - loopback addresses only, never a remote host",
                Checked = allowInsecureHttp,
                Bounds = new Rectangle(160, y, 424, 22)
            };
            Controls.Add(m_allowInsecureHttp);
            y += 30;

            Controls.Add(Label("API version:", y));
            m_apiVersion = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Bounds = new Rectangle(160, y - 3, 150, 23)
            };
            // Only what actually works today: the v2 client arrives in M4, and offering it
            // here would silently give you v1.
            m_apiVersion.Items.Add("auto");
            m_apiVersion.Items.Add("v1");
            m_apiVersion.Items.Add("v2");
            m_apiVersion.SelectedItem = (apiVersion == "v1" || apiVersion == "v2") ? apiVersion : "auto";
            Controls.Add(m_apiVersion);
            Controls.Add(new Label
            {
                Text = "(auto detects and caches per host for 7 days)",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Bounds = new Rectangle(320, y, 264, 20)
            });
            y += 36;

            Controls.Add(Header("Credentials", y)); y += 26;

            Controls.Add(Label("Stored in entry:", y));
            m_credentialDisplay = new TextBox
            {
                Text = credentialDisplay ?? string.Empty,
                ReadOnly = true,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(160, y - 3, 250, 23)
            };
            Controls.Add(m_credentialDisplay);

            var select = new Button { Text = "Select...", Bounds = new Rectangle(418, y - 4, 80, 25) };
            select.Click += OnSelectEntry;
            Controls.Add(select);

            var clear = new Button { Text = "Clear", Bounds = new Rectangle(504, y - 4, 80, 25) };
            clear.Click += OnClearEntry;
            Controls.Add(clear);
            y += 28;

            Controls.Add(Label("Fallback:", y));
            m_credentialFallbackStatus = new TextBox
            {
                Text = credentialFallback.StatusText,
                ReadOnly = true,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(160, y - 3, 250, 23)
            };
            Controls.Add(m_credentialFallbackStatus);

            m_credentialFallbackSet = new Button { Text = "Use This Entry", Bounds = new Rectangle(418, y - 4, 80, 25) };
            m_credentialFallbackSet.Click += OnSetCredentialFallback;
            Controls.Add(m_credentialFallbackSet);

            m_credentialFallbackClear = new Button { Text = "Clear", Bounds = new Rectangle(504, y - 4, 80, 25) };
            m_credentialFallbackClear.Click += OnClearCredentialFallback;
            Controls.Add(m_credentialFallbackClear);
            y += 28;

            Controls.Add(new Label
            {
                Text = "The API token is never written to KeePass.config.xml - only a reference to " +
                       "the entry is stored, and the token is read at the moment of sharing. " +
                       "\"Fallback\" lets other open databases with no credential entry of their own " +
                       "borrow this one, but only while this database is open too - nothing about it " +
                       "is ever written into another database's file.",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Bounds = new Rectangle(160, y, 424, 56)
            });
            y += 66;

            Controls.Add(Header("Share defaults", y)); y += 26;

            Controls.Add(Label("Default lifetime:", y));
            m_ttl = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Bounds = new Rectangle(160, y - 3, 150, 23)
            };
            foreach (TtlOption option in TtlOptions.Presets()) m_ttl.Items.Add(option);
            m_ttl.Items.Add("custom (seconds)");
            m_ttl.SelectedIndexChanged += delegate { m_customTtl.Enabled = IsCustomTtlSelected(); };
            Controls.Add(m_ttl);

            m_customTtl = new TextBox { Bounds = new Rectangle(320, y - 3, 100, 23), Enabled = false };
            Controls.Add(m_customTtl);
            SelectTtl(defaultTtlSeconds);
            y += 32;

            Controls.Add(Label("Content template:", y));
            m_template = new TextBox
            {
                Text = template ?? string.Empty,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Bounds = new Rectangle(160, y - 3, 424, 66)
            };
            Controls.Add(m_template);
            // Setting Text leaves the caret at the end, which scrolls a multi-line box past
            // its first line - making a two-line template look like it only holds the last
            // one. Park the caret at the start so the whole template is visible on open.
            m_template.Select(0, 0);
            y += 72;

            Controls.Add(new Label
            {
                Text = "Placeholders: {TITLE} {USERNAME} {PASSWORD} {URL} {NOTES}",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Bounds = new Rectangle(160, y, 424, 20)
            });
            y += 26;

            m_writeBackReceipt = new CheckBox
            {
                Text = "Write the receipt (and passphrase, if any) to protected fields on the " +
                       "entry (enables \"Burn last share\")",
                Checked = writeBackReceipt,
                AutoSize = false,
                // Longer text than the original 79-character wording (M4's own AutoSize
                // finding) can wrap to three lines at this width - sized generously rather
                // than measured exactly, since clipping here is the same class of bug.
                Bounds = new Rectangle(160, y, 424, 50)
            };
            Controls.Add(m_writeBackReceipt);
            y += 58;

            m_passphraseMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Bounds = new Rectangle(160, y - 3, 200, 23)
            };
            foreach (PassphraseMode mode in new[] { PassphraseMode.None, PassphraseMode.Generate,
                PassphraseMode.Manual, PassphraseMode.Entry })
                m_passphraseMode.Items.Add(new ModeItem(mode));
            SelectPassphraseMode(passphraseMode);
            m_passphraseMode.SelectedIndexChanged += delegate { UpdatePassphraseEntryVisibility(); };
            Controls.Add(Label("Passphrase:", y));
            Controls.Add(m_passphraseMode);
            y += 30;

            // Only shown for the Entry mode - a picker for a setting you are not using is noise.
            m_passphraseEntryTop = y;
            m_passphraseEntryLabel = Label("From entry:", y);
            Controls.Add(m_passphraseEntryLabel);

            m_passphraseEntryDisplay = new TextBox
            {
                Text = passphraseEntryDisplay ?? string.Empty,
                ReadOnly = true,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(160, y - 3, 250, 23)
            };
            Controls.Add(m_passphraseEntryDisplay);

            m_passphraseEntrySelect = new Button { Text = "Select...", Bounds = new Rectangle(418, y - 4, 80, 25) };
            m_passphraseEntrySelect.Click += OnSelectPassphraseEntry;
            Controls.Add(m_passphraseEntrySelect);

            m_passphraseEntryClear = new Button { Text = "Clear", Bounds = new Rectangle(504, y - 4, 80, 25) };
            m_passphraseEntryClear.Click += OnClearPassphraseEntry;
            Controls.Add(m_passphraseEntryClear);
            y += 28;

            m_passphraseFallbackLabel = Label("Fallback:", y);
            Controls.Add(m_passphraseFallbackLabel);

            m_passphraseFallbackStatus = new TextBox
            {
                Text = passphraseFallback.StatusText,
                ReadOnly = true,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(160, y - 3, 250, 23)
            };
            Controls.Add(m_passphraseFallbackStatus);

            m_passphraseFallbackSet = new Button { Text = "Use This Entry", Bounds = new Rectangle(418, y - 4, 80, 25) };
            m_passphraseFallbackSet.Click += OnSetPassphraseFallback;
            Controls.Add(m_passphraseFallbackSet);

            m_passphraseFallbackClear = new Button { Text = "Clear", Bounds = new Rectangle(504, y - 4, 80, 25) };
            m_passphraseFallbackClear.Click += OnClearPassphraseFallback;
            Controls.Add(m_passphraseFallbackClear);
            y += 28;

            m_passphraseEntryHint = new Label
            {
                Text = "Read from that entry's Password field at share time. Reusing one passphrase " +
                       "across shares is weaker than generating one: anyone who received an earlier " +
                       "share can open later ones.",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                // Labels default to AutoSize=true, which ignores this Height and just grows
                // wide instead of wrapping unless turned off explicitly - see ShareForm's
                // passphrase warning label for the bug this caused there.
                Bounds = new Rectangle(160, y, 424, 48)
            };
            Controls.Add(m_passphraseEntryHint);

            // The entry block is shown only for one passphrase mode, so the dialog is laid
            // out from the running y rather than fixed coordinates - hardcoding them is what
            // let the content run underneath the buttons when this block was added.
            m_contentBottomWithoutEntry = m_passphraseEntryTop;
            m_contentBottomWithEntry = y + 48;

            m_ok = new Button { Text = "OK", Bounds = new Rectangle(418, 0, 80, 26) };
            m_ok.Click += OnOk;
            Controls.Add(m_ok);

            m_cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(504, 0, 80, 26)
            };
            Controls.Add(m_cancel);
            CancelButton = m_cancel;

            UpdatePassphraseEntryVisibility();
            SyncRegionToBaseUrl();
        }

        /// <summary>Places the buttons below whatever content is currently shown, and sizes
        /// the dialog to match. This form is not resizable, so it has to fit by construction.</summary>
        private void LayOutTail(bool entryBlockVisible)
        {
            int bottom = entryBlockVisible ? m_contentBottomWithEntry : m_contentBottomWithoutEntry;

            m_ok.Top = bottom + 12;
            m_cancel.Top = bottom + 12;
            ClientSize = new Size(600, bottom + 12 + m_ok.Height + 12);
        }

        public string ResultBaseUrl { get; private set; }
        public string ResultApiVersion { get; private set; }
        public int ResultTtlSeconds { get; private set; }
        public string ResultTemplate { get; private set; }
        public bool ResultAllowInsecureHttp { get; private set; }
        public bool ResultWriteBackReceipt { get; private set; }
        public PassphraseMode ResultPassphraseMode { get; private set; }
        public string ResultCredentialUuidHex { get; private set; }
        public string ResultPassphraseEntryUuidHex { get; private set; }
        public FallbackState ResultCredentialFallback { get; private set; }
        public FallbackState ResultPassphraseFallback { get; private set; }

        private void UpdatePassphraseEntryVisibility()
        {
            var selected = m_passphraseMode.SelectedItem as ModeItem;
            bool show = (selected != null) && selected.Mode == PassphraseMode.Entry;

            m_passphraseEntryLabel.Visible = show;
            m_passphraseEntryDisplay.Visible = show;
            m_passphraseEntrySelect.Visible = show;
            m_passphraseEntryClear.Visible = show;
            m_passphraseFallbackLabel.Visible = show;
            m_passphraseFallbackStatus.Visible = show;
            m_passphraseFallbackSet.Visible = show;
            m_passphraseFallbackClear.Visible = show;
            m_passphraseEntryHint.Visible = show;

            LayOutTail(show);
        }

        private void OnSelectPassphraseEntry(object sender, EventArgs e)
        {
            CredentialSelection selection = m_pickEntry(this);
            if (selection == null) return;

            m_passphraseEntryUuidHex = selection.UuidHex;
            m_passphraseEntryDisplay.Text = selection.Display;
        }

        private void OnClearPassphraseEntry(object sender, EventArgs e)
        {
            m_passphraseEntryUuidHex = string.Empty;
            m_passphraseEntryDisplay.Text = "(none selected)";
        }

        private void OnSetCredentialFallback(object sender, EventArgs e)
        {
            if (!m_activeDatabaseOpen || string.IsNullOrEmpty(m_credentialUuidHex))
            {
                Complain("Select this database's own credential entry first - the fallback " +
                    "is that entry, made available to other open databases with none of " +
                    "their own.");
                return;
            }

            // Just the database name, not which entry - shown back in warning dialogs
            // elsewhere in the plugin, where naming the specific entry would be noise (or,
            // for a database shared with a customer, an unwanted detail about the fallback
            // database's own contents).
            m_credentialFallbackDatabaseUuidHex = m_activeDatabaseUuidHex;
            m_credentialFallbackEntryUuidHex = m_credentialUuidHex;
            m_credentialFallbackDisplay = m_activeDatabaseDisplayName;
            m_credentialFallbackStatus.Text = m_credentialFallbackDisplay;
        }

        private void OnClearCredentialFallback(object sender, EventArgs e)
        {
            m_credentialFallbackDatabaseUuidHex = string.Empty;
            m_credentialFallbackEntryUuidHex = string.Empty;
            m_credentialFallbackDisplay = string.Empty;
            m_credentialFallbackStatus.Text = "(none)";
        }

        private void OnSetPassphraseFallback(object sender, EventArgs e)
        {
            if (!m_activeDatabaseOpen || string.IsNullOrEmpty(m_passphraseEntryUuidHex))
            {
                Complain("Select this database's own passphrase entry first - the fallback " +
                    "is that entry, made available to other open databases with none of " +
                    "their own.");
                return;
            }

            // Just the database name - see OnSetCredentialFallback.
            m_passphraseFallbackDatabaseUuidHex = m_activeDatabaseUuidHex;
            m_passphraseFallbackEntryUuidHex = m_passphraseEntryUuidHex;
            m_passphraseFallbackDisplay = m_activeDatabaseDisplayName;
            m_passphraseFallbackStatus.Text = m_passphraseFallbackDisplay;
        }

        private void OnClearPassphraseFallback(object sender, EventArgs e)
        {
            m_passphraseFallbackDatabaseUuidHex = string.Empty;
            m_passphraseFallbackEntryUuidHex = string.Empty;
            m_passphraseFallbackDisplay = string.Empty;
            m_passphraseFallbackStatus.Text = "(none)";
        }

        private void SelectPassphraseMode(PassphraseMode mode)
        {
            for (int i = 0; i < m_passphraseMode.Items.Count; i++)
            {
                var item = m_passphraseMode.Items[i] as ModeItem;
                if (item != null && item.Mode == mode) { m_passphraseMode.SelectedIndex = i; return; }
            }
            m_passphraseMode.SelectedIndex = 0;
        }

        private static Label Header(string text, int top)
        {
            return new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DialogFont, FontStyle.Bold),
                AutoSize = false,
                Bounds = new Rectangle(14, top, 570, 20)
            };
        }

        private static Label Label(string text, int top)
        {
            return new Label { Text = text, AutoSize = false, Bounds = new Rectangle(14, top, 140, 20) };
        }

        private void OnRegionChanged(object sender, EventArgs e)
        {
            var region = m_region.SelectedItem as string;
            if (string.IsNullOrEmpty(region) || region == "(custom)") return;

            m_baseUrl.Text = "https://" + region + ".onetimesecret.com";
        }

        private void SyncRegionToBaseUrl()
        {
            foreach (string region in Regions)
            {
                if (string.Equals(m_baseUrl.Text.TrimEnd('/'),
                        "https://" + region + ".onetimesecret.com", StringComparison.OrdinalIgnoreCase))
                {
                    m_region.SelectedItem = region;
                    return;
                }
            }
            m_region.SelectedIndex = 0;
        }

        private bool IsCustomTtlSelected()
        {
            return m_ttl.SelectedItem as string == "custom (seconds)";
        }

        private void SelectTtl(int seconds)
        {
            for (int i = 0; i < m_ttl.Items.Count; i++)
            {
                var option = m_ttl.Items[i] as TtlOption;
                if (option != null && option.Seconds == seconds)
                {
                    m_ttl.SelectedIndex = i;
                    return;
                }
            }

            m_ttl.SelectedIndex = m_ttl.Items.Count - 1;
            m_customTtl.Enabled = true;
            m_customTtl.Text = seconds.ToString(CultureInfo.InvariantCulture);
        }

        private void OnSelectEntry(object sender, EventArgs e)
        {
            // Owned by this dialog, not the main window: the picker opens on top of a modal
            // form, so it must be parented here to centre and stack correctly.
            CredentialSelection selection = m_pickEntry(this);
            if (selection == null) return;

            m_credentialUuidHex = selection.UuidHex;
            m_credentialDisplay.Text = selection.Display;
        }

        private void OnClearEntry(object sender, EventArgs e)
        {
            m_credentialUuidHex = string.Empty;
            m_credentialDisplay.Text = "(none - sharing anonymously)";
        }

        private void OnOk(object sender, EventArgs e)
        {
            int ttlSeconds;
            if (IsCustomTtlSelected())
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

            // Validated here rather than on the way out, so a rejected URL keeps the dialog
            // open with the offending value still in place.
            try
            {
                UrlPolicy.Validate(m_baseUrl.Text, m_allowInsecureHttp.Checked);
            }
            catch (UrlPolicyException ex)
            {
                Complain(ex.Message);
                m_baseUrl.Focus();
                m_baseUrl.SelectAll();
                return;
            }

            ResultBaseUrl = m_baseUrl.Text.Trim();
            ResultApiVersion = (string)m_apiVersion.SelectedItem;
            ResultTtlSeconds = ttlSeconds;
            ResultTemplate = m_template.Text;
            ResultAllowInsecureHttp = m_allowInsecureHttp.Checked;
            ResultWriteBackReceipt = m_writeBackReceipt.Checked;
            var selectedMode = m_passphraseMode.SelectedItem as ModeItem;
            ResultPassphraseMode = (selectedMode != null) ? selectedMode.Mode : PassphraseMode.Generate;

            // Choosing the stored-passphrase mode without naming an entry would fail at the
            // moment of sharing, which is the worst time to discover it.
            if (ResultPassphraseMode == PassphraseMode.Entry && string.IsNullOrEmpty(m_passphraseEntryUuidHex))
            {
                Complain("Select the entry that holds the passphrase, or choose a different " +
                    "passphrase option.");
                m_passphraseMode.Focus();
                return;
            }

            ResultCredentialUuidHex = m_credentialUuidHex;
            ResultPassphraseEntryUuidHex = m_passphraseEntryUuidHex;
            ResultCredentialFallback = new FallbackState(
                m_credentialFallbackDatabaseUuidHex, m_credentialFallbackEntryUuidHex, m_credentialFallbackDisplay, null);
            ResultPassphraseFallback = new FallbackState(
                m_passphraseFallbackDatabaseUuidHex, m_passphraseFallbackEntryUuidHex, m_passphraseFallbackDisplay, null);

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Complain(string message)
        {
            MessageBox.Show(this, message, "OneTimeSecret Options",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
