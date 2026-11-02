using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    public sealed class PetNameDialog : Form
    {
        private readonly TextBox input = new TextBox();
        private readonly Label validation = new Label();
        public string SelectedName { get; private set; }

        public PetNameDialog(string current)
        {
            Text = "修改宠物名称";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(344, 158);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Controls.Add(new Label { Text = "输入 1–20 个字符的名称", AutoSize = true, Location = new Point(18, 16) });
            input.SetBounds(18, 43, 308, 26);
            input.MaxLength = 100;
            input.Text = current;
            Controls.Add(input);
            validation.SetBounds(18, 76, 308, 22);
            validation.ForeColor = Color.Firebrick;
            Controls.Add(validation);
            Button save = new Button { Text = "保存", Location = new Point(165, 112), Size = new Size(77, 29) };
            Button cancel = new Button { Text = "取消", Location = new Point(249, 112), Size = new Size(77, 29), DialogResult = DialogResult.Cancel };
            save.Click += delegate
            {
                string normalized;
                if (!TryNormalize(input.Text, out normalized)) { validation.Text = "名称须为 1–20 个字符。"; input.Focus(); return; }
                SelectedName = normalized;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = save; CancelButton = cancel;
            Shown += delegate { input.Focus(); input.SelectAll(); };
        }

        public static bool TryNormalize(string name, out string normalized)
        {
            StringBuilder clean = new StringBuilder();
            foreach (char character in name ?? "")
                if (!Char.IsControl(character) && character != '\u2028' && character != '\u2029') clean.Append(character);
            normalized = clean.ToString().Trim();
            int length = new StringInfo(normalized).LengthInTextElements;
            return length >= 1 && length <= 20;
        }
    }
}
