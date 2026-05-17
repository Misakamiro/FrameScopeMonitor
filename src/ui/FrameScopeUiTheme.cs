using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static partial class FrameScopeNativeMonitor
{
    private static readonly Color UiApp = Color.FromArgb(11, 17, 24);
    private static readonly Color UiPanel = Color.FromArgb(16, 25, 35);
    private static readonly Color UiRaised = Color.FromArgb(21, 33, 48);
    private static readonly Color UiBorder = Color.FromArgb(32, 48, 68);
    private static readonly Color UiText = Color.FromArgb(238, 246, 255);
    private static readonly Color UiSubText = Color.FromArgb(185, 200, 216);
    private static readonly Color UiMuted = Color.FromArgb(127, 147, 168);
    private static readonly Color UiBlue = Color.FromArgb(59, 167, 255);
    private static readonly Color UiCyan = Color.FromArgb(41, 230, 255);
    private static readonly Color UiGreen = Color.FromArgb(125, 250, 114);
    private static readonly Color UiAmber = Color.FromArgb(255, 211, 91);
    private static readonly Color UiRed = Color.FromArgb(255, 93, 125);

    private const int UiRadiusCard = 16;
    private const int UiRadiusControl = 10;
    private const int UiRadiusPill = 999;

    private const int UiSpacePage = 18;
    private const int UiSpaceCard = 18;
    private const int UiSpaceSection = 12;
    private const int UiControlHeight = 36;
    private const int UiCompactControlHeight = 34;
}
