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
    public partial string SelectedMode { get; set; } = "자연스러운 펜선";

    [ObservableProperty]
    public partial string SelectedRenderStyle { get; set; } = "자연스러운 펜선";

    [ObservableProperty]
    public partial string SelectedQuality { get; set; } = "균형";

    [ObservableProperty]
    public partial string SelectedSpeed { get; set; } = "매우 빠르게";

    [ObservableProperty]
    public partial string SelectedCanvasAspect { get; set; } = "1:1 정사각형";

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
    public partial bool SmartSubjectEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsProfileCalibrated { get; set; }

    [ObservableProperty]
    public partial bool IsColorToolsCalibrated { get; set; }

    [ObservableProperty]
    public partial bool IsCalibrating { get; set; }

    [ObservableProperty]
    public partial string CalibrationMessage { get; set; } = "Roblox Podiums 창을 찾은 뒤 F6으로 좌표를 기록합니다.";

    [ObservableProperty]
    public partial string PlanSummary { get; set; } = "이미지를 분석하면 해상도, 색상 수, 예상 시간이 표시됩니다.";

    [ObservableProperty]
    public partial double MaximumColors { get; set; } = 16d;

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

    public bool CanStart => HasImage && IsProfileCalibrated &&
        (SelectedMode is not "원본 색상" and not "픽셀 컬러" || IsColorToolsCalibrated) &&
        !IsBusy && !IsCalibrating &&
        Stage is WorkspaceStage.Ready or WorkspaceStage.Completed or WorkspaceStage.Failed;

    public bool CanPause => Stage is WorkspaceStage.Running or WorkspaceStage.Paused;

    public bool CanStop => CanPause;

    public bool IsPaused => Stage == WorkspaceStage.Paused;

    public string ModeDescription => SelectedMode switch
    {
        "정밀 윤곽선" => "경계 대비를 세밀하게 추적합니다. 로고·도형·이미 선화인 원본에 적합합니다.",
        "원본 펜선 보존" => "가로 스캔 없이 원본 암부의 중심선을 추적해 큰 외곽선→얼굴→내부선 순서로 그립니다.",
        "원본 색상" => "대표 색을 HEX 입력란에 자동 입력하고 같은 색 영역을 빠른 선으로 채웁니다.",
        "픽셀 컬러" => "형태를 픽셀 단위로 보존합니다. 작은 아이콘과 도트 그림에 적합합니다.",
        "자동 추천" => "사진과 일러스트를 판별해 자연스러운 펜선을 우선 적용합니다.",
        _ => "암부 중심선을 한 번씩 이어 그려 이중선과 털선을 줄입니다. 인물·애니 캐릭터에 권장합니다."
    };

    public string QualityDescription =>
        $"{SelectedQuality}: 미리보기와 실제 실행 경로의 해상도·곡선 정밀도·게임 입력 간격을 함께 조절합니다.";

    public string SpeedDescription =>
        $"{SelectedSpeed}: 게임이 놓치지 않는 프레임 동기화 최대 안전 속도를 자동 적용합니다.";

    public string ProfileStatusLabel => IsProfileCalibrated
        ? IsColorToolsCalibrated ? "캔버스 · 도구 · HEX 저장됨" : "캔버스 저장됨 · 색상 도구 설정 필요"
        : "캔버스 연결 필요";

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

    partial void OnIsColorToolsCalibratedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ProfileStatusLabel));
    }

    partial void OnSelectedModeChanged(string value)
    {
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnSelectedQualityChanged(string value)
    {
        OnPropertyChanged(nameof(QualityDescription));
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

    public void SetProfileState(string name, bool calibrated, bool colorToolsCalibrated = false)
    {
        ProfileName = name;
        IsProfileCalibrated = calibrated;
        IsColorToolsCalibrated = colorToolsCalibrated;
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

    public void ResetWorkspace()
    {
        SelectedImagePath = null;
        SelectedImageName = "이미지가 선택되지 않았습니다.";
        SelectedMode = "자연스러운 펜선";
        SelectedRenderStyle = "자연스러운 펜선";
        SelectedQuality = "균형";
        SelectedSpeed = "매우 빠르게";
        SmartSubjectEnabled = true;
        MaximumColors = 16d;
        Progress = 0d;
        PlanSummary = "이미지를 분석하면 해상도, 색상 수, 예상 시간이 표시됩니다.";
        BusyMessage = "준비 중…";
        IsBusy = false;
        IsExecutionPanelOpen = false;
        Stage = WorkspaceStage.SelectImage;
        StatusMessage = "작업을 초기화했습니다. 새 이미지를 선택하세요.";
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
