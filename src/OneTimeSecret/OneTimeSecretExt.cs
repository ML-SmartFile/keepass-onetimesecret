using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
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
        private ToolStripMenuItem m_quickShareMenuItem;
        private ToolStripMenuItem m_burnMenuItem;
        private bool m_quickShareInFlight;
        private bool m_burnInFlight;

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
                // A submenu, not a bare item: GetMenuItem hands back exactly one slot per
                // PluginMenuType, and SPEC.md rules out adding a second item directly to
                // entryMenu.Items ourselves - KeePass owns that collection, shares it with
                // every other plugin, and rebuilds it on its own schedule. DropDownItems
                // belong entirely to the ToolStripMenuItem this method returns, so nesting
                // Burn under it stays within the one sanctioned integration point while
                // still keeping both commands on the entry's own right-click menu, not
                // buried under Tools - found confusing there by the user's own testing.
                var root = new ToolStripMenuItem("OneTimeSecret");

                m_entryMenuItem = new ToolStripMenuItem("Share…");
                m_entryMenuItem.Click += OnShareClick;
                root.DropDownItems.Add(m_entryMenuItem);

                m_quickShareMenuItem = new ToolStripMenuItem("Quick Share…");
                m_quickShareMenuItem.Click += OnQuickShareClick;
                root.DropDownItems.Add(m_quickShareMenuItem);

                m_burnMenuItem = new ToolStripMenuItem("Burn Last Share…");
                m_burnMenuItem.Click += OnBurnClick;
                root.DropDownItems.Add(m_burnMenuItem);

                ContextMenuStrip entryMenu = m_host.MainWindow.EntryContextMenu;
                if (entryMenu != null)
                {
                    entryMenu.Opening -= OnEntryContextMenuOpening;
                    entryMenu.Opening += OnEntryContextMenuOpening;
                }

                return root;
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

            bool singleSelected = (m_host.MainWindow.GetSelectedEntriesCount() == 1);
            m_entryMenuItem.Enabled = singleSelected;

            // Quick Share shares the same single-selection gate as Share, plus it must
            // never be clickable again while one is already in flight - there is no
            // dialog open to disable inputs on, unlike Share and Burn.
            if (m_quickShareMenuItem != null)
                m_quickShareMenuItem.Enabled = singleSelected && !m_quickShareInFlight;

            if (m_burnMenuItem == null) return;

            PwEntry entry = singleSelected ? m_host.MainWindow.GetSelectedEntry(true) : null;
            m_burnMenuItem.Enabled = singleSelected && !m_burnInFlight && ReceiptWriter.HasReceipt(entry);
        }

        private void OnShareClick(object sender, EventArgs e)
        {
            MainForm mainWindow = (m_host != null) ? m_host.MainWindow : null;
            if (mainWindow == null) return;

            PwEntry entry = mainWindow.GetSelectedEntry(true);
            if (entry == null) return;

            var config = new PluginConfig(m_host.CustomConfig);
            List<PwDatabase> openDatabases = GetOpenDatabases(mainWindow);

            Dictionary<string, string> fields;
            Uri baseUri;
            OtsCredentials credentials;
            try
            {
                // Everything the request needs is read here, on the UI thread, before the
                // dialog opens - the database can lock while it is open or mid-request.
                fields = EntryFieldReader.ReadResolvedFields(entry, m_host.Database);
                baseUri = UrlPolicy.Validate(config.BaseUrl, config.AllowInsecureHttp);

                CredentialResolution resolution = CredentialResolver.Resolve(config, m_host.Database, openDatabases);
                if (resolution.Warning != null && !ConfirmAnonymousFallback(mainWindow, resolution.Warning, "Share"))
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

            PassphraseResolution storedPassphrase = PassphraseResolver.Resolve(config, m_host.Database, openDatabases);

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
                BuildClientFactory(config, baseUri, credentials, proxy, timeoutMs)))
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

                if (config.WriteBackReceipt) WriteBackReceipt(mainWindow, m_host.Database, entry, form.Result);

                OnShareSucceeded(mainWindow, form.Result, form.PassphraseUsed, entry);
            }
        }

        /// <summary>
        /// Best effort and silent on failure: a receipt write-back that cannot happen (the
        /// database locked between dispatch and here) must not turn a successful share into
        /// an error dialog. The user already has the link either way.
        /// </summary>
        private void WriteBackReceipt(MainForm mainWindow, PwDatabase database, PwEntry entry, ShareResult result)
        {
            if (database == null || !database.IsOpen) return;

            PwEntry current = database.RootGroup.FindEntry(entry.Uuid, true);
            if (current == null) return;

            ReceiptWriter.Write(current, result.MetadataKey, DateTime.UtcNow);
            mainWindow.UpdateUI(false, null, false, null, true, null, true);
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

        /// <summary>
        /// Shares immediately using the configured defaults - no ShareForm. Requested as a
        /// backlog item (ROADMAP.md): the full dialog is right when composing a share
        /// deliberately, but it is friction for "send this entry as configured". Still
        /// satisfies SPEC.md section 2's confirmation requirement (exact host and exact
        /// fields, before any request) with one dialog rather than zero, still opens
        /// ResultForm (a generated passphrase and the receipt link exist nowhere else),
        /// and still gates on the anonymous-fallback warning - the three invariants the
        /// backlog note flagged as non-negotiable for this path.
        /// </summary>
        private void OnQuickShareClick(object sender, EventArgs e)
        {
            MainForm mainWindow = (m_host != null) ? m_host.MainWindow : null;
            if (mainWindow == null) return;

            if (m_quickShareInFlight) return;

            PwEntry entry = mainWindow.GetSelectedEntry(true);
            if (entry == null) return;

            // Captured now, not re-read when the response arrives: unlike ShareForm, nothing
            // here is modal, so the active tab can change or the database can lock while the
            // request is in flight. The receipt write-back and the result must still refer
            // back to where the entry actually came from, not whatever is active later.
            PwDatabase sourceDatabase = m_host.Database;

            var config = new PluginConfig(m_host.CustomConfig);
            List<PwDatabase> openDatabases = GetOpenDatabases(mainWindow);

            Dictionary<string, string> fields;
            Uri baseUri;
            OtsCredentials credentials;
            try
            {
                fields = EntryFieldReader.ReadResolvedFields(entry, m_host.Database);
                baseUri = UrlPolicy.Validate(config.BaseUrl, config.AllowInsecureHttp);

                CredentialResolution resolution = CredentialResolver.Resolve(config, m_host.Database, openDatabases);
                if (resolution.Warning != null && !ConfirmAnonymousFallback(mainWindow, resolution.Warning, "Share"))
                    return;

                credentials = resolution.Credentials;
            }
            catch (UrlPolicyException ex)
            {
                ShowError(mainWindow, ex.Message +
                    Environment.NewLine + Environment.NewLine +
                    "Fix it under Tools > OneTimeSecret > Options.");
                return;
            }

            // Read from the actual template text, not the last-used checkbox state: the
            // template stays directly editable in Options, so the two can diverge - a hand-
            // cleared template with stale "include" flags still set would otherwise confirm
            // fields that are not actually in what gets sent. Found by testing: clearing the
            // template left this check (and the confirmation text below) still claiming
            // "user name, password" while the real request would have been empty.
            TemplateFields templateFields = TemplateBuilder.FieldsPresentIn(config.Template);

            if (!templateFields.Any)
            {
                ShowError(mainWindow, "The configured template has no recognized fields in " +
                    "it, so there is nothing to confirm sharing. Check the template under " +
                    "Tools > OneTimeSecret > Options.");
                return;
            }

            string entryTitle = entry.Strings.ReadSafe(PwDefs.TitleField);
            if (string.IsNullOrEmpty(entryTitle)) entryTitle = "this entry";

            DialogResult confirm = MessageBox.Show(mainWindow,
                "Share " + DescribeIncludedFields(templateFields) + " from \"" + entryTitle +
                "\"" + Environment.NewLine + "with " + baseUri.Host + "?",
                "OneTimeSecret - Quick Share", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            // Quick Share has no message input, so a {MESSAGE} token - unusual, but
            // reachable if the template was hand-edited to include one - must resolve to
            // nothing rather than leak the literal token text into the shared secret.
            var compositionValues = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
            compositionValues[SecretComposer.FieldMessage] = string.Empty;
            string composed = SecretComposer.Compose(config.Template, compositionValues);

            // FieldsPresentIn can only over-report relative to what actually substitutes -
            // a template of just "{MESSAGE}", or a {URL} on an entry with no URL, passes
            // that check yet composes to nothing. Same guard ShareForm already has.
            if (string.IsNullOrEmpty(composed.Trim()))
            {
                ShowError(mainWindow, "There is nothing to share - the configured template " +
                    "produced no content for this entry.");
                return;
            }

            PassphraseMode mode = config.PassphraseMode;
            string passphrase = null;
            if (mode == PassphraseMode.Generate || mode == PassphraseMode.Manual)
            {
                // Manual needs a per-share prompt Quick Share has no dialog to show, so it
                // falls back to generating one instead - still protected, just not the
                // exact configured mode.
                passphrase = PassphraseGenerator.Generate(m_host);
            }
            else if (mode == PassphraseMode.Entry)
            {
                PassphraseResolution stored = PassphraseResolver.Resolve(config, m_host.Database, openDatabases);
                if (stored.Warning != null)
                {
                    ShowError(mainWindow, stored.Warning);
                    return;
                }
                passphrase = stored.Passphrase;
            }

            IWebProxy proxy = ProxyResolver.Resolve();
            int timeoutMs = OtsClientV1.DefaultTimeoutMs;
            Func<CancellationToken, IOtsClient> createClient =
                BuildClientFactory(config, baseUri, credentials, proxy, timeoutMs);

            var request = new ShareRequest(composed, config.DefaultTtlSeconds, passphrase, null);

            m_quickShareInFlight = true;
            mainWindow.SetStatusEx("Sharing via OneTimeSecret...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                ShareResult result = null;
                Exception failure = null;
                try
                {
                    result = createClient(CancellationToken.None).Share(request, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                SafeInvoke(mainWindow, new MethodInvoker(delegate
                {
                    m_quickShareInFlight = false;
                    mainWindow.SetStatusEx(null);

                    if (failure != null)
                    {
                        string message = (failure is OtsException)
                            ? failure.Message
                            : "Unexpected error while sharing (" + failure.GetType().Name + "): " + failure.Message;
                        ShowError(mainWindow, message);
                        return;
                    }

                    // The database this entry came from may have locked, or a different one
                    // may now be active, while the request was in flight - checked against
                    // the captured reference, not whatever m_host.Database is right now.
                    bool sourceStillOpen = sourceDatabase != null && sourceDatabase.IsOpen;

                    if (config.WriteBackReceipt && sourceStillOpen)
                        WriteBackReceipt(mainWindow, sourceDatabase, entry, result);

                    if (!sourceStillOpen)
                    {
                        MessageBox.Show(mainWindow,
                            "The database was locked or is no longer open, so the result " +
                            "cannot be shown here. The share itself may have succeeded - " +
                            "check your OneTimeSecret account, or share again.",
                            "OneTimeSecret", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    OnShareSucceeded(mainWindow, result, passphrase, entry);
                }));
            });
        }

        private static string DescribeIncludedFields(TemplateFields fields)
        {
            var names = new List<string>(5);
            if (fields.Title) names.Add("title");
            if (fields.UserName) names.Add("user name");
            if (fields.Password) names.Add("password");
            if (fields.Url) names.Add("URL");
            if (fields.Notes) names.Add("notes");

            if (names.Count == 0) return string.Empty;
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return names[0] + " and " + names[1];

            string allButLast = string.Join(", ", names.GetRange(0, names.Count - 1).ToArray());
            return allButLast + ", and " + names[names.Count - 1];
        }

        private void OnOptionsClick(object sender, EventArgs e)
        {
            MainForm mainWindow = (m_host != null) ? m_host.MainWindow : null;
            if (mainWindow == null) return;

            // Captured once for the whole call: Options is modal and GlobalWindowManager-
            // registered, so - same reasoning already established for ShareForm in M3 -
            // the active database cannot change while it is open.
            PwDatabase database = m_host.Database;
            bool databaseOpen = database != null && database.IsOpen;
            List<PwDatabase> openDatabases = GetOpenDatabases(mainWindow);

            var config = new PluginConfig(m_host.CustomConfig);

            if (databaseOpen) MigrateLegacyReferences(mainWindow, config, database);

            string credentialUuidHex = databaseOpen ? UuidHex(DatabaseScopedConfig.GetCredentialEntryUuid(database)) : string.Empty;
            string passphraseUuidHex = databaseOpen ? UuidHex(DatabaseScopedConfig.GetPassphraseEntryUuid(database)) : string.Empty;

            FallbackReference credentialFallback = config.CredentialFallback;
            FallbackReference passphraseFallback = config.PassphraseFallback;

            var credentialFallbackState = new FallbackState(
                credentialFallback != null ? UuidHex(credentialFallback.DatabaseUuid) : string.Empty,
                credentialFallback != null ? UuidHex(credentialFallback.EntryUuid) : string.Empty,
                credentialFallback != null ? credentialFallback.Display : string.Empty,
                CredentialResolver.DescribeFallback(config, openDatabases));

            var passphraseFallbackState = new FallbackState(
                passphraseFallback != null ? UuidHex(passphraseFallback.DatabaseUuid) : string.Empty,
                passphraseFallback != null ? UuidHex(passphraseFallback.EntryUuid) : string.Empty,
                passphraseFallback != null ? passphraseFallback.Display : string.Empty,
                PassphraseResolver.DescribeFallback(config, openDatabases));

            string activeDatabaseUuidHex = databaseOpen ? database.RootGroup.Uuid.ToHexString() : string.Empty;
            string activeDatabaseDisplayName = databaseOpen ? DatabaseDisplayName(database) : string.Empty;

            // Qualified: KeePass.Forms has its own OptionsForm, so the bare name is ambiguous.
            using (var form = new Ui.OptionsForm(
                config.BaseUrl,
                config.ApiVersion,
                config.DefaultTtlSeconds,
                config.Template,
                config.AllowInsecureHttp,
                config.WriteBackReceipt,
                config.PassphraseMode,
                credentialUuidHex,
                CredentialResolver.Describe(config, database),
                passphraseUuidHex,
                PassphraseResolver.Describe(config, database),
                credentialFallbackState,
                passphraseFallbackState,
                databaseOpen,
                activeDatabaseUuidHex,
                activeDatabaseDisplayName,
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
                config.WriteBackReceipt = form.ResultWriteBackReceipt;
                config.PassphraseMode = form.ResultPassphraseMode;

                // The per-database primary reference lives in the database's own CustomData
                // (SPEC.md section 6) - only written, and the database only marked modified,
                // when it actually changed, so an Options save that touches nothing but a
                // global setting does not spuriously prompt "save changes?" on exit.
                if (databaseOpen)
                {
                    PwUuid newCredentialUuid = ParseUuid(form.ResultCredentialUuidHex);
                    PwUuid newPassphraseUuid = ParseUuid(form.ResultPassphraseEntryUuidHex);

                    bool credentialChanged = !newCredentialUuid.Equals(DatabaseScopedConfig.GetCredentialEntryUuid(database));
                    bool passphraseChanged = !newPassphraseUuid.Equals(DatabaseScopedConfig.GetPassphraseEntryUuid(database));

                    if (credentialChanged) DatabaseScopedConfig.SetCredentialEntryUuid(database, newCredentialUuid);
                    if (passphraseChanged) DatabaseScopedConfig.SetPassphraseEntryUuid(database, newPassphraseUuid);

                    if (credentialChanged || passphraseChanged)
                        mainWindow.UpdateUI(false, null, false, null, true, null, true);
                }

                // The fallback pointer is global and local-only - saves instantly, like every
                // other setting in this dialog, no database-modified flag involved.
                config.CredentialFallback = ToFallbackReference(form.ResultCredentialFallback);
                config.PassphraseFallback = ToFallbackReference(form.ResultPassphraseFallback);

                // SPEC.md section 5: re-probe capability "when the user hits Save in the
                // options dialog". OtsClientV2's own 404 message tells the user to do
                // exactly this to clear a stale cache - this is what makes that true.
                // Clearing here rather than re-probing synchronously keeps this dialog free
                // of a network call; the next share picks it up lazily.
                config.DetectedVersion = string.Empty;
            }
        }

        /// <summary>
        /// One-time per database: copies the pre-M6 global credential/passphrase reference
        /// into this database's own CustomData, so an existing single-database install keeps
        /// working after upgrading with no re-selection needed. Also promotes it to the
        /// global fallback, if none is configured yet, so a second database opened later
        /// inherits it automatically - a pre-existing single-database user's one working
        /// setup should keep working everywhere without extra clicks.
        /// </summary>
        private static void MigrateLegacyReferences(MainForm mainWindow, PluginConfig config, PwDatabase database)
        {
            if (!DatabaseScopedConfig.NeedsMigration(database)) return;

            PwUuid legacyCredential = config.LegacyCredentialEntryUuid;
            PwUuid legacyPassphrase = config.LegacyPassphraseEntryUuid;

            if (!legacyCredential.Equals(PwUuid.Zero))
            {
                DatabaseScopedConfig.SetCredentialEntryUuid(database, legacyCredential);
                if (config.CredentialFallback == null)
                    config.CredentialFallback = new FallbackReference
                    {
                        DatabaseUuid = database.RootGroup.Uuid,
                        EntryUuid = legacyCredential,
                        Display = DatabaseDisplayName(database),
                    };
            }

            if (!legacyPassphrase.Equals(PwUuid.Zero))
            {
                DatabaseScopedConfig.SetPassphraseEntryUuid(database, legacyPassphrase);
                if (config.PassphraseFallback == null)
                    config.PassphraseFallback = new FallbackReference
                    {
                        DatabaseUuid = database.RootGroup.Uuid,
                        EntryUuid = legacyPassphrase,
                        Display = DatabaseDisplayName(database),
                    };
            }

            DatabaseScopedConfig.MarkMigrated(database);
            mainWindow.UpdateUI(false, null, false, null, true, null, true);
        }

        private static FallbackReference ToFallbackReference(FallbackState state)
        {
            if (state == null || string.IsNullOrEmpty(state.DatabaseUuidHex)) return null;
            return new FallbackReference
            {
                DatabaseUuid = ParseUuid(state.DatabaseUuidHex),
                EntryUuid = ParseUuid(state.EntryUuidHex),
                Display = state.Display,
            };
        }

        private static string UuidHex(PwUuid uuid)
        {
            return (uuid == null || uuid.Equals(PwUuid.Zero)) ? string.Empty : uuid.ToHexString();
        }

        private static string DatabaseDisplayName(PwDatabase database)
        {
            string path = (database.IOConnectionInfo != null) ? database.IOConnectionInfo.Path : null;
            return string.IsNullOrEmpty(path) ? "(this database)" : System.IO.Path.GetFileName(path);
        }

        /// <summary>
        /// All databases currently open in any tab, not just the active one - how the
        /// fallback credential/passphrase lookup finds a database that is not the one the
        /// user is sharing from right now.
        /// </summary>
        private static List<PwDatabase> GetOpenDatabases(MainForm mainWindow)
        {
            var result = new List<PwDatabase>();
            if (mainWindow == null || mainWindow.DocumentManager == null) return result;

            foreach (PwDocument doc in mainWindow.DocumentManager.Documents)
                if (doc.Database != null && doc.Database.IsOpen) result.Add(doc.Database);

            return result;
        }

        /// <summary>
        /// Picks the client per the configured API version. "auto" probes once (cached per
        /// host for 7 days - SPEC.md section 5) and, if it lands on v2, wraps the client so
        /// a stale cache still recovers by retrying once as v1 (section 8's 404 row).
        /// "v1"/"v2" are forced and never probe.
        /// </summary>
        private Func<CancellationToken, IOtsClient> BuildClientFactory(
            PluginConfig config, Uri baseUri, OtsCredentials credentials, IWebProxy proxy, int timeoutMs)
        {
            var transport = new HttpTransport(proxy);

            if (config.ApiVersion == PluginConfig.ApiVersionV1)
                return delegate { return (IOtsClient)new OtsClientV1(baseUri, transport, credentials, timeoutMs); };

            if (config.ApiVersion == PluginConfig.ApiVersionV2)
                return delegate { return (IOtsClient)new OtsClientV2(baseUri, transport, credentials, timeoutMs); };

            var cache = new ApiVersionCache(config);
            return delegate(CancellationToken ct)
            {
                string version = OtsClientFactory.Detect(baseUri, transport, timeoutMs, cache, ct);

                if (version == OtsClientFactory.VersionV1)
                    return (IOtsClient)new OtsClientV1(baseUri, transport, credentials, timeoutMs);

                IOtsClient v2 = new OtsClientV2(baseUri, transport, credentials, timeoutMs);
                return new AutoFallbackOtsClient(
                    v2,
                    delegate { return (IOtsClient)new OtsClientV1(baseUri, transport, credentials, timeoutMs); },
                    delegate { cache.SetCachedVersion(OtsClientFactory.CacheKey(baseUri), OtsClientFactory.VersionV1); });
            };
        }

        private void OnBurnClick(object sender, EventArgs e)
        {
            MainForm mainWindow = (m_host != null) ? m_host.MainWindow : null;
            if (mainWindow == null) return;

            if (m_burnInFlight) return;

            PwEntry entry = mainWindow.GetSelectedEntry(true);
            if (entry == null) return;

            // Captured now, not re-read when the response arrives - same reasoning as
            // Quick Share: nothing here is modal, so the active tab or lock state can
            // change while the request is in flight.
            PwDatabase sourceDatabase = m_host.Database;

            string receiptKey = ReceiptWriter.TryReadReceiptKey(entry);
            if (string.IsNullOrEmpty(receiptKey))
            {
                ShowError(mainWindow, "No OneTimeSecret receipt was found on this entry.");
                return;
            }

            DialogResult confirm = MessageBox.Show(mainWindow,
                "Burn the last OneTimeSecret share for this entry now?" + Environment.NewLine +
                Environment.NewLine +
                "Anyone still holding the link will no longer be able to open it. This cannot be undone.",
                "OneTimeSecret", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK) return;

            var config = new PluginConfig(m_host.CustomConfig);
            List<PwDatabase> openDatabases = GetOpenDatabases(mainWindow);
            Uri baseUri;
            OtsCredentials credentials;
            try
            {
                baseUri = UrlPolicy.Validate(config.BaseUrl, config.AllowInsecureHttp);

                CredentialResolution resolution = CredentialResolver.Resolve(config, m_host.Database, openDatabases);
                // Same confirm-or-abort gate as sharing: degrading to an anonymous request
                // for a destructive action deserves the same explicit confirmation sharing
                // anonymously does, rather than silently trying and failing.
                if (resolution.Warning != null && !ConfirmAnonymousFallback(mainWindow, resolution.Warning, "Burn"))
                    return;

                credentials = resolution.Credentials;
            }
            catch (UrlPolicyException ex)
            {
                ShowError(mainWindow, ex.Message +
                    Environment.NewLine + Environment.NewLine +
                    "Fix it under Tools > OneTimeSecret > Options.");
                return;
            }

            IWebProxy proxy = ProxyResolver.Resolve();
            int timeoutMs = OtsClientV1.DefaultTimeoutMs;
            Func<CancellationToken, IOtsClient> createClient =
                BuildClientFactory(config, baseUri, credentials, proxy, timeoutMs);

            PwUuid entryUuid = entry.Uuid;
            m_burnInFlight = true;
            mainWindow.SetStatusEx("Burning the last OneTimeSecret share...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                BurnResult result = null;
                Exception failure = null;
                try
                {
                    result = createClient(CancellationToken.None).Burn(receiptKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                SafeInvoke(mainWindow, new MethodInvoker(delegate
                {
                    m_burnInFlight = false;
                    mainWindow.SetStatusEx(null);

                    if (failure != null)
                    {
                        string message = (failure is OtsException)
                            ? failure.Message
                            : "Unexpected error while burning (" + failure.GetType().Name + "): " + failure.Message;
                        ShowError(mainWindow, message);
                        return;
                    }

                    if (!result.Burned)
                    {
                        ShowError(mainWindow, result.Detail ??
                            "OneTimeSecret did not confirm the secret was burned.");
                        return;
                    }

                    if (sourceDatabase != null && sourceDatabase.IsOpen)
                    {
                        PwEntry current = sourceDatabase.RootGroup.FindEntry(entryUuid, true);
                        if (current != null)
                        {
                            ReceiptWriter.Remove(current);
                            mainWindow.UpdateUI(false, null, false, null, true, null, true);
                        }
                    }

                    MessageBox.Show(mainWindow, "The share has been burned. The link no longer works.",
                        "OneTimeSecret", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
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
        private static bool ConfirmAnonymousFallback(MainForm mainWindow, string warning, string action)
        {
            DialogResult answer = MessageBox.Show(mainWindow,
                warning + Environment.NewLine + Environment.NewLine + action + " anonymously anyway?",
                "OneTimeSecret", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            return answer == DialogResult.OK;
        }

        private static void ShowError(MainForm mainWindow, string message)
        {
            MessageBox.Show(mainWindow, message, "OneTimeSecret",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Marshals back to the UI thread from a thread-pool callback, tolerating the main
        /// window having gone away while the request was in flight (KeePass closing while
        /// Quick Share or Burn is running - neither is modal, so nothing prevents that). An
        /// unguarded Invoke on a disposed window throws on the thread-pool thread, which is
        /// an unhandled exception under .NET Framework and takes the whole process down.
        /// </summary>
        private static void SafeInvoke(MainForm mainWindow, MethodInvoker action)
        {
            if (mainWindow.IsDisposed || !mainWindow.IsHandleCreated) return;

            try
            {
                mainWindow.Invoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
