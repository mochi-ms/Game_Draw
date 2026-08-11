using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameDraw.Core.Execution;
using GameDraw.Core.Presentation;

namespace GameDraw_App.ViewModels;

/// <summary>
/// Presentation state for the responsive workspace shell. Native drawing
/// execution remains owned by the planner/executor layers; this ViewModel
/// exposes safe UI state transitions and progress hooks for them.
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
    public partial string SelectedMode { get; set; } = "가로 스캔라인";

    [ObservableProperty]
    public partial string SelectedRenderStyle { get; set; } = "자동 채색";

    [ObservableProperty]
    public partial string SelectedSpeed { get; set; } = "빠르게";

    [ObservableProperty]
    public partial string ProfileName { get; set; } = "새 게임 프로필";

    [ObservableProperty]
    public partial WorkspaceStage Stage { get; set; } = WorkspaceStage.SelectImage;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial ResponsiveLayoutMode LayoutMode { get; set; } = ResponsiveLayoutMode.Expanded;

    [ObservableProperty]
    public partial AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string BusyMessage { get; set; } = "준비 중…";

    [ObservableProperty]
    public partial bool IsExecutionPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsReducedMotion { get; set; }

    [ObservableProperty]
    public partial bool IsFloating { get; set; }

    [ObservableProperty]
    public partial bool IsProfileCalibrated { get; set; }

    [ObservableProperty]
    public partial bool IsCalibrating { get; set; }

    [ObservableProperty]
    public partial string CalibrationMessage { get; set; } = "Roblox Podiums 창을 찾은 뒤 F6으로 좌표를 기록합니다.";

    [ObservableProperty]
    public partial string PlanSummary { get; set; } = "이미지를 분석하면 해상도, 색상 수, 예상 시간이 표시됩니다.";

    [ObservableProperty]
    public partial double MaximumColors { get; set; } = 128d;

    [ObservableProperty]
    public partial double LogicalWidth { get; set; } = 512d;

    [ObservableProperty]
    public partial double LogicalHeight { get; set; } = 512d;

    public bool HasImage => !string.IsNullOrWhiteSpace(SelectedImagePath);

    public bool CanPrepare => HasImage && !IsBusy && !IsCalibrating &&
        Stage is not WorkspaceStage.Running and not WorkspaceStage.Paused;

    public bool CanClearImage => HasImage && !IsBusy &&
        Stage is not WorkspaceStage.Running and not WorkspaceStage.Paused;

    public bool CanChangeImage => !IsBusy &&
        Stage is not WorkspaceStage.Running and not WorkspaceStage.Paused;

    public bool CanStart => HasImage && IsProfileCalibrated && !IsBusy && !IsCalibrating &&
        Stage is WorkspaceStage.Ready or WorkspaceStage.Completed;

    public bool CanPause => Stage is WorkspaceStage.Running or WorkspaceStage.Paused;

    public bool CanStop => CanPause;

    public bool IsPaused => Stage == WorkspaceStage.Paused;

    public string ModeDescription => SelectedMode switch
    {
        "픽셀 점찍기" => "픽셀마다 점을 찍습니다. 가장 정확하지만 실행 시간이 가장 깁니다.",
        "가로 스캔라인" => "같은 색의 가로 구간을 선으로 묶습니다. 대부분의 이미지에 빠르고 정확합니다.",
        "세로 스캔라인" => "세로 방향이 긴 그림을 선으로 묶어 그립니다.",
        "윤곽선" => "색 영역의 테두리만 그립니다. 선화와 로고에 적합합니다.",
        "면 채우기" => "같은 색 영역을 줄 단위로 채웁니다.",
        "하이브리드" => "윤곽선과 면 채우기를 함께 사용합니다.",
        _ => "이미지를 비교해 모드를 고릅니다. 큰 사진은 분석 시간이 더 걸릴 수 있습니다."
    };

    public string RenderStyleDescription => SelectedRenderStyle == "검정 선화"
        ? "색을 채우지 않고 원본의 경계만 검은 선으로 그립니다."
        : "원본 색상을 자동으로 줄이고 HEX 색상을 바꾸면서 채색합니다.";

    public string SpeedDescription => SelectedSpeed switch
    {
        "안전하게" => "1× · 입력 안정성을 우선합니다.",
        "매우 빠르게" => "4× · 단순한 그림에서 사용하세요.",
        _ => "2× · 속도와 입력 안정성의 권장 균형입니다."
    };

    public string ProfileStatusLabel => IsProfileCalibrated
        ? "캘리브레이션 저장됨"
        : "캘리브레이션 필요";

    public bool IsExecutionPanelVisible =>
        IsExecutionPanelOpen || Stage is WorkspaceStage.Running or WorkspaceStage.Paused;

    public string ProgressLabel => $"{Math.Round(Math.Clamp(Progress, 0d, 1d) * 100d):0}%";

    public string ThemeLabel => ThemeMode switch
    {
        AppThemeMode.Light => "밝은 테마",
        AppThemeMode.Dark => "어두운 테마",
        _ => "시스템 테마"
    };

    public string FloatingLabel => IsFloating ? "플로팅 끄기" : "게임 위 플로팅";

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
        OnPropertyChanged(nameof(CanPrepare));
        OnPropertyChanged(nameof(CanClearImage));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnStageChanged(WorkspaceStage value)
    {
        OnPropertyChanged(nameof(StageLabel));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPrepare));
        OnPropertyChanged(nameof(CanClearImage));
        OnPropertyChanged(nameof(CanChangeImage));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsExecutionPanelVisible));
    }

    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
    }

    partial void OnThemeModeChanged(AppThemeMode value)
    {
        OnPropertyChanged(nameof(ThemeLabel));
    }

    partial void OnIsExecutionPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExecutionPanelVisible));
    }

    partial void OnIsFloatingChanged(bool value)
    {
        OnPropertyChanged(nameof(FloatingLabel));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPrepare));
        OnPropertyChanged(nameof(CanClearImage));
        OnPropertyChanged(nameof(CanChangeImage));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnIsCalibratingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPrepare));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnIsProfileCalibratedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ProfileStatusLabel));
    }

    partial void OnSelectedModeChanged(string value)
    {
        OnPropertyChanged(nameof(ModeDescription));
    }

    partial void OnSelectedRenderStyleChanged(string value)
    {
        OnPropertyChanged(nameof(RenderStyleDescription));
    }

    partial void OnSelectedSpeedChanged(string value)
    {
        OnPropertyChanged(nameof(SpeedDescription));
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
        IsExecutionPanelOpen = false;
        PlanSummary = "이미지를 분석하면 해상도, 색상 수, 예상 시간이 표시됩니다.";
    }

    public void SetLayoutWidth(double width)
    {
        LayoutMode = ResponsiveLayoutPolicy.FromWidth(width);
    }

    public void BeginLoading(string message)
    {
        BusyMessage = string.IsNullOrWhiteSpace(message) ? "준비 중…" : message;
        IsBusy = true;
    }

    public void EndLoading()
    {
        IsBusy = false;
    }

    public void SetProfileState(string name, bool calibrated)
    {
        ProfileName = name;
        IsProfileCalibrated = calibrated;
    }

    public void SetExecutionState(DrawingExecutionState state, string message)
    {
        Stage = state switch
        {
            DrawingExecutionState.Preparing => WorkspaceStage.Ready,
            DrawingExecutionState.Running => WorkspaceStage.Running,
            DrawingExecutionState.Paused => WorkspaceStage.Paused,
            DrawingExecutionState.Completed => WorkspaceStage.Completed,
            DrawingExecutionState.Stopping => HasImage ? WorkspaceStage.Ready : WorkspaceStage.SelectImage,
            DrawingExecutionState.Failed => WorkspaceStage.Failed,
            _ => Stage
        };
        StatusMessage = message;
    }

    public void SetProgress(double progress, string? message = null)
    {
        Progress = double.IsFinite(progress) ? Math.Clamp(progress, 0d, 1d) : 0d;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = message;
        }
    }

    public void OpenExecutionPanel()
    {
        IsExecutionPanelOpen = true;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeMode = ThemeMode switch
        {
            AppThemeMode.System => AppThemeMode.Light,
            AppThemeMode.Light => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };
    }

    [RelayCommand]
    private void ToggleExecutionPanel()
    {
        IsExecutionPanelOpen = !IsExecutionPanelOpen;
    }

    [RelayCommand]
    private void ToggleReducedMotion()
    {
        IsReducedMotion = !IsReducedMotion;
    }

    [RelayCommand]
    private void PauseOrResumeExecution()
    {
        if (Stage == WorkspaceStage.Running)
        {
            Stage = WorkspaceStage.Paused;
            StatusMessage = "실행을 일시 정지했습니다.";
        }
        else if (Stage == WorkspaceStage.Paused)
        {
            Stage = WorkspaceStage.Running;
            StatusMessage = "실행을 재개했습니다.";
        }
    }

    [RelayCommand]
    private void StopExecution()
    {
        if (!CanStop)
        {
            return;
        }

        Stage = HasImage ? WorkspaceStage.Ready : WorkspaceStage.SelectImage;
        StatusMessage = "실행을 중지하고 입력 상태를 해제했습니다.";
        SetProgress(0d);
    }

    [RelayCommand]
    private void ClearImage()
    {
        SelectedImagePath = null;
        SelectedImageName = "이미지가 선택되지 않았습니다.";
        StatusMessage = "이미지를 선택하면 작업을 시작할 수 있습니다.";
        Stage = WorkspaceStage.SelectImage;
        Progress = 0;
        IsBusy = false;
        IsExecutionPanelOpen = false;
    }
}
