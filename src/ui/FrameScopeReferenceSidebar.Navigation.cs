using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed class FrameScopeNavigationEventArgs : EventArgs
{
    public FrameScopeNavigationEventArgs(string key)
    {
        Key = key ?? "";
    }

    public string Key { get; private set; }
}
