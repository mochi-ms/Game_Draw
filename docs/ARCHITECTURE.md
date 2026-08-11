# GameDraw 아키텍처

GameDraw는 특정 게임 클라이언트 내부를 수정하지 않는 외부 Windows 데스크톱 앱이다. 게임별 차이는 `GameProfile`과 Color Adapter에 저장하고, 이미지 처리·DrawingPlan·실행 엔진은 재사용한다.

## 프로젝트 경계

```text
GameDraw.App
    ├── GameDraw.Core
    ├── GameDraw.Adapters
    └── GameDraw.Windows
```

- `GameDraw.Core`: 이미지 버퍼, 색상 수학, 정규화 좌표, 프로필 모델, DrawingPlan, 계획 생성, 실행 추상화와 취소/일시정지 로직을 가진다. WinUI, 특정 게임, 화면 좌표 상수, SendInput을 참조하지 않는다.
- `GameDraw.Adapters`: Manual, HEX Input, Fixed Palette, HSV Picker의 색상 선택 전략을 구현한다. 입력은 Core의 `IInputController` 추상화로만 요청한다.
- `GameDraw.Windows`: Win32 `SendInput`, 전역 `RegisterHotKey`/윈도우 subclass, 가상 화면과 DPI 정보를 제공한다.
- `GameDraw.App`: WinUI 3 화면, 파일 선택, 프로필 저장소, 캘리브레이션 오버레이와 composition root를 담당한다.
- `GameDraw.Core.Tests`: UI 없이 Core의 중요한 계약을 검증한다.

## 처리 파이프라인

```text
Image
  ↓
Image Processor (ImageSharp, EXIF orientation, fit, background, palette)
  ↓
Processed Image (logical resolution)
  ↓
Drawing Planner (scanline/pixel/line-art)
  ↓
DrawingPlan (normalized points, color groups, strokes)
  ↓
Color Adapter (profile-specific color selection)
  ↓
Coordinate Mapper (normalized → calibrated physical screen)
  ↓
Drawing Executor (async, pause, cancellation, sampling)
  ↓
Windows SendInput
  ↓
Target Game
```

DrawingPlan은 이미지와 입력 장치 사이의 중간 표현이다. 따라서 미리보기, Dry Run, 예상 시간 계산, 실제 실행이 같은 계획을 공유한다.

## 안전성

- 그리기 작업은 UI thread에서 반복 입력을 수행하지 않고 `Task`와 `CancellationToken`을 사용한다.
- F7은 `PauseGate`를 통해 일시정지/재개하고 F8은 즉시 취소한다.
- `DrawingExecutor`의 `finally`에서 항상 왼쪽 MouseUp을 시도한다.
- 게임 프로세스 메모리, DLL injection, 내부 스크립트, 패킷, 드라이버, anti-cheat 우회는 사용하지 않는다.

## 좌표와 DPI

DrawingPlan의 점은 `0.0 ~ 1.0` 정규화 좌표다. 프로필의 Canvas는 물리 화면 좌표를 저장하며 실행 시점에만 매핑한다. 캔버스 선택 오버레이는 Windows 가상 화면의 음수 X/Y와 다중 모니터 영역을 포함한다. WinUI의 DIP는 오버레이의 `RasterizationScale`을 적용해 물리 좌표로 변환한다.

실제 125%/150%/175% 배율과 복수 모니터 조합의 QA는 개발 환경에서 모두 수행하지 못했으므로 배포 전 대상 PC에서 재검증해야 한다.

## 확장 지점

- `DrawingPlanner`: serpentine scanline, pixel, 기본 managed line-art 이후 Outline + Fill을 추가할 수 있다.
- `IColorAdapter`: RGB 입력, Hue wheel, 게임별 브러시 UI 등을 추가할 수 있다.
- `IInputController`: SendInput 외에 Dry Run용 recording controller나 테스트용 fake를 주입할 수 있다.
- `GameProfile`의 `SchemaVersion`과 serializer를 통해 향후 마이그레이션을 추가한다.
