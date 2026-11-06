// f.Wave build 3 - file 2 of 4: MainWindow.xaml.cs
using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace f.Wave
{
    public sealed partial class MainWindow : Window
    {
        private const int BarCount = 28;
        private const double MinBarHeight = 3;

        private readonly Border[] _bars = new Border[BarCount];
        private readonly AudioAnalyzer _analyzer = new AudioAnalyzer(BarCount);
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private OverlayWindow _overlay;
        private bool _listening;

        public MainWindow()
        {
            InitializeComponent();

            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1100, Height = 760 });
            SetupTitleBar();
            BuildBars();

            _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60 fps
            _timer.Tick += OnFrame;

            Closed += (s, e) =>
            {
                _timer.Stop();
                _analyzer.Dispose();

                if (_overlay != null)
                {
                    _overlay.Closed -= OnOverlayClosed;
                    _overlay.Close();
                    _overlay = null;
                }
            };
        }

        // Black title bar that blends into the app, white caption buttons.
        private void SetupTitleBar()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
                return;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDrag);

            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 0x5C, 0x5C, 0x5C);
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 0x26, 0x26, 0x26);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 0x40, 0x40, 0x40);
            titleBar.ButtonPressedForegroundColor = Colors.White;
        }

        // Creates the 28 coloured bars (orange, green, pink, blue, repeat).
        private void BuildBars()
        {
            Windows.UI.Color[] colors =
            {
                ColorHelper.FromArgb(255, 0xFA, 0xA6, 0x02), // orange
                ColorHelper.FromArgb(255, 0x41, 0xB6, 0x57), // green
                ColorHelper.FromArgb(255, 0xF8, 0x38, 0x87), // pink
                ColorHelper.FromArgb(255, 0x22, 0xB9, 0xF5), // blue
            };

            for (int i = 0; i < BarCount; i++)
            {
                BarsHost.ColumnDefinitions.Add(new ColumnDefinition());

                var bar = new Border
                {
                    Height = MinBarHeight,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(colors[i % colors.Length]),
                };

                Grid.SetColumn(bar, i);
                BarsHost.Children.Add(bar);
                _bars[i] = bar;
            }
        }

        // ---------------------------------------------------------------------
        //  Buttons
        // ---------------------------------------------------------------------
        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_listening) StopListening();
            else StartListening();
        }

        // The pink tile: turns the screen-edge overlay on and off.
        private void OverlayTile_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null)
            {
                _overlay.Close(); // OnOverlayClosed resets the tile
                return;
            }

            try
            {
                _overlay = new OverlayWindow();
                _overlay.Closed += OnOverlayClosed;
                _overlay.Activate();
                OverlayValue.Text = "on";

                if (!_listening)
                    StartListening();
            }
            catch (Exception ex)
            {
                _overlay = null;
                OverlayValue.Text = "off";
                StatusText.Text = "overlay failed  ·  " + ex.Message.ToLowerInvariant();
            }
        }

        private void OnOverlayClosed(object sender, WindowEventArgs args)
        {
            _overlay = null;
            OverlayValue.Text = "off";
        }

        // ---------------------------------------------------------------------
        //  Start / stop
        // ---------------------------------------------------------------------
        private void StartListening()
        {
            try
            {
                _analyzer.Start();
                _timer.Start();
                _listening = true;

                ListenIcon.Glyph = "\uE71A";           // stop icon
                ListenLabel.Text = "stop listening";
                StatusText.Text = "listening  ·  capturing system audio";
            }
            catch (Exception ex)
            {
                _analyzer.Stop();
                StatusText.Text = "could not start  ·  " + ex.Message.ToLowerInvariant();
            }
        }

        private void StopListening(string status = "stopped  ·  not capturing")
        {
            _timer.Stop();
            _analyzer.Stop();
            _listening = false;

            foreach (var bar in _bars)
                bar.Height = MinBarHeight;

            _overlay?.Clear();

            ListenIcon.Glyph = "\uE768";               // play icon
            ListenLabel.Text = "start listening";
            StatusText.Text = status;
        }

        // Runs ~60 times per second while listening.
        private void OnFrame(object sender, object e)
        {
            if (_analyzer.LastError != null)
            {
                StopListening("stopped  ·  " + _analyzer.LastError.ToLowerInvariant());
                return;
            }

            float[] levels = _analyzer.GetLevels();
            double height = BarsHost.ActualHeight;

            for (int i = 0; i < BarCount; i++)
                _bars[i].Height = Math.Max(MinBarHeight, levels[i] * height);

            _overlay?.Render(levels);
        }
    }
}