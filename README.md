# GameDraw

GameDraw는 그림을 그릴 수 있는 게임에 이미지를 외부 Windows 마우스/키보드 입력으로 그려 주는 범용 데스크톱 앱이다. 게임 내부 프로세스나 메모리를 수정하지 않고, 사용자가 한 번 캘리브레이션한 Canvas와 색상 UI를 `GameProfile`로 저장해 같은 Drawing Engine을 재사용한다.

## 현재 상태

이번 초기 기반은 실제로 연결된 vertical slice다.

- WinUI 3 / Windows App SDK 데스크톱 앱
- ImageSharp 기반 PNG, JPEG/JPG, WEBP, BMP 입력과 EXIF orientation
- Contain/Stretch, 색상 수 제한, 배경 무시, Fixed Palette 매핑, 선택적 dithering
- Original / Processed Preview
- Scanline, Pixel, 기본 managed Line Art DrawingPlan
- 정규화 좌표, Scanline contiguous run 병합, 색상 그룹화, 이동 샘플링
- Game Profile JSON 저장소와 import/export serializer 기반
- Manual, HEX Input, Fixed Palette, HSV Picker adapter 구조
- 전체 가상 화면 Canvas Calibration 오버레이
- HEX 입력 위치 및 기본 8색 Fixed Palette 버튼 위치 캘리브레이션
- Dry Run과 예상 stroke/시간
- Win32 SendInput, F7 Pause/Resume, F8 Emergency Stop
- 취소·예외 시 MouseUp 안전 처리
- Core xUnit 단위 테스트 12개

현재 실제 게임에 입력을 넣는 QA와 모든 DPI/다중 모니터 조합의 시각적 QA는 수행하지 않았다.

## 지원 환경

- Windows 10/11 (TargetPlatformMinVersion 10.0.17763.0)
- .NET SDK 10.0 이상
- x64 Debug/Release를 우선 검증

설치된 SDK 확인:

```powershell
dotnet --info
dotnet --list-sdks
```

## 빌드와 테스트

WinUI 앱은 런타임 팩을 포함하도록 먼저 복원한다.

```powershell
dotnet restore GameDraw.sln -r win-x64
dotnet build GameDraw.sln -p:Platform=x64
dotnet test tests/GameDraw.Core.Tests/GameDraw.Core.Tests.csproj
```

실행:

```powershell
dotnet run --project src/GameDraw.App/GameDraw.App.csproj -p:Platform=x64
```

개발 환경이 없는 배포용 폴더를 만들려면:

```powershell
dotnet publish src/GameDraw.App/GameDraw.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
```

MSIX 설치 프로그램 제작은 초기 범위에 포함하지 않았다.

## 첫 사용 흐름

1. 이미지 선택 또는 창으로 드래그한다.
2. `새 프로필`로 Game Profile을 만든다.
3. `Canvas Calibration`에서 게임의 그림 영역을 드래그한다. 전체 가상 화면과 음수 모니터 좌표를 지원한다.
4. Drawing Mode와 Color Adapter를 선택한다.
5. HEX Input이면 `Adapter Calibration`에서 입력 상자를 클릭한다. Fixed Palette면 색상 버튼을 안내 순서대로 클릭한다. Manual은 게임에서 색을 직접 선택한다.
6. Processed Preview와 Dry Run을 확인한다.
7. `Start Drawing`을 누르고 3초 안에 게임에 포커스를 옮긴다.

## Game Profile

프로필은 Git 저장소가 아닌 다음 위치에 저장한다.

```text
%LOCALAPPDATA%\GameDraw\profiles\
```

프로필에는 Canvas, adapter 설정, 브러시, 지연, 입력 샘플링, DPI 메타데이터가 들어간다. 특정 Roblox 게임이나 특정 게임 좌표는 소스 코드에 하드코딩하지 않는다. 스키마 설명은 [docs/GAME_PROFILE_SPEC.md](docs/GAME_PROFILE_SPEC.md)를 참고한다.

## Color Adapter

- Manual: 색상을 자동으로 바꾸지 않고 게임에서 직접 선택한다.
- HEX Input: 캘리브레이션한 입력 위치에 `#RRGGBB`를 Ctrl+A/타이핑/Enter로 넣는다.
- Fixed Palette: 캘리브레이션한 버튼 중 CIE Lab Delta E가 가장 가까운 색을 선택한다.
- HSV Picker: 프로필의 Hue/SV 영역으로 RGB를 HSV 좌표에 매핑한다. 완전 자동 UI 인식은 하지 않는다.

OCR, AI UI detection, OCR 기반 자동 탐색, DLL injection, process injection, 메모리 수정, packet manipulation, anti-cheat 우회, privileged driver는 사용하지 않는다.

## 입력 안전과 단축키

- F7: Pause / Resume
- F8: Emergency Stop

작업이 정상 완료·취소·예외·창 종료 어느 경우든 MouseUp을 시도한다. 대상 게임의 포커스를 확인한 뒤 Start를 눌러야 한다.

## DPI와 멀티 모니터 주의

Canvas는 물리 화면 좌표로 저장하고, Preview/계획은 정규화 좌표로 저장한다. 100%, 125%, 150%, 175% 배율과 primary/secondary 모니터, 음수 X/Y를 고려한 코드가 들어 있다. 다만 실제 장치 조합별 QA는 사용자의 환경에서 반드시 확인해야 한다.

## 프로젝트 구조

```text
src/GameDraw.App       WinUI 화면, ViewModel, profile store, calibration
src/GameDraw.Core      순수 이미지/색상/좌표/plan/execution core
src/GameDraw.Adapters  Color Adapter 구현
src/GameDraw.Windows   SendInput, hotkey, screen/DPI Win32 계층
tests/GameDraw.Core.Tests
docs/                  아키텍처·프로필·파이프라인 문서
profiles/examples/     스키마 예제
```

자세한 설계는 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), 실행 흐름은 [docs/DRAWING_PIPELINE.md](docs/DRAWING_PIPELINE.md)를 참고한다.

## 현재 제한과 다음 계획

- HSV 캘리브레이션 UI는 기본 adapter와 프로필 구조까지만 제공한다.
- Line Art는 가벼운 managed edge fallback이며 OpenCvSharp contour tracing은 후속이다.
- Settings 페이지, profile import/export UI, compact always-on-top progress panel은 후속 작업이다.
- 실제 게임별 입력 샘플링과 캔버스 브러시 pitch는 게임마다 Dry Run 후 조정해야 한다.
- DPI 조합 자동 테스트와 실제 게임 입력 QA는 아직 수행하지 않았다.

다음 권장 작업은 대상 게임 하나를 정해 캘리브레이션·Dry Run·실제 작은 이미지 입력을 수행하고, 그 결과로 profile의 `InputSampling`과 delays를 조정하는 것이다.
