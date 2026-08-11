using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameDraw_App.ViewModels;

/// <summary>
/// State for the phase-one workspace shell. Drawing execution is intentionally
/// not started here; later phases will attach the planner and Windows executor.
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string WorkspaceTitle { get; set; } = "새 그림 작업";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "이미지를 선택하면 작업을 시작할 수 있습니다.";

    [ObservableProperty]
    public partial string SelectedImageName { get; set; } = "이미지가 선택되지 않았습니다.";

    [ObservableProperty]
    public partial string? SelectedImagePath { get; set; }

    [ObservableProperty]
    public partial string SelectedMode { get; set; } = "자동 추천";

    [ObservableProperty]
    public partial string ProfileName { get; set; } = "새 게임 프로필";

    [ObservableProperty]
    public partial WorkspaceStage Stage { get; set; } = WorkspaceStage.SelectImage;

    [ObservableProperty]
    public partial double Progress { get; set; }

    public bool HasImage => !string.IsNullOrWhiteSpace(SelectedImagePath);

    public string StageLabel => Stage switch
    {
        WorkspaceStage.SelectImage => "이미지 선택",
        WorkspaceStage.Configure => "설정 중",
        WorkspaceStage.Ready => "실행 준비",
        WorkspaceStage.Running => "그리는 중",
        WorkspaceStage.Paused => "일시 정지",
        WorkspaceStage.Completed => "완료",
        WorkspaceStage.Failed => "확인 필요",
        _ => "대기"
    };

    partial void OnSelectedImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnStageChanged(WorkspaceStage value)
    {
        OnPropertyChanged(nameof(StageLabel));
    }

    public void SetImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("이미지 경로는 비워 둘 수 없습니다.", nameof(path));
        }

        SelectedImagePath = path;
        SelectedImageName = Path.GetFileName(path);
        Stage = WorkspaceStage.Configure;
        StatusMessage = $"'{SelectedImageName}'을(를) 불러왔습니다. 캔버스와 그리기 모드를 설정하세요.";
        Progress = 0;
    }

    [RelayCommand]
    private void ClearImage()
    {
        SelectedImagePath = null;
        SelectedImageName = "이미지가 선택되지 않았습니다.";
        StatusMessage = "이미지를 선택하면 작업을 시작할 수 있습니다.";
        Stage = WorkspaceStage.SelectImage;
        Progress = 0;
    }
}
