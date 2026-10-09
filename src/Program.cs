using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReforgerModSync
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        readonly TextBox serverBox = new TextBox();
        readonly Button find = new Button();
        readonly Button import = new Button();
        readonly Label selectedServer = new Label();
        readonly TextBox pathBox = new TextBox();
        readonly Button browse = new Button();
        readonly Button refresh = new Button();
        readonly Button sync = new Button();
        readonly Button disable = new Button();
        readonly Button restore = new Button();
        readonly Button page = new Button();
        readonly Label summary = new Label();
        readonly Label status = new Label();
        readonly Label source = new Label();
        readonly TextBox details = new TextBox();
        readonly ComboBox filter = new ComboBox();
        readonly CheckBox permanent = new CheckBox();
        readonly DataGridView grid = new DataGridView();
        SyncPlan plan;
        bool busy;
        bool settingServerText;
        ServerCandidate target = new ServerCandidate { Id = ServerSource.ServerId, Name = "JSOTF | PvE REALISM | Public Operation Server 1", Host = ServerSource.ExpectedHost, Port = ServerSource.ExpectedPort, Online = true };
        string importedFile;
        bool confirmedEmpty;

        public MainForm()
        {
            Text = "Reforger Mod Sync";
            ClientSize = new Size(1130, 815);
            MinimumSize = new Size(980, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(245, 247, 249);
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(22, 16, 22, 16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            Controls.Add(layout);

            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "Reforger Mod Sync", AutoSize = true, Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = Color.FromArgb(27, 47, 62), Location = new Point(0, 0) });
            selectedServer.Text = "JSOTF | PvE Realism | Public Operation Server 1 | 108.171.199.178:10500";
            selectedServer.ForeColor = Color.FromArgb(72, 86, 99);
            selectedServer.Location = new Point(2, 48);
            selectedServer.Size = new Size(1050, 29);
            selectedServer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            selectedServer.AutoEllipsis = true;
            header.Controls.Add(selectedServer);
            layout.Controls.Add(header, 0, 0);

            var serverRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            serverRow.Controls.Add(new Label { Text = "Server", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            serverBox.Text = "https://reforgermods.net/servers/" + ServerSource.ServerId;
            serverBox.Dock = DockStyle.Fill;
            serverBox.Margin = new Padding(0, 12, 10, 8);
            new ToolTip().SetToolTip(serverBox, "Search by server name, or paste a ReforgerMods server URL or UUID. BattleMetrics links may require searching the server name instead.");
            serverRow.Controls.Add(serverBox, 1, 0);
            StyleButton(find, "Find server", false);
            StyleButton(import, "Import mod list...", false);
            find.Dock = import.Dock = DockStyle.Fill;
            find.Margin = new Padding(0, 6, 10, 8);
            import.Margin = new Padding(0, 6, 0, 8);
            serverRow.Controls.Add(find, 2, 0);
            serverRow.Controls.Add(import, 3, 0);
            layout.Controls.Add(serverRow, 0, 1);

            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            pathRow.Controls.Add(new Label { Text = "Addons folder", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            pathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "ArmaReforger", "addons");
            pathBox.Dock = DockStyle.Fill;
            pathBox.Margin = new Padding(0, 12, 10, 8);
            pathRow.Controls.Add(pathBox, 1, 0);
            StyleButton(browse, "Browse...", false);
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(0, 6, 0, 8);
            pathRow.Controls.Add(browse, 2, 0);
            layout.Controls.Add(pathRow, 0, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            StyleButton(refresh, "Refresh & preview", false);
            StyleButton(sync, "Delete extra mods", true);
            StyleButton(disable, "Disable extras", false);
            StyleButton(restore, "Restore all disabled", false);
            StyleButton(page, "Open server page", false);
            buttons.Controls.AddRange(new Control[] { refresh, sync, disable, restore, page });
            foreach (Button button in buttons.Controls) { button.Width = 160; button.Margin = new Padding(0, 3, 8, 3); }
            sync.Enabled = false;
            disable.Enabled = false;
            layout.Controls.Add(buttons, 0, 3);

            var overview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));
            summary.Text = "Ready to compare your local mods with the server.";
            summary.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            summary.Dock = DockStyle.Fill;
            summary.TextAlign = ContentAlignment.MiddleLeft;
            source.Text = "Cleanup matches mod IDs only. Close Arma Reforger before deleting mods.";
            source.ForeColor = Color.FromArgb(72, 86, 99);
            source.Dock = DockStyle.Fill;
            source.AutoEllipsis = true;
            filter.DropDownStyle = ComboBoxStyle.DropDownList;
            filter.Items.AddRange(new object[] { "All mods", "Changes to make", "Missing mods", "Kept mods", "Unknown folders" });
            filter.SelectedIndex = 0;
            filter.Dock = DockStyle.Fill;
            filter.Margin = new Padding(12, 8, 0, 0);
            overview.Controls.Add(summary, 0, 0);
            overview.Controls.Add(filter, 1, 0);
            overview.Controls.Add(source, 0, 1);
            permanent.Text = "Delete permanently";
            permanent.Dock = DockStyle.Fill;
            permanent.Margin = new Padding(12, 0, 0, 0);
            new ToolTip().SetToolTip(permanent, "Unchecked: send deleted mods to the Windows Recycle Bin. Checked: permanently remove them and reclaim disk space.");
            overview.Controls.Add(permanent, 1, 1);
            layout.Controls.Add(overview, 0, 4);

            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoGenerateColumns = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Color.FromArgb(227, 231, 236);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(27, 47, 62);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.ColumnHeadersHeight = 38;
            grid.RowTemplate.Height = 32;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(213, 231, 236);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(27, 47, 62);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 251);
            AddColumn("Status", 120, false);
            AddColumn("Action", 200, false);
            AddColumn("Mod / folder", 260, true);
            AddColumn("Mod ID", 166, false);
            layout.Controls.Add(grid, 0, 5);

            details.Dock = DockStyle.Fill;
            details.Multiline = true;
            details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Vertical;
            details.BackColor = Color.FromArgb(237, 242, 245);
            details.BorderStyle = BorderStyle.FixedSingle;
            details.Margin = new Padding(0, 12, 0, 0);
            details.Text = "Delete extra mods sends folders to the Windows Recycle Bin. Select Delete permanently to remove them and reclaim space.\r\nVersions are ignored. Disable extras keeps files in a separate folder; Restore all disabled returns those files to addons.";
            layout.Controls.Add(details, 0, 6);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.BottomLeft;
            status.ForeColor = Color.FromArgb(72, 86, 99);
            status.Text = "No mods have been changed.";
            layout.Controls.Add(status, 0, 7);

            refresh.Click += async (s, e) => await Preview();
            find.Click += async (s, e) => await FindServer();
            import.Click += async (s, e) => await ImportList();
            sync.Click += async (s, e) => await Synchronize(permanent.Checked ? "delete" : "recycle");
            disable.Click += async (s, e) => await Synchronize("disable");
            restore.Click += async (s, e) => await Restore();
            browse.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { Description = "Select the Arma Reforger addons folder", SelectedPath = pathBox.Text, ShowNewFolderButton = false }) if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.SelectedPath; };
            page.Click += (s, e) => { try { Process.Start(target == null ? "https://reforgermods.net/servers/" : ServerSource.CanonicalUrl(target)); } catch (Exception ex) { ShowError(ex); } };
            pathBox.TextChanged += (s, e) => { plan = null; sync.Enabled = disable.Enabled = false; summary.Text = "Folder changed. Refresh to preview this folder."; grid.Rows.Clear(); };
            filter.SelectedIndexChanged += (s, e) => RenderRows();
            permanent.CheckedChanged += (s, e) => RenderRows();
            serverBox.TextChanged += (s, e) => { if (!settingServerText) { target = null; importedFile = null; confirmedEmpty = false; InvalidatePlan(); selectedServer.Text = "Find and select a server, or import its complete mod list."; status.Text = "Server changed. Select a server before previewing."; } };
            serverBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter && !busy) { e.SuppressKeyPress = true; await FindServer(); } };
            Shown += async (s, e) => await Preview();
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = "Please wait for the current operation to finish."; } };
        }

        static void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(text.Length > 17 ? 185 : 166, 40);
            button.Margin = new Padding(0, 3, 10, 3);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(33, 113, 96) : Color.FromArgb(185, 195, 203);
            button.BackColor = primary ? Color.FromArgb(33, 113, 96) : Color.White;
            button.ForeColor = primary ? Color.White : Color.FromArgb(27, 47, 62);
        }

        void AddColumn(string title, int width, bool fill)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, Width = width, MinimumWidth = 70, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.Automatic });
        }

        void SetBusy(bool value, string message)
        {
            busy = value;
            refresh.Enabled = browse.Enabled = restore.Enabled = pathBox.Enabled = !value;
            find.Enabled = import.Enabled = serverBox.Enabled = !value;
            grid.Enabled = filter.Enabled = permanent.Enabled = !value;
            sync.Enabled = !value && plan != null && plan.DisableCount + plan.EnableCount > 0;
            disable.Enabled = sync.Enabled;
            UseWaitCursor = value;
            if (message != null) status.Text = message;
        }

        async Task Preview()
        {
            if (busy) return;
            if (target == null && importedFile == null) { status.Text = "Find and select a server, or import a mod list first."; return; }
            string root = pathBox.Text.Trim();
            ServerCandidate selected = CopyTarget();
            string file = importedFile;
            bool empty = confirmedEmpty;
            plan = null;
            grid.Rows.Clear();
            summary.Text = "Checking the server and local folders...";
            SetBusy(true, "Reading the live server list and local addon metadata...");
            try
            {
                plan = await Task.Run(() => ModEngine.BuildPlan(root, ReadSnapshot(selected, file, empty)));
                RenderPlan();
                status.Text = "Preview ready. No mods have been changed.";
            }
            catch (Exception ex) { source.Text = "No verified live list. Cleanup is unavailable until Refresh succeeds."; ShowError(ex); }
            finally { SetBusy(false, null); }
        }

        void RenderPlan()
        {
            if (plan == null) return;
            summary.Text = string.Format("{0} server mods   |   {1} local mods   |   {2} extras   |   {3} missing", plan.Snapshot.Mods.Count, plan.Active.Count(m => string.IsNullOrEmpty(m.Warning)), plan.DisableCount, plan.MissingCount);
            selectedServer.Text = plan.Snapshot.Name + (string.IsNullOrEmpty(plan.Snapshot.Host) ? "" : "  |  " + plan.Snapshot.Host + ":" + plan.Snapshot.Port);
            source.Text = (importedFile == null ? "Live list checked " : "Imported list read ") + plan.Snapshot.FetchedUtc.ToLocalTime().ToString("HH:mm:ss") + "  |  Versions ignored";
            details.Text = "Delete extra mods sends folders to the Windows Recycle Bin. Check Delete permanently to remove them and reclaim space.\r\n" + plan.UnknownCount + " unrecognized folder(s) will stay untouched. " + plan.EnableCount + " required disabled mod(s) will be restored.\r\nDisable extras keeps files in: " + plan.VaultRoot;
            if (importedFile != null) details.Text = "Imported list: " + importedFile + "\r\nThis must be the complete required mod list, including dependencies. " + plan.UnknownCount + " unrecognized folder(s) stay untouched.\r\nDeleted folders go to Recycle Bin unless Delete permanently is checked.";
            RenderRows();
        }

        void RenderRows()
        {
            grid.Rows.Clear();
            if (plan == null) return;
            IEnumerable<PlanRow> rows = plan.Rows;
            string chosen = Convert.ToString(filter.SelectedItem);
            if (chosen == "Changes to make") rows = rows.Where(r => r.Status == "Disable" || r.Status == "Restore");
            else if (chosen == "Missing mods") rows = rows.Where(r => r.Status == "Missing");
            else if (chosen == "Kept mods") rows = rows.Where(r => r.Status.StartsWith("Keep") || r.Status == "Version differs");
            else if (chosen == "Unknown folders") rows = rows.Where(r => r.Status == "Unknown");
            foreach (var row in rows.OrderBy(r => Order(r.Status)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                string shownStatus = row.Status == "Disable" ? "Extra" : row.Status;
                string shownAction = row.Status == "Disable" ? (permanent.Checked ? "Delete permanently" : "Send to Recycle Bin") : row.Action;
                int index = grid.Rows.Add(shownStatus, shownAction, row.Name, row.Id);
                grid.Rows[index].Tag = row;
                foreach (DataGridViewCell cell in grid.Rows[index].Cells) cell.ToolTipText = row.Folder + (row.Local != null && !string.IsNullOrEmpty(row.Local.Warning) ? "\r\n" + row.Local.Warning : "");
                if (row.Status == "Disable") grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(139, 78, 14);
                else if (row.Status == "Missing" || row.Status == "Version differs") grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(155, 61, 59);
                else if (row.Status == "Restore") grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(33, 113, 96);
            }
            if (grid.Rows.Count > 0) grid.ClearSelection();
            grid.SelectionChanged -= GridSelectionChanged;
            grid.SelectionChanged += GridSelectionChanged;
        }

        void GridSelectionChanged(object sender, EventArgs e)
        {
            if (plan == null) return;
            if (grid.SelectedRows.Count == 0) return;
            var row = grid.SelectedRows[0].Tag as PlanRow;
            if (row == null) return;
            details.Text = row.Name + "  |  " + row.Status + "  |  " + row.Action + "\r\n" + (string.IsNullOrEmpty(row.Folder) ? "Not downloaded locally. Join the server to install it." : row.Folder) + "\r\n" + (row.Local != null && !string.IsNullOrEmpty(row.Local.Warning) ? row.Local.Warning : "Disabled folder: " + plan.VaultRoot);
        }

        static int Order(string value)
        {
            switch (value) { case "Disable": return 0; case "Restore": return 1; case "Missing": return 2; case "Version differs": return 3; case "Unknown": return 4; default: return 5; }
        }

        async Task Synchronize(string mode)
        {
            if (busy || plan == null) return;
            string root = pathBox.Text.Trim();
            ServerCandidate selected = CopyTarget();
            string file = importedFile;
            bool empty = confirmedEmpty;
            SetBusy(true, "Refreshing the server list before cleanup...");
            try
            {
                ModEngine.EnsureGameClosed();
                plan = await Task.Run(() => ModEngine.BuildPlan(root, ReadSnapshot(selected, file, empty)));
                RenderPlan();
                if (plan.DisableCount + plan.EnableCount == 0) { status.Text = "No extra mods to remove. Versions are ignored."; return; }
                string confirmation = mode == "disable" ? string.Format("Move {0} extra mod folder(s) into:\r\n{1}\r\n\r\nRestore all disabled can return these files to addons.", plan.DisableCount, plan.VaultRoot) : string.Format("{0} {1} extra mod folder(s) from:\r\n{2}\r\n\r\n{3}", mode == "delete" ? "Permanently delete" : "Send to Recycle Bin:", plan.DisableCount, plan.AddonsRoot, mode == "delete" ? "Permanent deletion cannot be undone." : "You can restore deleted folders through the Windows Recycle Bin.");
                confirmation = "Selected server / mod list:\r\n" + selectedServer.Text + "\r\n" + (file == null ? plan.Snapshot.Id : file) + "\r\n\r\n" + confirmation;
                if (plan.EnableCount > 0) confirmation += "\r\n\r\nAlso restore " + plan.EnableCount + " required mod folder(s) from the disabled folder.";
                if (MessageBox.Show(this, confirmation, mode == "disable" ? "Disable extra mods" : "Delete extra mods", MessageBoxButtons.OKCancel, mode == "delete" ? MessageBoxIcon.Warning : MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) { status.Text = "Cleanup cancelled. No mods have been changed."; return; }
                status.Text = mode == "disable" ? "Moving extra mod folders..." : "Deleting extra mod folders...";
                string result = await Task.Run(() => ModEngine.Apply(plan, mode));
                plan = await Task.Run(() => ModEngine.BuildPlan(root, plan.Snapshot));
                RenderPlan();
                status.Text = result;
                MessageBox.Show(this, result, "Cleanup finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { InvalidatePlan(); ShowError(ex); }
            finally { SetBusy(false, null); }
        }

        async Task Restore()
        {
            if (busy) return;
            string root = pathBox.Text.Trim();
            try { ModEngine.EnsureGameClosed(); }
            catch (Exception ex) { ShowError(ex); return; }
            if (MessageBox.Show(this, "Restore all recognized mods from the disabled folder into addons?\r\nExisting folders will be kept and will never be overwritten.\r\n\r\nThis also restores mods that are not on the selected server.", "Restore disabled mods", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            SetBusy(true, "Restoring disabled mod folders...");
            try
            {
                string result = await Task.Run(() => ModEngine.RestoreAll(root));
                if (plan != null) { plan = await Task.Run(() => ModEngine.BuildPlan(root, plan.Snapshot)); RenderPlan(); }
                status.Text = result;
                MessageBox.Show(this, result, "Restore finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { InvalidatePlan(); ShowError(ex); }
            finally { SetBusy(false, null); }
        }

        void ShowError(Exception ex)
        {
            status.Text = "Operation stopped: " + ex.Message;
            MessageBox.Show(this, ex.Message + "\r\n\r\nRefresh to inspect the current state before trying again.", "Reforger Mod Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void InvalidatePlan()
        {
            plan = null;
            grid.Rows.Clear();
            summary.Text = "Refresh to inspect the current mod state.";
            source.Text = "The previous preview is no longer valid.";
            sync.Enabled = false;
            disable.Enabled = false;
            details.Text = "No current preview. Find a server or import its complete mod list, then Refresh & preview.";
        }

        ServerCandidate CopyTarget()
        {
            return target == null ? null : new ServerCandidate { Id = target.Id, Name = target.Name, Host = target.Host, Port = target.Port, Players = target.Players, ModCount = target.ModCount, Online = target.Online };
        }

        static ServerSnapshot ReadSnapshot(ServerCandidate selected, string file, bool empty)
        {
            if (file != null) return ManualModList.Load(file, empty);
            if (selected == null) throw new InvalidOperationException("Select a server before previewing or cleaning up.");
            return ServerSource.Fetch(selected);
        }

        async Task FindServer()
        {
            if (busy) return;
            string query = serverBox.Text.Trim();
            target = null;
            importedFile = null;
            confirmedEmpty = false;
            InvalidatePlan();
            selectedServer.Text = "Searching servers...";
            SetBusy(true, "Searching the public Reforger server list...");
            ServerCandidate chosen = null;
            try
            {
                List<ServerCandidate> results = await Task.Run(() => ServerSource.Search(query));
                if (results.Count == 0) { selectedServer.Text = "No matching server selected."; status.Text = "No servers found. Try a shorter name, a ReforgerMods link, or import a complete mod-list JSON."; return; }
                using (var picker = new ServerPicker(results)) if (picker.ShowDialog(this) == DialogResult.OK) chosen = picker.Selected;
                if (chosen == null) { selectedServer.Text = "No server selected."; status.Text = "Server selection cancelled. No mods have been changed."; return; }
                target = chosen;
                settingServerText = true;
                try { serverBox.Text = ServerSource.CanonicalUrl(chosen); } finally { settingServerText = false; }
                selectedServer.Text = chosen.Name + "  |  Reading server details...";
            }
            catch (Exception ex) { selectedServer.Text = "No server selected."; ShowError(ex); }
            finally { SetBusy(false, null); }
            if (chosen != null) await Preview();
        }

        async Task ImportList()
        {
            if (busy) return;
            string file;
            using (var picker = new OpenFileDialog { Title = "Import a complete Arma Reforger server mod list", Filter = "JSON mod lists / server configs (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                file = picker.FileName;
            }
            InvalidatePlan();
            target = null;
            importedFile = null;
            confirmedEmpty = false;
            SetBusy(true, "Reading the imported mod list...");
            try
            {
                ServerSnapshot snapshot = await Task.Run(() => ManualModList.Load(file, false));
                if (snapshot.Mods.Count == 0)
                {
                    if (MessageBox.Show(this, "This file lists ZERO required mods. Cleanup will remove every recognized local mod folder.\r\n\r\nOnly continue if you know this is the complete list for a server with no mods. An empty or incomplete public response is not proof of that.\r\n\r\nUse this explicitly empty list?", "Confirm empty server mod list", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { selectedServer.Text = "No server selected."; status.Text = "Empty list cancelled. No mods have been changed."; return; }
                    confirmedEmpty = true;
                }
                importedFile = file;
                settingServerText = true;
                try { serverBox.Text = "Imported JSON: " + Path.GetFileName(file); } finally { settingServerText = false; }
                selectedServer.Text = snapshot.Name + "  |  Imported mod list";
            }
            catch (Exception ex) { selectedServer.Text = "No server selected."; ShowError(ex); }
            finally { SetBusy(false, null); }
            if (importedFile != null) await Preview();
        }
    }

    sealed class ServerPicker : Form
    {
        readonly DataGridView servers = new DataGridView();
        readonly Button select = new Button();
        public ServerCandidate Selected { get; private set; }

        public ServerPicker(List<ServerCandidate> candidates)
        {
            Text = "Choose an Arma Reforger server";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 450);
            MinimumSize = new Size(750, 380);
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "Select your server. Its address and full mod list will be checked before cleanup.\r\nShowing up to 100 matches; narrow the name if your server is missing.", Dock = DockStyle.Fill }, 0, 0);
            servers.Dock = DockStyle.Fill;
            servers.ReadOnly = true;
            servers.AllowUserToAddRows = servers.AllowUserToDeleteRows = false;
            servers.RowHeadersVisible = false;
            servers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            servers.MultiSelect = false;
            servers.BackgroundColor = Color.White;
            servers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Server", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200 });
            servers.Columns.Add("Players", "Players");
            servers.Columns.Add("Mods", "Mods");
            servers.Columns.Add("Online", "Online");
            foreach (var candidate in candidates)
            {
                int index = servers.Rows.Add(candidate.Name, candidate.Players, candidate.ModCount, candidate.Online ? "Yes" : "No");
                servers.Rows[index].Tag = candidate;
                foreach (DataGridViewCell cell in servers.Rows[index].Cells) cell.ToolTipText = candidate.Id + (string.IsNullOrEmpty(candidate.Host) ? "" : "  |  " + candidate.Host + ":" + candidate.Port);
            }
            layout.Controls.Add(servers, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(120, 36) };
            select.Text = "Select server";
            select.Size = new Size(150, 36);
            select.Enabled = false;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(select);
            layout.Controls.Add(buttons, 0, 2);
            CancelButton = cancel;
            servers.SelectionChanged += (s, e) => select.Enabled = servers.SelectedRows.Count == 1;
            select.Click += (s, e) => Choose();
            servers.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Choose(); };
            Shown += (s, e) => { servers.ClearSelection(); select.Enabled = false; };
        }

        void Choose()
        {
            if (servers.SelectedRows.Count != 1) return;
            Selected = servers.SelectedRows[0].Tag as ServerCandidate;
            if (Selected == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
