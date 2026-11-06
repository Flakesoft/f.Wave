// f.Wave build 3 - file 4 of 4: OverlayWindow.cs  (NEW FILE - add it as a plain class)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace f.Wave
{
    /// <summary>
    /// A see-through, click-through window that sits on top of everything and draws
    /// the spectrum around the edges of your screen. Bass is in the middle of each
    /// edge, treble in the corners. Everything underneath stays fully usable.
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        // =====================================================================
        //  KNOBS - change these numbers to tune the look.
        // =====================================================================

        private const float MaxDepth = 110f;   // how far bars grow inward at full loudness
        private const float Pitch = 22f;       // distance from one bar to the next
        private const float Gap = 4f;          // empty space between bars (bar width = Pitch - Gap)
        private const float BarOpacity = 0.9f; // 1 = solid, lower = more see-through

        private const bool ShowBottom = true;
        private const bool ShowTop = true;
        private const bool ShowLeft = true;
        private const bool ShowRight = true;

        // true  = stay inside the usable area (bottom bars sit ABOVE the taskbar)
        // false = use the whole monitor (bottom bars sit behind/over the taskbar)
        private const bool UseWorkArea = true;

        // =====================================================================
        //  Internals - no need to touch anything below this line.
        // =====================================================================

        private sealed class Bar
        {
            public SpriteVisual Visual;
            public int Edge;     // 0 bottom, 1 top, 2 left, 3 right
            public float Pos;    // where the bar starts along its edge
            public float Thick;  // bar width
            public float U;      // 0 = centre of edge (bass) ... 1 = corner (treble)
        }

        private readonly Grid _root = new Grid();
        private readonly Compositor _compositor;
        private readonly ContainerVisual _container;
        private readonly CompositionColorBrush[] _brushes;
        private readonly List<Bar> _bars = new List<Bar>();
        private float _w, _h;

        public OverlayWindow()
        {
            Title = "f.Wave overlay";
            Content = _root;

            // 1. Fully transparent background.
            SystemBackdrop = new TransparentBackdrop();

            // 2. No title bar, no border, always on top, not in Alt+Tab.
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            AppWindow.SetPresenter(presenter);
            try { AppWindow.IsShownInSwitchers = false; } catch { }

            // 3. Click-through + never steals focus.
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            MakeClickThrough(hwnd);
            RemoveWindowDecorations(hwnd);

            // 4. Cover the main screen.
            var area = DisplayArea.Primary;
            AppWindow.MoveAndResize(UseWorkArea ? area.WorkArea : area.OuterBounds);

            // 5. Drawing surface: composition visuals (fast, no layout work per frame).
            _compositor = ElementCompositionPreview.GetElementVisual(_root).Compositor;
            _container = _compositor.CreateContainerVisual();
            _container.Opacity = BarOpacity;
            ElementCompositionPreview.SetElementChildVisual(_root, _container);

            _brushes = new[]
            {
                _compositor.CreateColorBrush(ColorHelper.FromArgb(255, 0xFA, 0xA6, 0x02)), // orange
                _compositor.CreateColorBrush(ColorHelper.FromArgb(255, 0x41, 0xB6, 0x57)), // green
                _compositor.CreateColorBrush(ColorHelper.FromArgb(255, 0xF8, 0x38, 0x87)), // pink
                _compositor.CreateColorBrush(ColorHelper.FromArgb(255, 0x22, 0xB9, 0xF5)), // blue
            };

            _root.SizeChanged += (s, e) => Rebuild((float)e.NewSize.Width, (float)e.NewSize.Height);
        }

        // Called ~60 times per second by MainWindow with the current bar levels (0..1).
        public void Render(float[] levels)
        {
            if (levels == null || levels.Length == 0 || _bars.Count == 0)
                return;

            int n = levels.Length;

            foreach (var bar in _bars)
            {
                float f = bar.U * (n - 1);
                int i0 = (int)f;
                int i1 = Math.Min(i0 + 1, n - 1);
                float level = levels[i0] + (levels[i1] - levels[i0]) * (f - i0);

                Place(bar, level * MaxDepth);
            }
        }

        // Makes all bars disappear (used when listening stops).
        public void Clear()
        {
            foreach (var bar in _bars)
                Place(bar, 0f);
        }

        private void Place(Bar bar, float length)
        {
            if (length < 1f) length = 0f;

            switch (bar.Edge)
            {
                case 0: // bottom
                    bar.Visual.Size = new Vector2(bar.Thick, length);
                    bar.Visual.Offset = new Vector3(bar.Pos, _h - length, 0f);
                    break;
                case 1: // top
                    bar.Visual.Size = new Vector2(bar.Thick, length);
                    bar.Visual.Offset = new Vector3(bar.Pos, 0f, 0f);
                    break;
                case 2: // left
                    bar.Visual.Size = new Vector2(length, bar.Thick);
                    bar.Visual.Offset = new Vector3(0f, bar.Pos, 0f);
                    break;
                default: // right
                    bar.Visual.Size = new Vector2(length, bar.Thick);
                    bar.Visual.Offset = new Vector3(_w - length, bar.Pos, 0f);
                    break;
            }
        }

        // (Re)creates all bars whenever the window size changes.
        private void Rebuild(float width, float height)
        {
            _w = width;
            _h = height;

            _container.Children.RemoveAll();
            _bars.Clear();

            if (ShowBottom) AddEdge(0, width);
            if (ShowTop) AddEdge(1, width);
            if (ShowLeft) AddEdge(2, height);
            if (ShowRight) AddEdge(3, height);
        }

        private void AddEdge(int edge, float length)
        {
            int slots = (int)(length / Pitch);
            slots -= slots % 2; // even number, so the middle of the edge is a clean split
            if (slots < 2) return;

            float pitch = length / slots;
            float thick = Math.Max(1f, pitch - Gap);

            for (int s = 0; s < slots; s++)
            {
                var visual = _compositor.CreateSpriteVisual();
                visual.Brush = _brushes[s % _brushes.Length];
                visual.Size = Vector2.Zero;
                _container.Children.InsertAtTop(visual);

                _bars.Add(new Bar
                {
                    Visual = visual,
                    Edge = edge,
                    Thick = thick,
                    Pos = s * pitch + (pitch - thick) / 2f,
                    U = Math.Abs((s + 0.5f) - slots / 2f) / (slots / 2f),
                });
            }
        }

        // ---------------------------------------------------------------------
        //  Windows plumbing
        // ---------------------------------------------------------------------
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint LWA_ALPHA = 0x2;

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void MakeClickThrough(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        }

        // Windows 11 adds rounded corners and a thin border; we don't want either.
        private static void RemoveWindowDecorations(IntPtr hwnd)
        {
            try
            {
                int doNotRound = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

                int noBorder = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
            }
            catch { }
        }
    }

    /// <summary>A window backdrop that is completely see-through.</summary>
    internal sealed class TransparentBackdrop : SystemBackdrop
    {
        private DispatcherQueueController? _dispatcherQueueController;
        private Windows.UI.Composition.Compositor? _compositor;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);

            // The callback can run on a thread without a DispatcherQueue.
            // Initialize one before creating the Windows composition object
            // required by ICompositionSupportsSystemBackdrop.
            if (DispatcherQueue.GetForCurrentThread() == null)
                _dispatcherQueueController = DispatcherQueueController.CreateOnCurrentThread();

            _compositor ??= new Windows.UI.Composition.Compositor();
            connectedTarget.SystemBackdrop = _compositor.CreateColorBrush(
                Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            base.OnTargetDisconnected(disconnectedTarget);
            disconnectedTarget.SystemBackdrop = null;
        }
    }
}