using System;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePassLib;

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
            PwEntry entry = m_host.MainWindow.GetSelectedEntry(true);
            if (entry == null) return;

            string title = entry.Strings.ReadSafe(PwDefs.TitleField);
            MessageBox.Show(m_host.MainWindow, title, "OneTimeSecret",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
