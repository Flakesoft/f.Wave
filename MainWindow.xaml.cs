// f.Wave build 3 - file 2 of 4: MainWindow.xaml.cs
using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace f.Wave
{
    public sealed partial class MainWindow : Window
    {
        private const int BarCount = 28;
        private const double MinBarHeight = 3;

        private readonly Border[] _bars = new Border[BarCount];
        private readonly Border[] _previewBars = new Border[8];
        private readonly float[] _displayLevels = new float[BarCount];
        private readonly AudioAnalyzer _analyzer = new AudioAnalyzer(BarCount);
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private OverlayWindow _overlay;
        private bool _barsBuilt;
        private bool _titleBarReady;
        private bool _listening;
        private bool _smoothMovement = true;

        public MainWindow()
        {
            InitializeComponent();

            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1100, Height = 760 });
            SetupTitleBar();
            ThemeSelector_SelectionChanged(ThemeSelector, null);
            BuildBars();
            BuildPreviewBars();
            ThemeSelector_SelectionChanged(ThemeSelector, null);
            DensitySelector_SelectionChanged(DensitySelector, null);
            if (OpacitySlider != null && PreviewHost != null)
                PreviewHost.Opacity = OpacitySlider.Value / 100.0;
            AnimationToggle_Toggled(AnimationToggle, null);

            // Attach handlers after XAML initialization and bar creation. Some WinUI
            // controls raise their initial value/selection events during InitializeComponent.
            PaletteSelector.SelectionChanged += PaletteSelector_SelectionChanged;
            SpacingSlider.ValueChanged += SpacingSlider_ValueChanged;
            PaletteSelector_SelectionChanged(PaletteSelector, null);

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

        // ---------------------------------------------------------------------
        //  Appearance
        // ---------------------------------------------------------------------
        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs? e)
        {
            if (RootShell == null || ThemeSelector == null || ThemeSelector.SelectedIndex < 0)
                return;

            bool light = ThemeSelector.SelectedIndex == 1;
            RootShell.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
            RootShell.Background = new SolidColorBrush(
                light ? ColorHelper.FromArgb(255, 245, 245, 245) : Colors.Black);

            if (MainPivot != null)
            {
                var selectedHeader = MainPivot.Resources["PivotHeaderForegroundSelectedBrush"] as SolidColorBrush;
                if (selectedHeader != null)
                    selectedHeader.Color = light ? Colors.Black : Colors.White;
            }

            var dimBrush = RootShell.Resources["DimBrush"] as SolidColorBrush;
            if (dimBrush != null)
                dimBrush.Color = light
                    ? ColorHelper.FromArgb(255, 100, 100, 100)
                    : ColorHelper.FromArgb(255, 138, 138, 138);

            if (PlayButton != null)
                PlayButton.Foreground = new SolidColorBrush(light ? Colors.Black : Colors.White);

            var appearanceForeground = new SolidColorBrush(light ? Colors.Black : Colors.White);
            if (StatusToggle != null)
                StatusToggle.Foreground = appearanceForeground;
            if (AnimationToggle != null)
                AnimationToggle.Foreground = appearanceForeground;

            if (ThemeOptionButton != null)
            {
                ThemeOptionButton.Foreground = appearanceForeground;
                ThemeOptionButton.Content = light ? "light" : "dark";
            }
            if (DensityOptionButton != null)
                DensityOptionButton.Foreground = appearanceForeground;
            if (PaletteOptionButton != null)
                PaletteOptionButton.Foreground = appearanceForeground;
            if (StatusOptionButton != null)
                StatusOptionButton.Foreground = appearanceForeground;
            if (AnimationOptionButton != null)
                AnimationOptionButton.Foreground = appearanceForeground;

            ApplyTitleBarTheme(light);
        }

        private void DensitySelector_SelectionChanged(object sender, SelectionChangedEventArgs? e)
        {
            if (BarsHost == null || DensitySelector == null || DensitySelector.SelectedIndex < 0)
                return;

            BarsHost.MinHeight = DensitySelector.SelectedIndex == 1 ? 120 : 160;
            BarsHost.MaxHeight = DensitySelector.SelectedIndex == 1 ? 210 : 260;

            if (DensityOptionButton != null)
                DensityOptionButton.Content = DensitySelector.SelectedIndex == 1 ? "compact" : "comfortable";

            if (PreviewHost != null)
                PreviewHost.Height = DensitySelector.SelectedIndex == 1 ? 68 : 86;
        }

        private void StatusToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (StatusText != null && StatusToggle != null)
                StatusText.Visibility = StatusToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

            if (StatusOptionButton != null && StatusToggle != null)
                StatusOptionButton.Content = StatusToggle.IsOn ? "show status details · on" : "show status details · off";
        }

        private void PaletteSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // InitializeComponent can raise SelectionChanged before BuildBars runs.
            if (!_barsBuilt || PaletteSelector == null || PaletteSelector.SelectedIndex < 0)
                return;

            Windows.UI.Color[] colors;
            switch (PaletteSelector.SelectedIndex)
            {
                case 1: // ocean
                    colors = new[]
                    {
                        ColorHelper.FromArgb(255, 0x22, 0xB9, 0xF5),
                        ColorHelper.FromArgb(255, 0x41, 0xB6, 0x57),
                        ColorHelper.FromArgb(255, 0x4A, 0x7B, 0xFF),
                        ColorHelper.FromArgb(255, 0x8B, 0x5C, 0xFF),
                    };
                    break;
                case 2: // mono
                    colors = new[]
                    {
                        ColorHelper.FromArgb(255, 0xF5, 0xF5, 0xF5),
                        ColorHelper.FromArgb(255, 0xC0, 0xC0, 0xC0),
                        ColorHelper.FromArgb(255, 0x88, 0x88, 0x88),
                        ColorHelper.FromArgb(255, 0x55, 0x55, 0x55),
                    };
                    break;
                default: // sunset
                    colors = new[]
                    {
                        ColorHelper.FromArgb(255, 0xFA, 0xA6, 0x02),
                        ColorHelper.FromArgb(255, 0x41, 0xB6, 0x57),
                        ColorHelper.FromArgb(255, 0xF8, 0x38, 0x87),
                        ColorHelper.FromArgb(255, 0x22, 0xB9, 0xF5),
                    };
                    break;
            }

            for (int i = 0; i < _bars.Length; i++)
                _bars[i].Background = new SolidColorBrush(colors[i % colors.Length]);

            for (int i = 0; i < _previewBars.Length; i++)
                _previewBars[i].Background = new SolidColorBrush(colors[i % colors.Length]);

            if (PaletteOptionButton != null)
                PaletteOptionButton.Content = PaletteSelector.SelectedIndex == 1 ? "ocean" :
                    PaletteSelector.SelectedIndex == 2 ? "mono" : "sunset";
        }

        private void SpacingSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // The slider can raise ValueChanged while InitializeComponent is still running.
            if (!_barsBuilt || SpacingValue == null)
                return;

            int spacing = (int)e.NewValue;
            SpacingValue.Text = spacing + " px";
            foreach (var bar in _bars)
                bar.Margin = new Thickness(0, 0, spacing, 0);

            foreach (var bar in _previewBars)
                bar.Margin = new Thickness(0, 0, spacing, 0);
        }

        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (OpacityValue == null || OpacitySlider == null || BarsHost == null)
                return;

            int opacity = (int)e.NewValue;
            OpacityValue.Text = opacity + "%";
            BarsHost.Opacity = opacity / 100.0;
            if (PreviewHost != null)
                PreviewHost.Opacity = opacity / 100.0;

        }

        private void ThemeOptionButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeSelector.SelectedIndex = ThemeSelector.SelectedIndex == 1 ? 0 : 1;
        }

        private void DensityOptionButton_Click(object sender, RoutedEventArgs e)
        {
            DensitySelector.SelectedIndex = DensitySelector.SelectedIndex == 1 ? 0 : 1;
        }

        private void PaletteOptionButton_Click(object sender, RoutedEventArgs e)
        {
            PaletteSelector.SelectedIndex = (PaletteSelector.SelectedIndex + 1) % 3;
        }

        private void StatusOptionButton_Click(object sender, RoutedEventArgs e)
        {
            StatusToggle.IsOn = !StatusToggle.IsOn;
        }

        private void AnimationOptionButton_Click(object sender, RoutedEventArgs e)
        {
            AnimationToggle.IsOn = !AnimationToggle.IsOn;
        }

        private void SpacingDownButton_Click(object sender, RoutedEventArgs e)
        {
            SpacingSlider.Value = Math.Max(SpacingSlider.Minimum, SpacingSlider.Value - 1);
        }

        private void SpacingUpButton_Click(object sender, RoutedEventArgs e)
        {
            SpacingSlider.Value = Math.Min(SpacingSlider.Maximum, SpacingSlider.Value + 1);
        }

        private void OpacityDownButton_Click(object sender, RoutedEventArgs e)
        {
            OpacitySlider.Value = Math.Max(OpacitySlider.Minimum, OpacitySlider.Value - OpacitySlider.StepFrequency);
        }

        private void OpacityUpButton_Click(object sender, RoutedEventArgs e)
        {
            OpacitySlider.Value = Math.Min(OpacitySlider.Maximum, OpacitySlider.Value + OpacitySlider.StepFrequency);
        }

        private void AnimationToggle_Toggled(object sender, RoutedEventArgs? e)
        {
            if (AnimationToggle != null)
                _smoothMovement = AnimationToggle.IsOn;

            if (PreviewModeText != null && AnimationToggle != null)
                PreviewModeText.Text = AnimationToggle.IsOn ? "smooth movement · on" : "smooth movement · off";
            if (AnimationOptionButton != null && AnimationToggle != null)
                AnimationOptionButton.Content = AnimationToggle.IsOn ? "smooth movement · on" : "smooth movement · off";
        }

        private void BuildPreviewBars()
        {
            if (PreviewHost == null)
                return;

            PreviewHost.ColumnDefinitions.Clear();
            PreviewHost.Children.Clear();

            Windows.UI.Color[] colors =
            {
                ColorHelper.FromArgb(255, 0xFA, 0xA6, 0x02),
                ColorHelper.FromArgb(255, 0x41, 0xB6, 0x57),
                ColorHelper.FromArgb(255, 0xF8, 0x38, 0x87),
                ColorHelper.FromArgb(255, 0x22, 0xB9, 0xF5),
            };

            for (int i = 0; i < _previewBars.Length; i++)
            {
                PreviewHost.ColumnDefinitions.Add(new ColumnDefinition());
                var bar = new Border
                {
                    Height = 18 + ((i * 13) % 42),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(colors[i % colors.Length]),
                };

                Grid.SetColumn(bar, i);
                PreviewHost.Children.Add(bar);
                _previewBars[i] = bar;
            }
        }

        // Flat title bar that follows the selected Metro theme.
        private void SetupTitleBar()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
                return;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDrag);
            _titleBarReady = true;

            ApplyTitleBarTheme(false);
        }

        private void ApplyTitleBarTheme(bool light)
        {
            if (!_titleBarReady || !AppWindowTitleBar.IsCustomizationSupported())
                return;

            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = light ? Colors.Black : Colors.White;
            titleBar.ButtonInactiveForegroundColor = light
                ? ColorHelper.FromArgb(255, 0x70, 0x70, 0x70)
                : ColorHelper.FromArgb(255, 0x5C, 0x5C, 0x5C);
            titleBar.ButtonHoverBackgroundColor = light
                ? ColorHelper.FromArgb(255, 0xDD, 0xDD, 0xDD)
                : ColorHelper.FromArgb(255, 0x26, 0x26, 0x26);
            titleBar.ButtonHoverForegroundColor = light ? Colors.Black : Colors.White;
            titleBar.ButtonPressedBackgroundColor = light
                ? ColorHelper.FromArgb(255, 0xC8, 0xC8, 0xC8)
                : ColorHelper.FromArgb(255, 0x40, 0x40, 0x40);
            titleBar.ButtonPressedForegroundColor = light ? Colors.Black : Colors.White;
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

            _barsBuilt = true;
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

            Array.Clear(_displayLevels, 0, _displayLevels.Length);

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
            {
                if (_smoothMovement)
                    _displayLevels[i] += (levels[i] - _displayLevels[i]) * 0.28f;
                else
                    _displayLevels[i] = levels[i];

                _bars[i].Height = Math.Max(MinBarHeight, _displayLevels[i] * height);
            }

            _overlay?.Render(_displayLevels);
        }
    }
}