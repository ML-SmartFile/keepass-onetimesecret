using System;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.Util;
using KeePassLib;
using OneTimeSecret.Core;
using OneTimeSecret.KeePassAdapters;
using OneTimeSecret.Ots;

namespace OneTimeSecret
{
    public sealed class OneTimeSecretExt : Plugin
    {
        // M1 scope: fixed endpoint and lifetime, anonymous shares. M2 moves these to config.
        private const string BaseUrl = "https://eu.onetimesecret.com";
        private const int TtlSeconds = 86400;

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
            if (t != PluginMenuType.Entry) return null;

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

            string composedSecret;
            Uri baseUri;
            try
            {
                // Everything the request needs is read here, on the UI thread, before dispatch.
                var fields = EntryFieldReader.ReadResolvedFields(entry, m_host.Database);
                composedSecret = SecretComposer.Compose(SecretComposer.DefaultTemplate, fields);
                baseUri = UrlPolicy.Validate(BaseUrl, false);
            }
            catch (UrlPolicyException ex)
            {
                ShowError(mainWindow, ex.Message);
                return;
            }

            if (string.IsNullOrEmpty(composedSecret.Trim()))
            {
                ShowError(mainWindow, "This entry has no user name or password to share.");
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var client = new OtsClientV1(baseUri, new HttpTransport());
                    ShareResult result = client.Share(new ShareRequest(composedSecret, TtlSeconds));
                    Marshal(mainWindow, delegate { OnShareSucceeded(mainWindow, result, entry); });
                }
                catch (OtsException ex)
                {
                    // OtsException messages are built from status codes and server error text
                    // only - the secret is never passed into an exception we construct.
                    string message = ex.Message;
                    Marshal(mainWindow, delegate { ShowError(mainWindow, message); });
                }
                catch (Exception ex)
                {
                    string message = "Unexpected error while sharing (" + ex.GetType().Name + "): " + ex.Message;
                    Marshal(mainWindow, delegate { ShowError(mainWindow, message); });
                }
            });
        }

        private void OnShareSucceeded(MainForm mainWindow, ShareResult result, PwEntry entry)
        {
            // The database may have locked while the request was in flight.
            PwDatabase database = null;
            if (m_host != null && m_host.Database != null && m_host.Database.IsOpen)
                database = m_host.Database;

            PwEntry clipboardEntry = (database != null) ? entry : null;

            // bSprCompile stays false: the share URL is final and must not be recompiled.
            // bIsEntryInfo stays true so the link counts as entry data for clipboard handling.
            bool copied = ClipboardUtil.Copy(
                result.ShareUrl, false, true, clipboardEntry, database, mainWindow.Handle);

            // Copy alone does NOT arm the auto-clear timer - MainForm owns the countdown, and
            // KeePass's own copy handlers call this straight after copying. Without it the
            // link sits on the clipboard indefinitely.
            if (copied) mainWindow.StartClipboardCountdown();

            MessageBox.Show(mainWindow,
                "The one-time link was copied to the clipboard:" + Environment.NewLine + Environment.NewLine +
                result.ShareUrl + Environment.NewLine + Environment.NewLine +
                "It can be opened once. Anyone with the link can read the secret.",
                "OneTimeSecret", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowError(MainForm mainWindow, string message)
        {
            MessageBox.Show(mainWindow, message, "OneTimeSecret",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Marshals back onto the KeePass UI thread, tolerating shutdown mid-request.</summary>
        private static void Marshal(MainForm mainWindow, MethodInvoker action)
        {
            if (mainWindow == null || mainWindow.IsDisposed) return;

            try
            {
                mainWindow.Invoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
