using System.Drawing;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    internal static class PetTheme
    {
        internal static readonly Color Panel = Color.FromArgb(247, 249, 246, 253);
        internal static readonly Color Border = Color.FromArgb(225, 188, 177, 207);
        internal static readonly Color Shadow = Color.FromArgb(38, 71, 56, 94);
        internal static readonly Color Text = Color.FromArgb(67, 55, 85);
        internal static readonly Color Secondary = Color.FromArgb(111, 96, 130);
        internal static readonly Color Accent = Color.FromArgb(109, 77, 155);
        internal static readonly Color Badge = Color.FromArgb(232, 223, 244);
        internal static readonly Color Divider = Color.FromArgb(185, 197, 186, 213);
        internal static readonly Color Track = Color.FromArgb(221, 211, 233);
        internal static readonly Color Healthy = Color.FromArgb(50, 112, 102);
        internal static readonly Color Warning = Color.FromArgb(145, 92, 32);
        internal static readonly Color Danger = Color.FromArgb(166, 63, 84);
        internal static readonly Color Hint = Color.FromArgb(242, 246, 240, 252);
        internal static readonly Color Dialog = Color.FromArgb(249, 246, 253);

        internal static void StyleDialog(Control control)
        {
            control.ForeColor = control.ForeColor == Color.Firebrick ? Danger : Text;
            control.BackColor = control is TextBoxBase || control is ListView ? Color.White : Dialog;
            var button = control as Button;
            if (button != null) {
                button.UseVisualStyleBackColor = false; button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(Border.R, Border.G, Border.B);
                button.BackColor = Badge;
            }
            foreach (Control child in control.Controls) StyleDialog(child);
        }
        internal static void StyleMenu(ContextMenuStrip menu)
        { menu.ForeColor = Text; menu.Renderer = new ToolStripProfessionalRenderer(new PearlMenuColors()); }

        private sealed class PearlMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Dialog; } }
            public override Color ImageMarginGradientBegin { get { return Dialog; } }
            public override Color ImageMarginGradientMiddle { get { return Dialog; } }
            public override Color ImageMarginGradientEnd { get { return Dialog; } }
            public override Color MenuItemSelected { get { return Badge; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color MenuBorder { get { return Color.FromArgb(Border.R, Border.G, Border.B); } }
            public override Color SeparatorDark { get { return Track; } }
        }
    }
}
