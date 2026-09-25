using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Views;

/// <summary>Pantalla de carga: pasos del arranque y barra de progreso animada.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow(SplashViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnProgress;
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    private void OnProgress(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SplashViewModel.Progress) && sender is SplashViewModel vm)
        {
            Bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(vm.Progress, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    /// <summary>Se desvanece y se cierra.</summary>
    public Task FadeOutAsync()
    {
        var done = new TaskCompletionSource();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            Close();
            done.TrySetResult();
        };
        BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }
}
