using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TsOverlay.Controls;

/// <summary>
/// TextBox с маленькими кнопками «вверх/вниз»: значение можно вводить руками
/// и менять стрелками (шаг Step), не теряя обычное поведение текстового поля.
/// </summary>
public class SpinBox : TextBox
{
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(SpinBox), new PropertyMetadata(1.0));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SpinBox), new PropertyMetadata(double.MinValue));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SpinBox), new PropertyMetadata(double.MaxValue));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(SpinBox), new PropertyMetadata(0));

    private RepeatButton? _up;
    private RepeatButton? _down;

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_up != null)
        {
            _up.Click -= OnUpClick;
        }

        if (_down != null)
        {
            _down.Click -= OnDownClick;
        }

        base.OnApplyTemplate();

        _up = GetTemplateChild("PART_Up") as RepeatButton;
        _down = GetTemplateChild("PART_Down") as RepeatButton;

        if (_up != null)
        {
            _up.Click += OnUpClick;
        }

        if (_down != null)
        {
            _down.Click += OnDownClick;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            Nudge(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            Nudge(-1);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => Nudge(1);

    private void OnDownClick(object sender, RoutedEventArgs e) => Nudge(-1);

    private void Nudge(int direction)
    {
        var text = (Text ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            value = Math.Clamp(0, Minimum, Maximum);
        }

        value = Math.Clamp(value + direction * Step, Minimum, Maximum);
        Text = value.ToString("F" + Math.Max(0, Decimals), CultureInfo.CurrentCulture);
        CaretIndex = Text.Length;
    }
}
