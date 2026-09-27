using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OneTimeSecret.Ui
{
    /// <summary>
    /// Picks one entry from the open database. Hand-rolled rather than reusing KeePass's
    /// ListViewForm, whose data contract is undocumented and could not be verified without
    /// driving the UI.
    ///
    /// Shows title, user name and group only - never a password, since this list is used to
    /// choose the entry that holds an API token.
    /// </summary>
    internal sealed class EntryPickerForm : Form
    {
        /// <summary>Title, user name, group path, and the opaque payload handed back.</summary>
        internal sealed class Item
        {
            public Item(string title, string userName, string group, object payload)
            {
                Title = title;
                UserName = userName;
                Group = group;
                Payload = payload;
            }

            public string Title { get; private set; }
            public string UserName { get; private set; }
            public string Group { get; private set; }
            public object Payload { get; private set; }
        }

        private readonly List<Item> m_items;
        private readonly ListView m_list;
        private readonly TextBox m_filter;

        public object SelectedPayload { get; private set; }

        public EntryPickerForm(IList<Item> items)
        {
            if (items == null) throw new ArgumentNullException("items");
            m_items = new List<Item>(items);

            Text = "Select the entry holding your OneTimeSecret credentials";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(620, 420);
            MinimumSize = new Size(480, 320);

            var hint = new Label
            {
                Text = "The entry's user name must be your OneTimeSecret account e-mail, " +
                       "and its password the API token.",
                AutoSize = false,
                Bounds = new Rectangle(12, 10, 596, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(hint);

            var filterLabel = new Label
            {
                Text = "Filter:",
                AutoSize = false,
                Bounds = new Rectangle(12, 50, 45, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            Controls.Add(filterLabel);

            m_filter = new TextBox
            {
                Bounds = new Rectangle(60, 47, 548, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            m_filter.TextChanged += delegate { Populate(); };
            Controls.Add(m_filter);

            m_list = new ListView
            {
                Bounds = new Rectangle(12, 80, 596, 296),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            m_list.Columns.Add("Title", 220);
            m_list.Columns.Add("User name", 220);
            m_list.Columns.Add("Group", 140);
            m_list.DoubleClick += delegate { Accept(); };
            Controls.Add(m_list);

            var ok = new Button
            {
                Text = "OK",
                Bounds = new Rectangle(440, 386, 80, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(528, 386, 80, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            Controls.Add(cancel);

            CancelButton = cancel;
            Populate();
            ActiveControl = m_filter;
        }

        private void Populate()
        {
            string filter = m_filter.Text.Trim();

            m_list.BeginUpdate();
            try
            {
                m_list.Items.Clear();
                foreach (Item item in m_items)
                {
                    if (filter.Length > 0 && !Matches(item, filter)) continue;

                    var row = new ListViewItem(item.Title ?? string.Empty);
                    row.SubItems.Add(item.UserName ?? string.Empty);
                    row.SubItems.Add(item.Group ?? string.Empty);
                    row.Tag = item;
                    m_list.Items.Add(row);
                }
            }
            finally
            {
                m_list.EndUpdate();
            }
        }

        private static bool Matches(Item item, string filter)
        {
            return Contains(item.Title, filter)
                || Contains(item.UserName, filter)
                || Contains(item.Group, filter);
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void Accept()
        {
            if (m_list.SelectedItems.Count != 1)
            {
                MessageBox.Show(this, "Select one entry first.", "OneTimeSecret",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = (Item)m_list.SelectedItems[0].Tag;
            SelectedPayload = item.Payload;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
