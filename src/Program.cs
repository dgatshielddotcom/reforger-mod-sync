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
        readonly Label[] totals = new Label[4];
        readonly Label cleanupHint = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly ToolTip tips = new ToolTip();
        SyncPlan plan;
        bool busy;
        bool settingServerText;
        ServerCandidate target = new ServerCandidate { Id = ServerSource.ServerId, Name = "JSOTF | PvE REALISM | Public Operation Server 1", Host = ServerSource.ExpectedHost, Port = ServerSource.ExpectedPort, Online = true };
        string importedFile;
        bool confirmedEmpty;

        public MainForm()
        {
            Text = "Reforger Mod Sync";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            var screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1040, screen.Width - 80), Math.Min(740, screen.Height - 80));
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            ForeColor = Color.FromArgb(31, 41, 55);
            BackColor = Color.FromArgb(244, 246, 248);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(20, 16, 20, 12) };
            foreach (int height in new[] { 62, 142, 76, 38 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 44, 74, 24 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.Controls.Add(new Label { Text = "Reforger Mod Sync", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0) }, 0, 0);
            header.Controls.Add(Caption("Keep the mods your server needs."), 0, 1);
            StyleButton(page, "Server page", false);
            page.FlatAppearance.BorderSize = 0;
            page.BackColor = BackColor;
            page.Dock = DockStyle.Fill;
            header.Controls.Add(page, 1, 0);
            layout.Controls.Add(header, 0, 0);

            var setup = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, BackColor = Color.White, Padding = new Padding(14, 10, 14, 8), Margin = new Padding(0, 0, 0, 12) };
            setup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            setup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            setup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            setup.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            setup.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            setup.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            setup.Controls.Add(new Label { Text = "Server", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            serverBox.Text = target.Name;
            serverBox.Dock = DockStyle.Fill;
            serverBox.BorderStyle = BorderStyle.FixedSingle;
            serverBox.Margin = new Padding(0, 8, 10, 8);
            tips.SetToolTip(serverBox, "Server name, ReforgerMods link, or server UUID.");
            setup.Controls.Add(serverBox, 1, 0);
            StyleButton(find, "Find server", false);
            StyleButton(import, "Import JSON", false);
            find.Dock = import.Dock = DockStyle.Fill;
            setup.Controls.Add(find, 2, 0);
            setup.Controls.Add(import, 3, 0);
            setup.Controls.Add(new Label { Text = "Mods folder", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            pathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "ArmaReforger", "addons");
            pathBox.Dock = DockStyle.Fill;
            pathBox.BorderStyle = BorderStyle.FixedSingle;
            pathBox.Margin = new Padding(0, 8, 10, 8);
            setup.Controls.Add(pathBox, 1, 1);
            setup.SetColumnSpan(pathBox, 2);
            StyleButton(browse, "Browse", false);
            browse.Dock = DockStyle.Fill;
            setup.Controls.Add(browse, 3, 1);
            selectedServer.Text = target.Name + "  |  " + target.Host + ":" + target.Port;
            selectedServer.ForeColor = Color.FromArgb(94, 108, 124);
            selectedServer.Font = new Font("Segoe UI", 9);
            selectedServer.Dock = DockStyle.Fill;
            selectedServer.TextAlign = ContentAlignment.MiddleLeft;
            selectedServer.AutoEllipsis = true;
            selectedServer.Margin = new Padding(0);
            setup.Controls.Add(selectedServer, 0, 2);
            setup.SetColumnSpan(selectedServer, 4);
            layout.Controls.Add(setup, 0, 1);

            var counts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            counts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string[] titles = { "ON SERVER", "LOCAL MODS", "EXTRA MODS", "MISSING" };
            for (int i = 0; i < totals.Length; i++)
            {
                counts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(0, 0, i == 3 ? 0 : 8, 0) };
                card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                totals[i] = new Label { Text = "—", Dock = DockStyle.Fill, Margin = new Padding(0), Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = i == 2 ? Color.FromArgb(165, 87, 25) : ForeColor };
                var title = Caption(titles[i]);
                card.Controls.Add(totals[i], 0, 0);
                card.Controls.Add(title, 0, 1);
                counts.Controls.Add(card, i, 0);
            }
            layout.Controls.Add(counts, 0, 2);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            summary.Text = "Mods";
            summary.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            summary.Dock = DockStyle.Fill;
            summary.TextAlign = ContentAlignment.MiddleLeft;
            toolbar.Controls.Add(summary, 0, 0);
            filter.DropDownStyle = ComboBoxStyle.DropDownList;
            filter.Items.AddRange(new object[] { "Changes", "All mods", "Missing", "Kept", "Unknown" });
            filter.SelectedIndex = 0;
            filter.Dock = DockStyle.Fill;
            filter.Margin = new Padding(0, 5, 10, 5);
            toolbar.Controls.Add(filter, 1, 0);
            StyleButton(refresh, "Refresh", false);
            refresh.Dock = DockStyle.Fill;
            refresh.Margin = new Padding(0, 0, 0, 4);
            toolbar.Controls.Add(refresh, 2, 0);
            layout.Controls.Add(toolbar, 0, 3);

            StyleGrid(grid);
            grid.AutoGenerateColumns = false;
            AddColumn("Mod", 260, true);
            AddColumn("Status", 100, false);
            AddColumn("Action", 178, false);
            AddColumn("Mod ID", 158, false);
            layout.Controls.Add(grid, 0, 4);

            details.Dock = DockStyle.Fill;
            details.Multiline = true;
            details.ReadOnly = true;
            details.TabStop = false;
            details.ScrollBars = ScrollBars.Vertical;
            details.BackColor = BackColor;
            details.ForeColor = Color.FromArgb(94, 108, 124);
            details.Font = new Font("Segoe UI", 9);
            details.BorderStyle = BorderStyle.None;
            details.Margin = new Padding(0, 8, 0, 0);
            details.Text = "Click Refresh to compare the selected server, or find another server.";
            layout.Controls.Add(details, 0, 5);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Margin = new Padding(0, 6, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            StyleButton(restore, "Restore disabled", false);
            restore.FlatAppearance.BorderSize = 0;
            restore.BackColor = BackColor;
            StyleButton(disable, "Disable extras", false);
            StyleButton(sync, "Delete extra mods", true);
            restore.Dock = disable.Dock = sync.Dock = DockStyle.Fill;
            actions.Controls.Add(restore, 0, 0);
            actions.Controls.Add(disable, 1, 0);
            permanent.Text = "Delete permanently";
            permanent.Dock = DockStyle.Fill;
            permanent.Margin = new Padding(6, 0, 0, 0);
            tips.SetToolTip(permanent, "Skip the Recycle Bin. This cannot be undone.");
            actions.Controls.Add(permanent, 3, 0);
            sync.Margin = new Padding(0, 3, 0, 3);
            actions.Controls.Add(sync, 4, 0);
            cleanupHint.Text = "Deletes go to Recycle Bin. Versions are ignored.";
            cleanupHint.ForeColor = Color.FromArgb(94, 108, 124);
            cleanupHint.Font = new Font("Segoe UI", 9);
            cleanupHint.Dock = DockStyle.Fill;
            cleanupHint.TextAlign = ContentAlignment.MiddleRight;
            actions.Controls.Add(cleanupHint, 0, 1);
            actions.SetColumnSpan(cleanupHint, 5);
            sync.Enabled = disable.Enabled = false;
            layout.Controls.Add(actions, 0, 6);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 258));
            status.Dock = source.Dock = DockStyle.Fill;
            status.ForeColor = source.ForeColor = Color.FromArgb(94, 108, 124);
            status.Font = source.Font = new Font("Segoe UI", 9);
            status.TextAlign = ContentAlignment.MiddleLeft;
            source.TextAlign = ContentAlignment.MiddleRight;
            status.AutoEllipsis = source.AutoEllipsis = true;
            status.Text = "Ready";
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 7, 8, 7);
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = false;
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(progress, 1, 0);
            footer.Controls.Add(source, 2, 0);
            layout.Controls.Add(footer, 0, 7);

            refresh.Click += async (s, e) => await Preview();
            find.Click += async (s, e) => await FindServer();
            import.Click += async (s, e) => await ImportList();
            sync.Click += async (s, e) => await Synchronize(permanent.Checked ? "delete" : "recycle");
            disable.Click += async (s, e) => await Synchronize("disable");
            restore.Click += async (s, e) => await Restore();
            browse.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { Description = "Choose your addons folder", SelectedPath = pathBox.Text, ShowNewFolderButton = false }) if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.SelectedPath; };
            page.Click += (s, e) => { try { if (target != null) Process.Start(ServerSource.CanonicalUrl(target)); } catch (Exception ex) { ShowError(ex); } };
            pathBox.TextChanged += (s, e) => { InvalidatePlan(); status.Text = "Folder changed. Click Refresh."; };
            filter.SelectedIndexChanged += (s, e) => RenderRows();
            permanent.CheckedChanged += (s, e) => { sync.Text = permanent.Checked ? "Delete permanently" : "Delete extra mods"; sync.BackColor = sync.FlatAppearance.BorderColor = permanent.Checked ? Color.FromArgb(178, 55, 55) : Color.FromArgb(41, 99, 190); cleanupHint.Text = permanent.Checked ? "Permanent deletion cannot be undone. Versions are ignored." : "Deletes go to Recycle Bin. Versions are ignored."; RenderRows(); };
            serverBox.TextChanged += (s, e) => { if (!settingServerText) { target = null; importedFile = null; confirmedEmpty = false; InvalidatePlan(); page.Enabled = false; selectedServer.Text = "Select a server or import its mod list."; status.Text = "Click Find server to continue."; } };
            serverBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter && !busy) { e.SuppressKeyPress = true; await FindServer(); } };
            Shown += (s, e) => { ActiveControl = serverBox; status.Text = "Choose a server or click Refresh."; };
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = "Wait for the current operation to finish."; } };
        }

        static Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(94, 108, 124), Font = new Font("Segoe UI", 9), Margin = new Padding(0), TextAlign = ContentAlignment.MiddleLeft };
        }

        internal static void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(150, 36);
            button.Margin = new Padding(0, 3, 8, 3);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(41, 99, 190) : Color.FromArgb(217, 223, 230);
            button.BackColor = primary ? Color.FromArgb(41, 99, 190) : Color.White;
            button.ForeColor = primary ? Color.White : Color.FromArgb(31, 41, 55);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        internal static void StyleGrid(DataGridView table)
        {
            table.Dock = DockStyle.Fill;
            table.ReadOnly = true;
            table.AllowUserToAddRows = table.AllowUserToDeleteRows = table.AllowUserToResizeRows = false;
            table.RowHeadersVisible = false;
            table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            table.MultiSelect = false;
            table.BackgroundColor = Color.White;
            table.BorderStyle = BorderStyle.None;
            table.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            table.GridColor = Color.FromArgb(236, 239, 243);
            table.EnableHeadersVisualStyles = false;
            table.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 239, 244);
            table.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(75, 89, 105);
            table.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            table.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            table.ColumnHeadersHeight = 34;
            table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            table.RowTemplate.Height = 32;
            table.DefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 237, 252);
            table.DefaultCellStyle.SelectionForeColor = Color.FromArgb(31, 41, 55);
            table.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 253);
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
            page.Enabled = !value && target != null;
            grid.Enabled = filter.Enabled = permanent.Enabled = !value;
            sync.Enabled = !value && plan != null && plan.DisableCount + plan.EnableCount > 0;
            disable.Enabled = sync.Enabled;
            UseWaitCursor = value;
            progress.Visible = value;
            if (message != null) status.Text = message;
        }

        async Task Preview()
        {
            if (busy) return;
            if (target == null && importedFile == null) { status.Text = "Select a server or import JSON first."; return; }
            string root = pathBox.Text.Trim();
            ServerCandidate selected = CopyTarget();
            string file = importedFile;
            bool empty = confirmedEmpty;
            plan = null;
            grid.Rows.Clear();
            summary.Text = "Checking mods...";
            foreach (var total in totals) total.Text = "—";
            SetBusy(true, "Reading mods...");
            try
            {
                plan = await Task.Run(() => ModEngine.BuildPlan(root, ReadSnapshot(selected, file, empty)));
                RenderPlan();
                status.Text = "Preview ready";
            }
            catch (Exception ex) { summary.Text = "Couldn't load mods"; source.Text = ""; details.Text = "Click Refresh to try again."; ShowError(ex); }
            finally { SetBusy(false, null); }
        }

        void RenderPlan()
        {
            if (plan == null) return;
            int[] values = { plan.Snapshot.Mods.Count, plan.Active.Count(m => string.IsNullOrEmpty(m.Warning)), plan.DisableCount, plan.MissingCount };
            for (int i = 0; i < totals.Length; i++) totals[i].Text = values[i].ToString();
            summary.Text = plan.DisableCount + plan.EnableCount == 0 ? "No cleanup needed" : "Mods";
            selectedServer.Text = plan.Snapshot.Name + (string.IsNullOrEmpty(plan.Snapshot.Host) ? "" : "  |  " + plan.Snapshot.Host + ":" + plan.Snapshot.Port);
            source.Text = (importedFile == null ? "Server checked at " : "JSON read at ") + plan.Snapshot.FetchedUtc.ToLocalTime().ToString("HH:mm:ss");
            RenderRows();
        }

        void RenderRows()
        {
            grid.SelectionChanged -= GridSelectionChanged;
            grid.Rows.Clear();
            if (plan == null) return;
            IEnumerable<PlanRow> rows = plan.Rows;
            string chosen = Convert.ToString(filter.SelectedItem);
            if (chosen == "Changes") rows = rows.Where(r => r.Status == "Disable" || r.Status == "Restore");
            else if (chosen == "Missing") rows = rows.Where(r => r.Status == "Missing");
            else if (chosen == "Kept") rows = rows.Where(r => r.Status.StartsWith("Keep") || r.Status == "Version differs");
            else if (chosen == "Unknown") rows = rows.Where(r => r.Status == "Unknown");
            foreach (var row in rows.OrderBy(r => Order(r.Status)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                int index = grid.Rows.Add(row.Name, RowStatus(row), RowAction(row), row.Id);
                grid.Rows[index].Tag = row;
                foreach (DataGridViewCell cell in grid.Rows[index].Cells) cell.ToolTipText = row.Folder + (row.Local != null && !string.IsNullOrEmpty(row.Local.Warning) ? "\r\n" + row.Local.Warning : "");
                if (row.Status == "Disable") grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(139, 78, 14);
                else if (row.Status == "Missing" || row.Status == "Version differs") grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(155, 61, 59);
                else if (row.Status == "Restore") grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(33, 113, 96);
            }
            if (grid.Rows.Count > 0) grid.ClearSelection();
            grid.SelectionChanged += GridSelectionChanged;
            ShowPlanDetails();
        }

        string RowStatus(PlanRow row)
        {
            return row.Status == "Disable" ? "Extra" : row.Status;
        }

        string RowAction(PlanRow row)
        {
            if (row.Status == "Disable") return permanent.Checked ? "Delete permanently" : "Recycle Bin";
            switch (row.Status)
            {
                case "Restore": return "Restore";
                case "Missing": return "Join server to download";
                case "Keep": return "Keep";
                case "Disabled": return "Leave disabled";
                case "Unknown": return "Leave alone";
                default: return row.Action;
            }
        }

        void ShowPlanDetails()
        {
            if (plan == null) return;
            var notes = new List<string>();
            if (plan.UnknownCount > 0) notes.Add(plan.UnknownCount + " unknown folders left alone.");
            if (plan.EnableCount > 0) notes.Add(plan.EnableCount + " required mods will be restored.");
            if (plan.MissingCount > 0) notes.Add("Join the server to download missing mods.");
            details.Text = importedFile == null ? string.Join("  ", notes) : "JSON: " + importedFile + "\r\n" + string.Join("  ", notes);
        }

        void GridSelectionChanged(object sender, EventArgs e)
        {
            if (plan == null) return;
            if (grid.SelectedRows.Count == 0) { ShowPlanDetails(); return; }
            var row = grid.SelectedRows[0].Tag as PlanRow;
            if (row == null) return;
            details.Text = row.Name + "  |  " + RowStatus(row) + "  |  " + RowAction(row) + "\r\n" + (string.IsNullOrEmpty(row.Folder) ? "Join the server to download this mod." : row.Folder);
            if (row.Local != null && !string.IsNullOrEmpty(row.Local.Warning)) details.Text += "\r\n" + row.Local.Warning;
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
            SetBusy(true, "Checking mods before cleanup...");
            try
            {
                ModEngine.EnsureGameClosed();
                plan = await Task.Run(() => ModEngine.BuildPlan(root, ReadSnapshot(selected, file, empty)));
                RenderPlan();
                if (plan.DisableCount + plan.EnableCount == 0) { status.Text = "No cleanup needed."; return; }
                string confirmation = mode == "disable" ? string.Format("Move {0} extra mods to:\r\n{1}\r\n\r\nUse Restore disabled to bring them back.", plan.DisableCount, plan.VaultRoot) : string.Format("{0} {1} extra mods from:\r\n{2}\r\n\r\n{3}", mode == "delete" ? "Permanently delete" : "Recycle", plan.DisableCount, plan.AddonsRoot, mode == "delete" ? "This cannot be undone." : "You can recover them from the Recycle Bin.");
                confirmation = selectedServer.Text + "\r\n" + (file == null ? plan.Snapshot.Id : file) + "\r\n\r\n" + confirmation;
                if (plan.EnableCount > 0) confirmation += "\r\n\r\nAlso restore " + plan.EnableCount + " required mods.";
                if (MessageBox.Show(this, confirmation, mode == "disable" ? "Disable extras?" : "Delete extras?", MessageBoxButtons.OKCancel, mode == "delete" ? MessageBoxIcon.Warning : MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) { status.Text = "Cancelled"; return; }
                status.Text = mode == "disable" ? "Disabling extras..." : "Deleting extras...";
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
            if (MessageBox.Show(this, "Restore all disabled mods to addons?\r\n\r\nThis includes mods your server doesn't use. Existing folders are skipped.", "Restore disabled?", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            SetBusy(true, "Restoring mods...");
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
            status.Text = ex.Message;
            tips.SetToolTip(status, ex.Message);
            MessageBox.Show(this, ex.Message, "Reforger Mod Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void InvalidatePlan()
        {
            plan = null;
            grid.Rows.Clear();
            summary.Text = "Mods";
            foreach (var total in totals) total.Text = "—";
            source.Text = "";
            sync.Enabled = false;
            disable.Enabled = false;
            details.Text = "Click Refresh to compare mods.";
        }

        ServerCandidate CopyTarget()
        {
            return target == null ? null : new ServerCandidate { Id = target.Id, Name = target.Name, Host = target.Host, Port = target.Port, Players = target.Players, ModCount = target.ModCount, Online = target.Online };
        }

        static ServerSnapshot ReadSnapshot(ServerCandidate selected, string file, bool empty)
        {
            if (file != null) return ManualModList.Load(file, empty);
            if (selected == null) throw new InvalidOperationException("Select a server first.");
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
            SetBusy(true, "Searching servers...");
            ServerCandidate chosen = null;
            try
            {
                List<ServerCandidate> results = await Task.Run(() => ServerSource.Search(query));
                if (results.Count == 0) { selectedServer.Text = "No server selected."; status.Text = "No matches. Try a shorter name or import JSON."; return; }
                using (var picker = new ServerPicker(results)) if (picker.ShowDialog(this) == DialogResult.OK) chosen = picker.Selected;
                if (chosen == null) { selectedServer.Text = "No server selected."; status.Text = "Cancelled"; return; }
                target = chosen;
                settingServerText = true;
                try { serverBox.Text = chosen.Name; } finally { settingServerText = false; }
                selectedServer.Text = chosen.Name;
            }
            catch (Exception ex) { selectedServer.Text = "No server selected."; ShowError(ex); }
            finally { SetBusy(false, null); }
            if (chosen != null) await Preview();
        }

        async Task ImportList()
        {
            if (busy) return;
            string file;
            using (var picker = new OpenFileDialog { Title = "Import server mod list", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                file = picker.FileName;
            }
            InvalidatePlan();
            target = null;
            importedFile = null;
            confirmedEmpty = false;
            SetBusy(true, "Reading JSON...");
            try
            {
                ServerSnapshot snapshot = await Task.Run(() => ManualModList.Load(file, false));
                if (snapshot.Mods.Count == 0)
                {
                    if (MessageBox.Show(this, "This list is empty. Cleanup will remove every recognized local mod.\r\n\r\nContinue only if this is the complete list for a server with no mods.", "Use empty mod list?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { selectedServer.Text = "No server selected."; status.Text = "Cancelled"; return; }
                    confirmedEmpty = true;
                }
                importedFile = file;
                settingServerText = true;
                try { serverBox.Text = "JSON: " + Path.GetFileName(file); } finally { settingServerText = false; }
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
            Text = "Choose a server";
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(920, 440);
            MinimumSize = new Size(760, 360);
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(244, 246, 248);
            ForeColor = Color.FromArgb(31, 41, 55);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 14), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "Pick your server. If it's missing, try a more specific name.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(94, 108, 124), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            MainForm.StyleGrid(servers);
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
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { DialogResult = DialogResult.Cancel };
            MainForm.StyleButton(cancel, "Cancel", false);
            MainForm.StyleButton(select, "Select server", true);
            select.Enabled = false;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(select);
            layout.Controls.Add(buttons, 0, 2);
            CancelButton = cancel;
            AcceptButton = select;
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
