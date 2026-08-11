# Drawing Pipeline

## 1. 입력과 전처리

ImageSharp가 PNG, JPEG/JPG, WEBP, BMP를 읽고 EXIF orientation을 적용한다. 큰 이미지는 4096px 최대 변으로 제한한다. Canvas의 논리 해상도에 `Contain`으로 맞춘 뒤 투명/흰색/사용자 지정 배경을 무시할 수 있다.

Fixed Palette를 사용하면 일반 색상 양자화보다 먼저 프로필 팔레트에 매핑한다. 그 외에는 입력 색 수 제한과 선택적 Floyd–Steinberg 방식의 간단한 dithering을 사용한다.

## 2. 계획 생성

`DrawingPlanner`는 픽셀을 바로 입력 이벤트로 보내지 않는다.

- Scanline: 한 줄의 연속된 같은 색을 하나의 stroke로 합친다.
- Pixel: 무시되지 않은 픽셀마다 한 점 stroke를 만든다.
- Line Art: 인접 색 Delta E 차이를 이용한 managed edge fallback을 만들고 scanline으로 계획한다. 고급 contour/vector tracing은 후속 작업이다.

색상 그룹을 먼저 만들고 그룹별로 stroke를 저장해 색상 변경 횟수를 줄인다. 불필요한 TSP solver를 사용하지 않으며, Scanline의 serpentine traversal로 수평 이동을 줄인다.

## 3. 실행

실행 직전에 정규화 점을 프로필 Canvas의 물리 좌표로 변환한다. 긴 stroke는 `SampleSpacingPixels`에 따라 중간 점으로 분할한다. `MovementSpeedPixelsPerSecond`, inter-stroke delay, color-change delay를 이용해 예상 시간을 계산한다.

`DrawingExecutor`는 다음 상태를 보고한다.

```text
Idle → Preparing → Drawing → Completed
                         ↘ Stopping
                         ↘ Error
```

Pause는 `PauseGate`, Stop/Emergency Stop은 `CancellationToken`으로 처리한다. 색상 adapter가 입력을 완료한 후 stroke를 그리며, 예외·취소·정상 완료 모두 마지막에 MouseUp을 시도한다.

## 4. Dry Run

Dry Run은 동일한 Image Processor와 Drawing Planner를 사용하되 `IInputController`를 호출하지 않는다. 색 수, stroke 수, 이동 거리와 예상 시간을 표시해 캘리브레이션을 검토할 수 있다.

## 5. 테스트 경계

Core 테스트는 색상 변환, 팔레트 거리, 좌표, 배경 무시, 계획 통계, 프로필 JSON과 취소 시 MouseUp을 검증한다. 실제 게임 UI의 클릭 위치, DPI 조합과 대상 게임의 입력 샘플링은 앱 실행 환경에서 별도 QA가 필요하다.
