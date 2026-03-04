// VillFlow.App/Views/OverlayWindow.xaml.cs
// Floating pill overlay — shows Listening/Processing/Typing states with color transitions.
// NEVER takes focus from the user's active window.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VillFlow.Core.Orchestration;

namespace VillFlow.App.Views;

public partial class OverlayWindow : Window
{
    // ── Win32 for non-activating window ──────────────────────────────────
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    // ── State colors ────────────────────────────────────────────────────────
    private static readonly Color CyanColor = (Color)ColorConverter.ConvertFromString("#00D4FF");
    private static readonly Color AmberColor = (Color)ColorConverter.ConvertFromString("#FFB800");
    private static readonly Color GreenColor = (Color)ColorConverter.ConvertFromString("#00FF88");
    private static readonly Color SurfaceColor = (Color)ColorConverter.ConvertFromString("#1A1A2E");

    private DoubleAnimation? _pulseAnimation;

    public OverlayWindow()
    {
        InitializeComponent();
        PositionBottomCenter();

        // Prepare pulse animation for the status dot
        _pulseAnimation = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Make this window NEVER steal focus from the active app
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>Positions the window at the bottom-center of the primary screen.</summary>
    private void PositionBottomCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = screen.Height - Height - 60; // 60px from bottom
    }

    /// <summary>
    /// Updates the overlay to reflect the current pipeline state.
    /// Must be called on the UI (Dispatcher) thread.
    /// </summary>
    public void SetState(PipelineOrchestrator.PipelineState state)
    {
        switch (state)
        {
            case PipelineOrchestrator.PipelineState.Listening:
                ShowWithState("Listening...", CyanColor, pulse: true);
                break;

            case PipelineOrchestrator.PipelineState.Processing:
                ShowWithState("Processing...", AmberColor, pulse: false);
                break;

            case PipelineOrchestrator.PipelineState.Typing:
                ShowWithState("Typing...", GreenColor, pulse: false);
                break;

            case PipelineOrchestrator.PipelineState.Idle:
            default:
                HideOverlay();
                break;
        }
    }

    private void ShowWithState(string text, Color color, bool pulse)
    {
        StatusText.Text = text;

        // Animate dot color
        var dotAnim = new ColorAnimation(color, TimeSpan.FromMilliseconds(200));
        DotBrush.BeginAnimation(SolidColorBrush.ColorProperty, dotAnim);

        // Animate pill background (subtle tint)
        var bgColor = Color.FromArgb(230, SurfaceColor.R, SurfaceColor.G, SurfaceColor.B);
        PillBackground.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(bgColor, TimeSpan.FromMilliseconds(200)));

        // Pulse animation on dot
        if (pulse && _pulseAnimation != null)
        {
            StatusDot.BeginAnimation(OpacityProperty, _pulseAnimation);
        }
        else
        {
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1.0;
        }

        // Fade in (non-activating — just make visible)
        Show();
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
        PillBorder.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void HideOverlay()
    {
        // Stop pulsing
        StatusDot.BeginAnimation(OpacityProperty, null);

        // Fade out
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
        fadeOut.Completed += (_, _) => Hide();
        PillBorder.BeginAnimation(OpacityProperty, fadeOut);
    }
}

