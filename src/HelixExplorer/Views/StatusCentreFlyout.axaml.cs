using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class StatusCentreFlyout : UserControl
{
    private const int OpenMs = 180;
    private DispatcherTimer? _hideTimer;
    private bool _isShown;
    private MainWindowViewModel? _subscribedVm;

    public StatusCentreFlyout()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => ApplyTransitions();

    private void ApplyTransitions()
    {
        Transitions ??= new Transitions();
        if (Transitions.Count == 0)
        {
            Transitions.Add(new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(OpenMs),
                Easing = new CubicEaseOut()
            });
        }

        if (RenderTransform is TranslateTransform translate)
        {
            translate.Transitions ??= new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.YProperty,
                    Duration = TimeSpan.FromMilliseconds(OpenMs),
                    Easing = new CubicEaseOut()
                }
            };
        }

        ActiveProgressBar.Transitions ??= new Transitions
        {
            new DoubleTransition
            {
                Property = RangeBase.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(120),
                Easing = new CubicEaseOut()
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = DataContext as MainWindowViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
            SyncOpenState(_subscribedVm.IsStatusCentreOpen, animate: false);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedVm = null;
        _hideTimer?.Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsStatusCentreOpen)
            && sender is MainWindowViewModel vm)
        {
            SyncOpenState(vm.IsStatusCentreOpen, animate: true);
        }
    }

    private void SyncOpenState(bool open, bool animate)
    {
        _hideTimer?.Stop();

        if (open)
        {
            _isShown = true;
            IsVisible = true;
            IsHitTestVisible = true;
            if (!animate)
            {
                Opacity = 1;
                if (RenderTransform is TranslateTransform instant)
                    instant.Y = 0;
            }
            else
            {
                Opacity = 1;
                if (RenderTransform is TranslateTransform slide)
                    slide.Y = 0;
            }

            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
            return;
        }

        IsHitTestVisible = false;
        if (!animate || !_isShown)
        {
            FinishHide();
            return;
        }

        Opacity = 0;
        if (RenderTransform is TranslateTransform closeSlide)
            closeSlide.Y = 12;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(OpenMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FinishHide();
        };
        _hideTimer.Start();
    }

    private void FinishHide()
    {
        _isShown = false;
        Opacity = 0;
        if (RenderTransform is TranslateTransform slide)
            slide.Y = 12;
        IsVisible = false;
        IsHitTestVisible = false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (DataContext is MainWindowViewModel vm)
            vm.CloseStatusCentreCommand.Execute(null);

        e.Handled = true;
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusCentre.SetHovered(true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusCentre.SetHovered(false);
    }
}
