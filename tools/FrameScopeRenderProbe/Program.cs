using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            var seconds = 12;
            if (args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed > 0) seconds = parsed;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new RenderForm(seconds);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "framescope-render-probe-error.txt");
            System.IO.File.WriteAllText(logPath, ex.ToString());
            MessageBox.Show(ex.ToString(), "FrameScope Render Probe failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class RenderForm : Form
{
    private readonly TimeSpan _duration;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _rtv;
    private readonly System.Windows.Forms.Timer _timer;
    private int _frames;

    public RenderForm(int seconds)
    {
        _duration = TimeSpan.FromSeconds(seconds);
        Text = "FrameScope D3D11 Render Probe";
        ClientSize = new System.Drawing.Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        _timer = new System.Windows.Forms.Timer { Interval = 1 };
        _timer.Tick += (_, _) =>
        {
            if (_clock.Elapsed >= _duration)
            {
                Close();
                return;
            }
            Render();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        InitializeD3D();
        _timer.Start();
    }

    private void InitializeD3D()
    {
        _factory = CreateDXGIFactory1<IDXGIFactory2>();
        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
            out _device,
            out _,
            out _context).CheckError();

        var desc = new SwapChainDescription1
        {
            Width = (uint)ClientSize.Width,
            Height = (uint)ClientSize.Height,
            Format = Format.R8G8B8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore
        };

        _swapChain = _factory.CreateSwapChainForHwnd(_device, Handle, desc);
        _factory.MakeWindowAssociation(Handle, WindowAssociationFlags.IgnoreAltEnter);
        RecreateRenderTarget();
    }

    private void RecreateRenderTarget()
    {
        _rtv?.Dispose();
        using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device!.CreateRenderTargetView(backBuffer);
    }

    private void Render()
    {
        if (_context == null || _swapChain == null || _rtv == null) return;
        var t = (float)_clock.Elapsed.TotalSeconds;
        var r = 0.08f + 0.45f * (float)(Math.Sin(t * 2.1) * 0.5 + 0.5);
        var g = 0.12f + 0.50f * (float)(Math.Sin(t * 1.7 + 2.0) * 0.5 + 0.5);
        var b = 0.16f + 0.55f * (float)(Math.Sin(t * 1.3 + 4.0) * 0.5 + 0.5);
        _context.OMSetRenderTargets(_rtv);
        _context.ClearRenderTargetView(_rtv, new Color4(r, g, b, 1.0f));
        _swapChain.Present(0, PresentFlags.None);
        _frames++;
        if ((_frames % 30) == 0)
        {
            Text = $"FrameScope D3D11 Render Probe - {_frames / Math.Max(0.001, _clock.Elapsed.TotalSeconds):0} FPS";
        }
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (_swapChain == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _rtv?.Dispose();
        _context?.ClearState();
        _swapChain.ResizeBuffers(0, (uint)ClientSize.Width, (uint)ClientSize.Height, Format.Unknown, SwapChainFlags.None).CheckError();
        RecreateRenderTarget();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _rtv?.Dispose();
            _swapChain?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
            _factory?.Dispose();
        }
        base.Dispose(disposing);
    }
}
