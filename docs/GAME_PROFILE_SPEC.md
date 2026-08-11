# Game Profile 스펙

프로필은 실제 사용자의 `%LOCALAPPDATA%\GameDraw\profiles`에 JSON으로 저장된다. 저장소 안의 `profiles/examples`는 스키마 예제만 포함한다.

## 최상위 필드

| 필드 | 설명 |
| --- | --- |
| `schemaVersion` | 필수 정수. 현재 버전은 `1`이다. |
| `id` | 프로필을 식별하는 GUID. |
| `name` / `gameName` / `notes` | 사용자 표시용 정보. |
| `canvas` | 물리 화면 Canvas와 논리 해상도. |
| `colorAdapter` | 색상 선택 방식과 캘리브레이션. |
| `brush` | 브러시 전략, 논리 픽셀 피치와 권장 간격. |
| `delays` | 클릭·색상 변경·stroke 사이 지연. |
| `drawingSpeed` | 0보다 큰 속도 배율. |
| `inputSampling` | 이동 속도, 중간점 간격, 최소 stroke 시간. |
| `screenMetadata` | 캘리브레이션 시 모니터/DPI 기록. |
| `createdAt` / `updatedAt` | UTC timestamp. |

## Canvas

```json
"canvas": {
  "bounds": { "left": 100, "top": 200, "width": 800, "height": 600 },
  "logicalWidth": 100,
  "logicalHeight": 75
}
```

`bounds`는 물리 화면 좌표이며 음수 left/top을 허용한다. DrawingPlan에는 bounds를 저장하지 않고 정규화 좌표를 저장한다.

## Color Adapter

`kind`는 `manual`, `hexInput`, `fixedPalette`, `hsvPicker` 중 하나다.

- `manual`: 색상 선택을 자동화하지 않는다. 사용자가 게임에서 색을 직접 맞춘다.
- `hexInput`: `inputPosition`, Ctrl+A 여부, Enter 여부와 입력 지연을 저장한다.
- `fixedPalette`: `palette[]`에 이름, RGB, 버튼 위치를 저장한다. 색상 거리는 CIE Lab Delta E 76 근사로 계산한다.
- `hsvPicker`: `hsv.hueRegion`과 `hsv.saturationValueRegion`을 저장한다. RGB → HSV → 화면 좌표로 변환한다.

## 검증

serializer는 camelCase JSON과 문자열 enum을 사용한다. schemaVersion, ID, 이름, Canvas 크기와 adapter별 필수 캘리브레이션을 검증하며, 잘못된 프로필은 저장하거나 import하지 않는다.

## 마이그레이션 전략

1. 새 스키마를 읽을 때 `schemaVersion`을 먼저 확인한다.
2. 현재 버전보다 낮은 문서는 버전별 순수 변환 함수를 순서대로 통과시킨다.
3. 더 높은 버전은 조용히 무시하지 않고 사용자에게 지원되지 않는 버전임을 표시한다.
4. 마이그레이션 결과는 기존 파일을 바로 덮어쓰지 않고 임시 파일에 기록한 후 교체한다.

현재 구현은 version 1 serializer와 validation을 제공하며, version 2 migration 함수는 아직 추가하지 않았다.
