using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    public sealed class WeatherLocationDialog : Form
    {
        private readonly TextBox queryBox = new TextBox();
        private readonly Button searchButton = new Button();
        private readonly Button saveButton = new Button();
        private readonly ListView results = new ListView();
        private readonly Label status = new Label();
        private CancellationTokenSource searchCancellation;
        private bool closing;
        private bool searching;
        private bool backgroundPaused;
        public WeatherLocationData SelectedLocation { get; private set; }

        public WeatherLocationDialog(WeatherLocationData current)
        {
            current = (current ?? WeatherLocationData.DefaultZhuji).ValidatedCopy();
            Text = "设置天气所在地"; StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(740, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; Font = new Font("Microsoft YaHei UI", 9F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            var currentLabel = new Label { Dock = DockStyle.Fill, Text = "当前：" + current.DisplayName + "\r\n经纬度：" + current.CoordinatesText };
            layout.Controls.Add(currentLabel, 0, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "输入城市名或邮编；同名地点请核对地区、国家和经纬度。" }, 0, 1);
            var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            queryBox.Dock = DockStyle.Fill; queryBox.MaxLength = 120; queryBox.Text = current.Name;
            searchButton.Text = "搜索"; searchButton.Dock = DockStyle.Fill; searchButton.Click += SearchClicked;
            searchRow.Controls.Add(queryBox, 0, 0); searchRow.Controls.Add(searchButton, 1, 0); layout.Controls.Add(searchRow, 0, 2);
            results.Dock = DockStyle.Fill; results.View = View.Details; results.FullRowSelect = true; results.MultiSelect = false; results.HideSelection = false;
            results.Columns.Add("地点", 158); results.Columns.Add("地区", 225); results.Columns.Add("国家", 108); results.Columns.Add("纬度，经度", 180);
            results.SelectedIndexChanged += delegate { if (!closing) saveButton.Enabled = !searching && !backgroundPaused && results.SelectedItems.Count == 1; };
            layout.Controls.Add(results, 0, 3);
            status.Dock = DockStyle.Fill; status.Text = "搜索后选中一项，再点击“使用此地点”。"; status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new Button { Text = "取消", Width = 86, Height = 30, DialogResult = DialogResult.Cancel };
            saveButton.Text = "使用此地点"; saveButton.Width = 122; saveButton.Height = 30; saveButton.Enabled = false;
            saveButton.Click += SaveClicked; buttons.Controls.Add(cancel); buttons.Controls.Add(saveButton); layout.Controls.Add(buttons, 0, 5);
            Controls.Add(layout); AcceptButton = searchButton; CancelButton = cancel;
            Shown += delegate { queryBox.SelectAll(); queryBox.Focus(); };
        }

        private async void SearchClicked(object sender, EventArgs args)
        {
            if (closing || searching || backgroundPaused) return;
            string query = queryBox.Text.Trim();
            try { WeatherLocationSearch.BuildSearchUrl(query); }
            catch (ArgumentException ex) { status.Text = ex.Message.Split('\r', '\n')[0]; queryBox.Focus(); return; }
            searching = true; searchButton.Enabled = false; queryBox.Enabled = false; saveButton.Enabled = false;
            results.Items.Clear(); status.Text = "正在搜索…";
            var cancellation = new CancellationTokenSource(); searchCancellation = cancellation;
            try {
                var locations = await WeatherLocationSearch.SearchAsync(query, cancellation.Token);
                if (closing || IsDisposed || cancellation.IsCancellationRequested) return;
                foreach (var location in locations) {
                    var item = new ListViewItem(location.Name) { Tag = location };
                    item.SubItems.Add(location.Region); item.SubItems.Add(location.Country ?? ""); item.SubItems.Add(location.CoordinatesText); results.Items.Add(item);
                }
                status.Text = locations.Count == 0 ? "没有找到地点；请尝试其他名称或英文名。原设置未改变。" : "找到 " + locations.Count + " 个地点，请选中后确认。";
            } catch (OperationCanceledException) { }
            catch (Exception) { if (!closing && !IsDisposed) status.Text = "搜索失败，请检查网络后重试。原设置未改变。"; }
            finally {
                if (searchCancellation == cancellation) searchCancellation = null;
                cancellation.Dispose(); searching = false;
                if (!closing && !IsDisposed && !backgroundPaused) { queryBox.Enabled = true; searchButton.Enabled = true; saveButton.Enabled = results.SelectedItems.Count == 1; queryBox.Focus(); }
            }
        }
        private void SaveClicked(object sender, EventArgs args)
        {
            if (closing || searching || backgroundPaused || results.SelectedItems.Count != 1) return;
            SelectedLocation = ((WeatherLocationData)results.SelectedItems[0].Tag).ValidatedCopy();
            DialogResult = DialogResult.OK; Close();
        }
        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            base.OnFormClosing(args);
            if (!args.Cancel) StopSearch();
        }
        protected override void Dispose(bool disposing)
        { if (disposing) StopSearch(); base.Dispose(disposing); }
        private void StopSearch()
        { closing = true; if (searchCancellation != null) searchCancellation.Cancel(); }
        internal void SetBackgroundQueriesPaused(bool paused)
        {
            if (closing || IsDisposed) return;
            backgroundPaused = paused;
            if (paused && searchCancellation != null) searchCancellation.Cancel();
            queryBox.Enabled = searchButton.Enabled = !paused && !searching;
            saveButton.Enabled = !paused && !searching && results.SelectedItems.Count == 1;
            status.Text = paused ? "后台查询已暂停。唤醒后可重新搜索。" : "输入地点后点击搜索。原设置未改变。";
        }
    }
}
