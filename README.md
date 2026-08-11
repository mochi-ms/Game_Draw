# GameDraw

GameDraw는 사용자가 선택한 이미지를 Windows 화면 기반 그림 게임의 캔버스에 자동으로 그려 주는 범용 데스크톱 애플리케이션입니다.

새 구현은 다음 원칙으로 시작합니다.

- 대상 게임의 프로세스·메모리·패킷·DLL을 수정하지 않는 외부 입력
- 창 상대 좌표와 게임 프로필을 통한 다중 게임 지원
- 원본 이미지 품질을 임의의 32×32·8색으로 제한하지 않는 이미지 파이프라인
- 픽셀 점, 스캔라인, 윤곽선, 면 채우기, 하이브리드 DrawingPlan
- 대상 창·캔버스·도구 상태를 검증하는 안전 실행
- One UI에서 영감을 받은 반응형 WinUI 3 데스크톱 UX

## 개발 기준

```powershell
dotnet restore GameDraw.sln
dotnet build GameDraw.sln -c Debug
dotnet test GameDraw.sln -c Debug
dotnet run --project src/GameDraw.App/GameDraw.App.csproj -c Debug -r win-x64
```

현재 브랜치는 자동 채색·검정 선화, 3단계 실행 속도, 게임 위 플로팅 작업창, 캔버스 자동 재등록과 배포 검증을 포함한 9단계 버전입니다.

기본 사용 순서는 다음과 같습니다.

1. `이미지 선택`으로 PNG/JPG/WEBP/BMP 파일을 불러옵니다.
2. `자동 채색` 또는 `검정 선화`, 실행 속도와 그리기 모드를 고른 뒤 `이미지 분석`을 누릅니다.
3. 처음 한 번 `Podiums 연결`을 누르고 Roblox 안의 안내된 8개 위치를 가리키며 `F6`을 누릅니다.
4. `Podiums에 그리기 시작`을 누른 뒤 15초 안에 Roblox로 전환합니다.

실행 중 `F7`은 일시 정지/재개, `F8`은 마우스를 즉시 해제하고 중지합니다. 프로필 공유 기능은 상단 `고급 설정`에서 사용할 수 있습니다.

`게임 위에 띄우기`를 켜면 작업창이 화면 오른쪽에 맞춰지고 항상 위에 유지됩니다. 자동 그리기는 실행 직전 현재 화면의 흰 캔버스를 다시 감지해 실제 좌표에 맞추므로, 창을 조금 이동하거나 크기를 조절해도 저장된 보정값의 작은 오차 때문에 차단되지 않습니다.

## 릴리스 묶음 생성

전체 테스트를 거친 self-contained Windows ZIP과 SHA-256 체크섬은 다음 한 줄로 생성합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

실제 Podiums 최종 확인 항목과 코드 서명 범위는 [최종 릴리스 인계 문서](docs/FINAL_RELEASE.md)에 정리되어 있습니다.

## 정책과 안전

이 도구는 자동 입력을 사용하므로 대상 게임의 이용 약관과 경험별 규칙을 확인해야 합니다. anti-cheat 우회, 메모리 변조, 탐지 회피 기능은 제품 범위에 포함하지 않습니다.
