using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ATEC.PM.Client.Controls
{
    public enum KpiTrend
    {
        None,
        Up,
        Down,
        Flat
    }

    public enum KpiCardTheme
    {
        Light,
        Dark
    }

    /// <summary>
    /// Custom Control representing a highly customizable KPI / Metric Card.
    /// </summary>
    public class KpiCard : Control
    {
        static KpiCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(KpiCard), new FrameworkPropertyMetadata(typeof(KpiCard)));
        }

        #region Dependency Properties

        // --- Data Properties ---

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty BadgeTextProperty =
            DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty));

        public string BadgeText
        {
            get => (string)GetValue(BadgeTextProperty);
            set => SetValue(BadgeTextProperty, value);
        }

        public static readonly DependencyProperty TrendTextProperty =
            DependencyProperty.Register(nameof(TrendText), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty));

        public string TrendText
        {
            get => (string)GetValue(TrendTextProperty);
            set => SetValue(TrendTextProperty, value);
        }

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty));

        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public static readonly DependencyProperty TrendProperty =
            DependencyProperty.Register(nameof(Trend), typeof(KpiTrend), typeof(KpiCard), new PropertyMetadata(KpiTrend.None));

        public KpiTrend Trend
        {
            get => (KpiTrend)GetValue(TrendProperty);
            set => SetValue(TrendProperty, value);
        }

        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(nameof(Theme), typeof(KpiCardTheme), typeof(KpiCard), new PropertyMetadata(KpiCardTheme.Light));

        public KpiCardTheme Theme
        {
            get => (KpiCardTheme)GetValue(ThemeProperty);
            set => SetValue(ThemeProperty, value);
        }

        // --- Card Styling Properties ---

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(KpiCard), new PropertyMetadata(new CornerRadius(12)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        // --- Title Styling Properties ---

        public static readonly DependencyProperty TitleFontSizeProperty =
            DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(KpiCard), new PropertyMetadata(11.0));

        public double TitleFontSize
        {
            get => (double)GetValue(TitleFontSizeProperty);
            set => SetValue(TitleFontSizeProperty, value);
        }

        public static readonly DependencyProperty TitleFontFamilyProperty =
            DependencyProperty.Register(nameof(TitleFontFamily), typeof(FontFamily), typeof(KpiCard), new PropertyMetadata(null));

        public FontFamily TitleFontFamily
        {
            get => (FontFamily)GetValue(TitleFontFamilyProperty);
            set => SetValue(TitleFontFamilyProperty, value);
        }

        public static readonly DependencyProperty TitleFontWeightProperty =
            DependencyProperty.Register(nameof(TitleFontWeight), typeof(FontWeight), typeof(KpiCard), new PropertyMetadata(FontWeights.Normal));

        public FontWeight TitleFontWeight
        {
            get => (FontWeight)GetValue(TitleFontWeightProperty);
            set => SetValue(TitleFontWeightProperty, value);
        }

        public static readonly DependencyProperty TitleForegroundProperty =
            DependencyProperty.Register(nameof(TitleForeground), typeof(Brush), typeof(KpiCard), new PropertyMetadata(null));

        public Brush TitleForeground
        {
            get => (Brush)GetValue(TitleForegroundProperty);
            set => SetValue(TitleForegroundProperty, value);
        }

        // --- Value Styling Properties ---

        public static readonly DependencyProperty ValueFontSizeProperty =
            DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(KpiCard), new PropertyMetadata(28.0));

        public double ValueFontSize
        {
            get => (double)GetValue(ValueFontSizeProperty);
            set => SetValue(ValueFontSizeProperty, value);
        }

        public static readonly DependencyProperty ValueFontFamilyProperty =
            DependencyProperty.Register(nameof(ValueFontFamily), typeof(FontFamily), typeof(KpiCard), new PropertyMetadata(null));

        public FontFamily ValueFontFamily
        {
            get => (FontFamily)GetValue(ValueFontFamilyProperty);
            set => SetValue(ValueFontFamilyProperty, value);
        }

        public static readonly DependencyProperty ValueFontWeightProperty =
            DependencyProperty.Register(nameof(ValueFontWeight), typeof(FontWeight), typeof(KpiCard), new PropertyMetadata(FontWeights.Bold));

        public FontWeight ValueFontWeight
        {
            get => (FontWeight)GetValue(ValueFontWeightProperty);
            set => SetValue(ValueFontWeightProperty, value);
        }

        public static readonly DependencyProperty ValueForegroundProperty =
            DependencyProperty.Register(nameof(ValueForeground), typeof(Brush), typeof(KpiCard), new PropertyMetadata(null));

        public Brush ValueForeground
        {
            get => (Brush)GetValue(ValueForegroundProperty);
            set => SetValue(ValueForegroundProperty, value);
        }

        // --- Badge Styling Properties ---

        public static readonly DependencyProperty BadgeFontSizeProperty =
            DependencyProperty.Register(nameof(BadgeFontSize), typeof(double), typeof(KpiCard), new PropertyMetadata(11.0));

        public double BadgeFontSize
        {
            get => (double)GetValue(BadgeFontSizeProperty);
            set => SetValue(BadgeFontSizeProperty, value);
        }

        public static readonly DependencyProperty BadgeFontFamilyProperty =
            DependencyProperty.Register(nameof(BadgeFontFamily), typeof(FontFamily), typeof(KpiCard), new PropertyMetadata(null));

        public FontFamily BadgeFontFamily
        {
            get => (FontFamily)GetValue(BadgeFontFamilyProperty);
            set => SetValue(BadgeFontFamilyProperty, value);
        }

        public static readonly DependencyProperty BadgeFontWeightProperty =
            DependencyProperty.Register(nameof(BadgeFontWeight), typeof(FontWeight), typeof(KpiCard), new PropertyMetadata(FontWeights.SemiBold));

        public FontWeight BadgeFontWeight
        {
            get => (FontWeight)GetValue(BadgeFontWeightProperty);
            set => SetValue(BadgeFontWeightProperty, value);
        }

        public static readonly DependencyProperty BadgeForegroundProperty =
            DependencyProperty.Register(nameof(BadgeForeground), typeof(Brush), typeof(KpiCard), new PropertyMetadata(null));

        public Brush BadgeForeground
        {
            get => (Brush)GetValue(BadgeForegroundProperty);
            set => SetValue(BadgeForegroundProperty, value);
        }

        public static readonly DependencyProperty BadgeBackgroundProperty =
            DependencyProperty.Register(nameof(BadgeBackground), typeof(Brush), typeof(KpiCard), new PropertyMetadata(null));

        public Brush BadgeBackground
        {
            get => (Brush)GetValue(BadgeBackgroundProperty);
            set => SetValue(BadgeBackgroundProperty, value);
        }

        // --- TrendText Styling Properties ---

        public static readonly DependencyProperty TrendFontSizeProperty =
            DependencyProperty.Register(nameof(TrendFontSize), typeof(double), typeof(KpiCard), new PropertyMetadata(13.0));

        public double TrendFontSize
        {
            get => (double)GetValue(TrendFontSizeProperty);
            set => SetValue(TrendFontSizeProperty, value);
        }

        public static readonly DependencyProperty TrendFontFamilyProperty =
            DependencyProperty.Register(nameof(TrendFontFamily), typeof(FontFamily), typeof(KpiCard), new PropertyMetadata(null));

        public FontFamily TrendFontFamily
        {
            get => (FontFamily)GetValue(TrendFontFamilyProperty);
            set => SetValue(TrendFontFamilyProperty, value);
        }

        public static readonly DependencyProperty TrendFontWeightProperty =
            DependencyProperty.Register(nameof(TrendFontWeight), typeof(FontWeight), typeof(KpiCard), new PropertyMetadata(FontWeights.SemiBold));

        public FontWeight TrendFontWeight
        {
            get => (FontWeight)GetValue(TrendFontWeightProperty);
            set => SetValue(TrendFontWeightProperty, value);
        }

        public static readonly DependencyProperty TrendForegroundProperty =
            DependencyProperty.Register(nameof(TrendForeground), typeof(Brush), typeof(KpiCard), new PropertyMetadata(null));

        public Brush TrendForeground
        {
            get => (Brush)GetValue(TrendForegroundProperty);
            set => SetValue(TrendForegroundProperty, value);
        }

        // --- Subtitle Styling Properties ---

        public static readonly DependencyProperty SubtitleFontSizeProperty =
            DependencyProperty.Register(nameof(SubtitleFontSize), typeof(double), typeof(KpiCard), new PropertyMetadata(12.0));

        public double SubtitleFontSize
        {
            get => (double)GetValue(SubtitleFontSizeProperty);
            set => SetValue(SubtitleFontSizeProperty, value);
        }

        public static readonly DependencyProperty SubtitleFontFamilyProperty =
            DependencyProperty.Register(nameof(SubtitleFontFamily), typeof(FontFamily), typeof(KpiCard), new PropertyMetadata(null));

        public FontFamily SubtitleFontFamily
        {
            get => (FontFamily)GetValue(SubtitleFontFamilyProperty);
            set => SetValue(SubtitleFontFamilyProperty, value);
        }

        public static readonly DependencyProperty SubtitleFontWeightProperty =
            DependencyProperty.Register(nameof(SubtitleFontWeight), typeof(FontWeight), typeof(KpiCard), new PropertyMetadata(FontWeights.Normal));

        public FontWeight SubtitleFontWeight
        {
            get => (FontWeight)GetValue(SubtitleFontWeightProperty);
            set => SetValue(SubtitleFontWeightProperty, value);
        }

        public static readonly DependencyProperty SubtitleForegroundProperty =
            DependencyProperty.Register(nameof(SubtitleForeground), typeof(Brush), typeof(KpiCard), new PropertyMetadata(null));

        public Brush SubtitleForeground
        {
            get => (Brush)GetValue(SubtitleForegroundProperty);
            set => SetValue(SubtitleForegroundProperty, value);
        }

        #endregion
    }
}
