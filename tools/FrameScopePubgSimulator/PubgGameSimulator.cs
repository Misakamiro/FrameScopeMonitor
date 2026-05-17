using System;
using System.Drawing;
using System.Windows.Forms;

internal static class PubgGameSimulator
{
    [STAThread]
    private static int Main(string[] args)
    {
        int durationSeconds = FrameScopePubgSimulationCommon.ParseInt(
            FrameScopePubgSimulationCommon.GetArgValue(args, "--duration", "8"),
            8);
        string title = FrameScopePubgSimulationCommon.GetArgValue(args, "--title", FrameScopePubgSimulationCommon.WindowTitle);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (Form form = new SimulatorForm())
        {
            form.Text = title;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-2000, -2000);
            form.Size = new Size(640, 360);
            form.ShowInTaskbar = true;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;

            Timer titleTimer = new Timer { Interval = 1200 };
            int tick = 0;
            titleTimer.Tick += delegate
            {
                tick++;
                form.Text = tick % 2 == 0
                    ? title
                    : "PUBG: BATTLEGROUNDS - Training Ground";
            };
            titleTimer.Start();

            Timer exitTimer = new Timer { Interval = Math.Max(1, durationSeconds) * 1000 };
            exitTimer.Tick += delegate
            {
                exitTimer.Stop();
                form.Close();
            };
            exitTimer.Start();

            Application.Run(form);
        }

        return 0;
    }

    private sealed class SimulatorForm : Form
    {
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }
}
