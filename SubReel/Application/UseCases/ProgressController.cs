using System;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

public class ProgressController
{
    private readonly ProgressBar _bar;
    private readonly Dispatcher _dispatcher;

    private double _currentValue = 0;
    private bool _isActive = false;

    public ProgressController(ProgressBar bar)
    {
        _bar = bar;
        _dispatcher = bar.Dispatcher;
    }

    // старт новой операции
    public void Start()
    {
        _isActive = true;
        _currentValue = 0;

        _dispatcher.Invoke(() =>
        {
            _bar.Value = 0;
        });
    }

    // безопасное обновление (только вперед)
    public void Report(double value)
    {
        if (!_isActive) return;

        if (value < _currentValue)
            value = _currentValue;

        _currentValue = value;
        AnimateTo(value);
    }

    // завершение
    public void Finish()
    {
        Report(100);
        _isActive = false;
    }

    // сброс
    public void Reset()
    {
        _isActive = false;
        _currentValue = 0;

        _dispatcher.Invoke(() =>
        {
            _bar.Value = 0;
        });
    }

    private void AnimateTo(double value)
    {
        _dispatcher.Invoke(() =>
        {
            var anim = new DoubleAnimation
            {
                To = value,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase()
            };

            _bar.BeginAnimation(ProgressBar.ValueProperty, anim);
        });
    }
}
