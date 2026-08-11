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

현재 브랜치는 이미지 분석부터 Podiums 캘리브레이션, 안전 실행까지 연결된 7.5단계 통합 버전을 포함합니다. 캘리브레이션 중에는 Roblox 안의 안내 위치에 마우스를 놓고 `F6`, 실행 중에는 `F7` 일시 정지/재개와 `F8` 즉시 중지를 사용합니다.

## 정책과 안전

이 도구는 자동 입력을 사용하므로 대상 게임의 이용 약관과 경험별 규칙을 확인해야 합니다. anti-cheat 우회, 메모리 변조, 탐지 회피 기능은 제품 범위에 포함하지 않습니다.
