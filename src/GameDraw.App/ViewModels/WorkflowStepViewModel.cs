using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GameDraw_App.ViewModels;

public sealed class WorkflowStepViewModel : ObservableObject
{
    public WorkflowStepViewModel(string number, string title, string description, string actionLabel, ICommand? actionCommand)
    {
        Number = number;
        Title = title;
        Description = description;
        ActionLabel = actionLabel;
        ActionCommand = actionCommand;
        UpdateState(completed: false, current: false, "대기");
    }

    public string Number { get; }

    public string Title { get; }

    public string Description { get; }

    public string ActionLabel { get; }

    public ICommand? ActionCommand { get; }

    public Visibility ActionVisibility => ActionCommand is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsCompleted { get; private set; }

    public bool IsCurrent { get; private set; }

    public string StateText { get; private set; } = "대기";

    public Brush StateBrush { get; private set; } = new SolidColorBrush(Color.FromArgb(255, 102, 112, 133));

    public Brush BadgeBackground { get; private set; } = new SolidColorBrush(Color.FromArgb(255, 239, 242, 246));

    public void UpdateState(bool completed, bool current, string stateText)
    {
        IsCompleted = completed;
        IsCurrent = current;
        StateText = stateText;
        StateBrush = completed
            ? new SolidColorBrush(Color.FromArgb(255, 41, 132, 90))
            : current
                ? new SolidColorBrush(Color.FromArgb(255, 53, 106, 230))
                : new SolidColorBrush(Color.FromArgb(255, 102, 112, 133));
        BadgeBackground = completed
            ? new SolidColorBrush(Color.FromArgb(255, 230, 247, 237))
            : current
                ? new SolidColorBrush(Color.FromArgb(255, 232, 238, 255))
                : new SolidColorBrush(Color.FromArgb(255, 239, 242, 246));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(BadgeBackground));
    }
}
