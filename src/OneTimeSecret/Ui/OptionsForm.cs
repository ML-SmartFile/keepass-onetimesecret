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
        private readonly CheckBox m_generatePassphrase;
        private readonly TextBox m_credentialDisplay;

        private string m_credentialUuidHex;

        public OptionsForm(
            string baseUrl,
            string apiVersion,
            int defaultTtlSeconds,
            string template,
            bool allowInsecureHttp,
            bool generatePassphrase,
            string credentialUuidHex,
            string credentialDisplay,
            Func<IWin32Window, CredentialSelection> pickEntry)
        {
            if (pickEntry == null) throw new ArgumentNullException("pickEntry");
            m_pickEntry = pickEntry;
            m_credentialUuidHex = credentialUuidHex ?? string.Empty;

            Text = "OneTimeSecret Options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
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
            m_apiVersion.SelectedItem = (apiVersion == "v1") ? "v1" : "auto";
            Controls.Add(m_apiVersion);
            Controls.Add(new Label
            {
                Text = "(v2 arrives in a later version)",
                ForeColor = SystemColors.GrayText,
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

            Controls.Add(new Label
            {
                Text = "The API token is never written to KeePass.config.xml - only a reference to " +
                       "the entry is stored, and the token is read at the moment of sharing.",
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(160, y, 424, 32)
            });
            y += 42;

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
                Bounds = new Rectangle(160, y, 424, 20)
            });
            y += 26;

            m_generatePassphrase = new CheckBox
            {
                Text = "Protect shares with a generated passphrase (takes effect in a later version)",
                Checked = generatePassphrase,
                Bounds = new Rectangle(160, y, 424, 22)
            };
            Controls.Add(m_generatePassphrase);

            var ok = new Button { Text = "OK", Bounds = new Rectangle(418, 462, 80, 26) };
            ok.Click += OnOk;
            Controls.Add(ok);

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(504, 462, 80, 26)
            };
            Controls.Add(cancel);
            CancelButton = cancel;

            SyncRegionToBaseUrl();
        }

        public string ResultBaseUrl { get; private set; }
        public string ResultApiVersion { get; private set; }
        public int ResultTtlSeconds { get; private set; }
        public string ResultTemplate { get; private set; }
        public bool ResultAllowInsecureHttp { get; private set; }
        public bool ResultGeneratePassphrase { get; private set; }
        public string ResultCredentialUuidHex { get; private set; }

        private static Label Header(string text, int top)
        {
            return new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DialogFont, FontStyle.Bold),
                Bounds = new Rectangle(14, top, 570, 20)
            };
        }

        private static Label Label(string text, int top)
        {
            return new Label { Text = text, Bounds = new Rectangle(14, top, 140, 20) };
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
            ResultGeneratePassphrase = m_generatePassphrase.Checked;
            ResultCredentialUuidHex = m_credentialUuidHex;

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
