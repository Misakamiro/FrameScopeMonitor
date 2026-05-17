using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public static class FrameScopeMotion
{
    public static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    public static float EaseOutCubic(float value)
    {
        value = Clamp01(value);
        float inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }

    public static float EaseInOutCubic(float value)
    {
        value = Clamp01(value);
        if (value < 0.5f) return 4f * value * value * value;
        float f = -2f * value + 2f;
        return 1f - (f * f * f) / 2f;
    }

    public static float LerpFloat(float start, float end, float amount)
    {
        amount = Clamp01(amount);
        return start + (end - start) * amount;
    }

    public static Color LerpColor(Color start, Color end, float amount)
    {
        amount = Clamp01(amount);
        return Color.FromArgb(
            LerpByte(start.A, end.A, amount),
            LerpByte(start.R, end.R, amount),
            LerpByte(start.G, end.G, amount),
            LerpByte(start.B, end.B, amount));
    }

    public static IDisposable Animate(Control owner, int durationMs, Action<float> render)
    {
        return Animate(owner, durationMs, render, null);
    }

    public static IDisposable Animate(Control owner, int durationMs, Action<float> render, Action completed)
    {
        if (owner == null || render == null)
        {
            return FrameScopeNoopMotion.Instance;
        }

        durationMs = Math.Max(1, durationMs);
        var handle = new FrameScopeMotionHandle(owner, durationMs, render, completed);
        handle.Start();
        return handle;
    }

    private static int LerpByte(byte start, byte end, float amount)
    {
        return (int)Math.Round(LerpFloat(start, end, amount));
    }

    private sealed class FrameScopeNoopMotion : IDisposable
    {
        public static readonly FrameScopeNoopMotion Instance = new FrameScopeNoopMotion();

        public void Dispose()
        {
        }
    }

    private sealed class FrameScopeMotionHandle : IDisposable
    {
        private readonly Control owner;
        private readonly int durationMs;
        private readonly Action<float> render;
        private readonly Action completed;
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly Timer timer;
        private bool disposed;

        public FrameScopeMotionHandle(Control owner, int durationMs, Action<float> render, Action completed)
        {
            this.owner = owner;
            this.durationMs = durationMs;
            this.render = render;
            this.completed = completed;
            timer = new Timer { Interval = 15 };
            timer.Tick += Tick;
        }

        public void Start()
        {
            stopwatch.Start();
            render(0f);
            timer.Start();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            timer.Tick -= Tick;
            timer.Dispose();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (disposed) return;
            if (owner.IsDisposed || !owner.IsHandleCreated)
            {
                Dispose();
                return;
            }

            float raw = stopwatch.ElapsedMilliseconds / (float)durationMs;
            float amount = EaseOutCubic(raw);
            render(amount);
            if (raw >= 1f)
            {
                render(1f);
                Dispose();
                if (completed != null) completed();
            }
        }
    }
}
