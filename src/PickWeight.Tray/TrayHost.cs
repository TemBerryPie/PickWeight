using System.Drawing;
using System.Windows.Forms;

namespace PickWeight.Tray;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayHost(string iconPath, Action show, Action showMenu)
    {
        _icon = new NotifyIcon
        {
            Icon = new Icon(iconPath),
            Text = "PickWeight",
            Visible = true
        };
        _icon.DoubleClick += (_, _) => show();
        _icon.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                showMenu();
            }
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
