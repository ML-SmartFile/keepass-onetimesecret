using System;
using System.Collections.Generic;
using System.Net;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePass.Util;
using KeePassLib;
using OneTimeSecret.Core;
using OneTimeSecret.KeePassAdapters;
using OneTimeSecret.Ots;
using OneTimeSecret.Ui;

namespace OneTimeSecret
{
    public sealed class OneTimeSecretExt : Plugin
    {
        private IPluginHost m_host;
        private ToolStripMenuItem m_entryMenuItem;

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            m_host = host;
            HttpTransport.EnableModernTls();
            return true;
        }

        public override void Terminate()
        {
            if (m_host != null && m_host.MainWindow != null && m_host.MainWindow.EntryContextMenu != null)
                m_host.MainWindow.EntryContextMenu.Opening -= OnEntryContextMenuOpening;

            m_host = null;
        }

        public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
        {
            if (t == PluginMenuType.Entry)
            {
                m_entryMenuItem = new ToolStripMenuItem("Share with OneTimeSecret…");
                m_entryMenuItem.Click += OnShareClick;

                ContextMenuStrip entryMenu = m_host.MainWindow.EntryContextMenu;
                if (entryMenu != null)
                {
                    entryMenu.Opening -= OnEntryContextMenuOpening;
                    entryMenu.Opening += OnEntryContextMenuOpening;
                }

                return m_entryMenuItem;
            }

            if (t == PluginMenuType.Main)
            {
                var tsmi = new ToolStripMenuItem("OneTimeSecret Options…");
                tsmi.Click += OnOptionsClick;
                return tsmi;
            }

            return null;
        }

        private void OnEntryContextMenuOpening(object sender, EventArgs e)
        {
            if (m_entryMenuItem == null) return;
            m_entryMenuItem.Enabled = (m_host.MainWindow.GetSelectedEntriesCount() == 1);
        }

        private void OnShareClick(object sender, EventArgs e)
        {
            MainForm mainWindow = (m_host != null) ? m_host.MainWindow : null;
            if (mainWindow == null) return;

            PwEntry entry = mainWindow.GetSelectedEntry(true);
            if (entry == null) return;

            var config = new PluginConfig(m_host.CustomConfig);

            Dictionary<string, string> fields;
            Uri baseUri;
            OtsCredentials credentials;
            try
            {
                // Everything the request needs is read here, on the UI thread, before the
                // dialog opens - the database can lock while it is open or mid-request.
                fields = EntryFieldReader.ReadResolvedFields(entry, m_host.Database);
                baseUri = UrlPolicy.Validate(config.BaseUrl, config.AllowInsecureHttp);

                CredentialResolution resolution = CredentialResolver.Resolve(config, m_host.Database);
                if (resolution.Warning != null && !ConfirmAnonymousFallback(mainWindow, resolution.Warning))
                    return;

                credentials = resolution.Credentials;
            }
            catch (UrlPolicyException ex)
            {
                ShowError(mainWindow, ex.Message +
                    Environment.NewLine + Environment.NewLine +
                    "Fix it under Tools > OneTimeSecret Options.");
                return;
            }

            IWebProxy proxy = ProxyResolver.Resolve();
            int timeoutMs = OtsClientV1.DefaultTimeoutMs;

            var templateFields = new TemplateFields
            {
                Title = config.IncludeTitle,
                UserName = config.IncludeUserName,
                Password = config.IncludePassword,
                Url = config.IncludeUrl,
                Notes = config.IncludeNotes,

                // Whether a message is included is a per-share decision, but where it sits
                // comes from the default template, so a layout set in Options survives.
                Message = false,
                MessageAfterFields = TemplateBuilder.MessageFollowsFields(config.Template),
            };

            PassphraseResolution storedPassphrase = PassphraseResolver.Resolve(config, m_host.Database);

            using (var form = new ShareForm(
                fields,
                templateFields,
                string.Empty,   // the message is per-share and is never carried over
                config.Template,
                config.DefaultTtlSeconds,
                config.PassphraseMode,
                storedPassphrase.Passphrase,
                storedPassphrase.Warning,
                baseUri.Host,
                delegate { return PassphraseGenerator.Generate(m_host); },
                delegate { return (IOtsClient)new OtsClientV1(
                    baseUri, new HttpTransport(proxy), credentials, timeoutMs); }))
            {
                GlobalWindowManager.AddWindow(form);
                DialogResult answer;
                try
                {
                    answer = form.ShowDialog(mainWindow);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }

                // Field selection is a durable preference ("I always share user name and
                // password"), so it is remembered.
                //
                // The message, the template and the passphrase mode are deliberately NOT
                // written back. A message is written for one recipient; persisting it would
                // silently attach it to the next share to someone else, and the generated
                // template contains that message, so storing it polluted the default template
                // configured in Options. The passphrase mode belongs to Options too, or a
                // one-off "None" would quietly become the standing default.
                config.IncludeTitle = form.ResultFields.Title;
                config.IncludeUserName = form.ResultFields.UserName;
                config.IncludePassword = form.ResultFields.Password;
                config.IncludeUrl = form.ResultFields.Url;
                config.IncludeNotes = form.ResultFields.Notes;
                if (form.ResultTtlSeconds > 0) config.DefaultTtlSeconds = form.ResultTtlSeconds;

                if (answer != DialogResult.OK || form.Result == null) return;

                OnShareSucceeded(mainWindow, form.Result, form.PassphraseUsed, entry);
            }
        }

        private void OnShareSucceeded(MainForm mainWindow, ShareResult result, string passphrase, PwEntry entry)
        {
            CopyToClipboard(mainWindow, result.ShareUrl, entry);

            using (var form = new ResultForm(result.ShareUrl, result.ReceiptUrl, passphrase,
                result.RequestedTtlSeconds, result.EffectiveTtlSeconds,
                delegate(string text) { CopyToClipboard(mainWindow, text, entry); }))
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    form.ShowDialog(mainWindow);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        private void OnOptionsClick(object sender, EventArgs e)
        {
            MainForm mainWindow = (m_host != null) ? m_host.MainWindow : null;
            if (mainWindow == null) return;

            var config = new PluginConfig(m_host.CustomConfig);
            PwUuid configured = config.CredentialEntryUuid;
            PwUuid passphraseEntry = config.PassphraseEntryUuid;

            // Qualified: KeePass.Forms has its own OptionsForm, so the bare name is ambiguous.
            using (var form = new Ui.OptionsForm(
                config.BaseUrl,
                config.ApiVersion,
                config.DefaultTtlSeconds,
                config.Template,
                config.AllowInsecureHttp,
                config.PassphraseMode,
                configured.Equals(PwUuid.Zero) ? string.Empty : configured.ToHexString(),
                CredentialResolver.Describe(config, m_host.Database),
                passphraseEntry.Equals(PwUuid.Zero) ? string.Empty : passphraseEntry.ToHexString(),
                PassphraseResolver.Describe(config, m_host.Database),
                delegate(IWin32Window owner) { return PickCredentialEntry(mainWindow, owner); }))
            {
                GlobalWindowManager.AddWindow(form);
                try
                {
                    if (form.ShowDialog(mainWindow) != DialogResult.OK) return;
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }

                config.BaseUrl = form.ResultBaseUrl;
                config.ApiVersion = form.ResultApiVersion;
                config.DefaultTtlSeconds = form.ResultTtlSeconds;
                config.Template = form.ResultTemplate;
                config.AllowInsecureHttp = form.ResultAllowInsecureHttp;
                config.PassphraseMode = form.ResultPassphraseMode;
                config.CredentialEntryUuid = ParseUuid(form.ResultCredentialUuidHex);
                config.PassphraseEntryUuid = ParseUuid(form.ResultPassphraseEntryUuidHex);
            }
        }

        private CredentialSelection PickCredentialEntry(MainForm mainWindow, IWin32Window owner)
        {
            PwDatabase database = m_host.Database;
            if (database == null || !database.IsOpen)
            {
                ShowError(mainWindow, "Open a database first - the credentials are read from " +
                    "one of its entries.");
                return null;
            }

            var items = new List<EntryPickerForm.Item>();
            foreach (PwEntry pe in database.RootGroup.GetEntries(true))
            {
                string group = (pe.ParentGroup != null) ? pe.ParentGroup.Name : string.Empty;
                items.Add(new EntryPickerForm.Item(
                    pe.Strings.ReadSafe(PwDefs.TitleField),
                    pe.Strings.ReadSafe(PwDefs.UserNameField),
                    group,
                    pe));
            }

            using (var picker = new EntryPickerForm(items))
            {
                GlobalWindowManager.AddWindow(picker);
                try
                {
                    if (picker.ShowDialog(owner) != DialogResult.OK) return null;
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(picker);
                }

                var chosen = picker.SelectedPayload as PwEntry;
                if (chosen == null) return null;

                string title = chosen.Strings.ReadSafe(PwDefs.TitleField);
                string user = chosen.Strings.ReadSafe(PwDefs.UserNameField);
                if (string.IsNullOrEmpty(title)) title = "(untitled entry)";

                return new CredentialSelection(
                    chosen.Uuid.ToHexString(),
                    string.IsNullOrEmpty(user) ? title : title + "  -  " + user);
            }
        }

        private static PwUuid ParseUuid(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return PwUuid.Zero;

            try
            {
                byte[] bytes = KeePassLib.Utility.MemUtil.HexStringToByteArray(hex);
                if (bytes == null || bytes.Length != (int)PwUuid.UuidSize) return PwUuid.Zero;
                return new PwUuid(bytes);
            }
            catch (Exception)
            {
                return PwUuid.Zero;
            }
        }

        /// <summary>
        /// The database may lock at any point - while the request is in flight, or while the
        /// result dialog is open - so its state is re-checked on every copy rather than
        /// captured once.
        /// </summary>
        private void CopyToClipboard(MainForm mainWindow, string text, PwEntry entry)
        {
            PwDatabase database = null;
            if (m_host != null && m_host.Database != null && m_host.Database.IsOpen)
                database = m_host.Database;

            PwEntry clipboardEntry = (database != null) ? entry : null;

            // bSprCompile stays false: these URLs are final and must not be recompiled.
            // bIsEntryInfo stays true so the link counts as entry data for clipboard handling.
            bool copied = ClipboardUtil.Copy(text, false, true, clipboardEntry, database, mainWindow.Handle);

            // Copy alone does NOT arm the auto-clear timer - MainForm owns the countdown, and
            // KeePass's own copy handlers call this straight after copying. Without it the
            // link sits on the clipboard indefinitely.
            if (copied) mainWindow.StartClipboardCountdown();
        }

        /// <summary>
        /// Credentials were configured but could not be used. Warn and let the user abort
        /// rather than quietly sending as anonymous, which carries a shorter maximum TTL.
        /// </summary>
        private static bool ConfirmAnonymousFallback(MainForm mainWindow, string warning)
        {
            DialogResult answer = MessageBox.Show(mainWindow,
                warning + Environment.NewLine + Environment.NewLine + "Share anonymously anyway?",
                "OneTimeSecret", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            return answer == DialogResult.OK;
        }

        private static void ShowError(MainForm mainWindow, string message)
        {
            MessageBox.Show(mainWindow, message, "OneTimeSecret",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
