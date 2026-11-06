# Ked.Presentation.Core — 복사 사본 (정본은 런타임 저장소)

`ked-presentation-runtime` 저장소 `Assets/Ked.Presentation/Core/`의 복사 사본이다(예전 자리 `Assets/Scripts/Ked.Presentation.Core/`).
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: 런타임 → 이 사본.** `ked-stage-runtime`은 저쪽의 포트폴리오용 분리본이라 출처가 아니다.
패키지화는 소유자 결정으로 보류 중이며(2026-08-20), 그 대신 아래 동기화 절차가
반복 가능한 대체물이다 (`docs/work-orders/presentation-refresh-orders.md` W64).

- 최근 동기화: **2026-11-07** — 저쪽 `dev` @ `bb90f3dd`. 소스는 줄바꿈까지 같은 한 벌(`diff -r` 0).
  - 들어온 것: 목표값 이름 정리(`StageNodeClaim` → `NodeTarget`, 리덕션 클래스들 → `NodeTargets` · `ShotIntents` —
    메서드 이름 = 커맨드 이름), 목표 alpha 페이드 `fade`(`NodeTargets.Fade` · 폴드), slide의 ease · punch 인자,
    gesture와 커스텀 이징 곡선 제거(`CurveKind` · `OscillationFunctions` 삭제 — 몸짓은 motion), 규약 문서
    `reduction-boundary.md` → `target-boundary.md`.
  - 이쪽 코드 멤버 감사: 이쪽에만 있던 차이 0건 — 저쪽 본으로 덮었다.
  - 소비자 적응(이쪽): 커스텀 곡선 기능 전체(편집기 · 창 · 베이커 · 모델 · 저장 · `curves.json` 출력)와 gesture
    프리뷰(`StageMotionPlan`의 진동 구간)를 걷었다. 옛 연출의 곡선 목록은 읽지 않고 `@이름` 이징은 읽을 때 빼서
    기본 이징으로 둔다(`LegacyCommandUpgrade`) — 열 때 몇 곳인지 상태줄로 알린다. 카탈로그에 `fade`를 넣고
    gesture를 뺐다(옮김 규칙 없음 — 남은 줄은 모르는 명령으로 드러난다). 프리뷰는 `fade`를 가시성으로 그리고
    0과 1 사이 목표는 "미표시"로 남긴다. slide의 ease · punch를 프리뷰 계획이 런타임 파서와 같은 규칙으로 읽는다.
    `MotionInspection`이 `NodeTargets.MoveBy`를 부른다. 런타임 예제 두 챕터의 gesture 세 줄은 런타임과 같게
    `motion <슬롯> nod`로 옮기고 다시 발행했다.
  - 순수 테스트: 저쪽 `Tests/EditMode/`(이제 Core 밖)에서 엔진을 참조하지 않는 것을 다시 들였다(430개).
    튜닝 덤프(`ExportedTuning/`)는 원본 경로 표기만 바뀌었다.

- 2026-11-06 — 저쪽 `dev` @ `3f1fb583`.
  - 들어온 것: 샷 식(`Transforms/ShotLayoutMath` — 카메라 · 깊이 층 반응 · 합성 · 팬 역산), 층 프로필 곡선과 라벨
    (`LayerProfileDto` · `StageLayerLabels` — far −1 … close 1), 무대의 역할(`StageRoles` — 본 무대 stage00만 샷을 따르고
    덮는 무대 stage01 · stage02는 카메라 중립 고정), 슬롯 부착의 깊이 값(`SlotAttachment.LayerDepth`), 하네스의 무대 뷰
    비교(`StageViewSnapshot` · `StageStateComparer.CompareStageView`), shot_focus_to가 층 반응까지 넣어 명중
    (`ShotZoomFocusReduction` 시그니처가 바뀌었다 — 이쪽 소비자 0), char_to 제거(리그는 무대 · 층을 옮기지 않는다).
  - 이쪽 코드 멤버 감사: 이쪽에만 있던 차이는 주석 아홉 파일(이쪽 `7b297a7` 주석 정리 · `6611fd3` place 주석)뿐이었고,
    저쪽이 같은 정리를 제 문구로 이미 해 두었다 — 저쪽 본으로 덮었다. 로컬 코드 수정 0건.
  - 소비자 적응(이쪽): 프리뷰(`StageSceneComposer`)가 슬롯 · 배경이 선 무대 · 층의 식(`ShotLayoutMath.ProjectToRigSpace`)으로
    그리고 덮는 무대는 샷을 태우지 않는다, `RuntimeTuningLibrary`가 `presets/layer-profile.json`을 있으면 싣고
    `LayerProfile`(없으면 코어 기본)을 낸다, 카탈로그 · 옮김표 · 런타임 등록 픽스처에서 char_to를 걷었다(옮길 정본 없음).
  - 순수 테스트: `ShotLayoutMathTests`를 새로, 바뀐 다섯 개를 저쪽 본으로. 튜닝 덤프(`ExportedTuning/`)는 그 사이 변경 0.

- 2026-11-04 — 저쪽 `dev` @ `5de605a4`.
  - 들어온 것: `Motion/`(모션 클립 · 규칙 · 수학 — `motion` 폴드), `StageReducer.Background.cs`와
    `BackgroundDimensionsDto`(배경은 캐스팅하지 않은 슬롯 — 저쪽 2026-10-29 배경 리그 제거),
    대본 커맨드 정본화(2026-11-03 — 별칭 이름을 걷음. 옛 연출은 `LegacyCommandUpgrade`가 읽을 때 정본으로 옮긴다).
  - 이쪽 코드 멤버 감사: 이쪽에만 있던 줄은 모두 저쪽의 옛 판(별칭 · 옛 slot 재선언)이었다 — 로컬 수정 0건.
  - 소비자 적응(이쪽): `CoreStageFold`가 `bg_*`를 보완 폴드의 배경 층으로 그리고 배경 슬롯을 인물 칸으로 투영하지 않는다,
    `RuntimeTuningLibrary`가 `background-dimensions.json` · `motions.json`(`MotionLibraryJson` — 저쪽 `MotionLibrary`와 같은 규칙)을 싣는다.
  - 픽스처: `tests/Vn.Authoring.Tests/TuningFixtures/ExportedTuning/`과 앱 내장 기본 튜닝(`src/Vn.App/Assets/DefaultTuning/`)을
    저쪽 `ExportedTuning/`(+ 대본 번들 곁의 `motions.json`)으로. 저쪽 덤프에는 공개 가능한 초상(harin · sigure · kasmi)만 있어 테스트의 캐릭터를 harin으로 바꿨다.
  - 순수 테스트: `Tests/EditMode/*.cs` 중 엔진을 참조하지 않는 것(UnityParity · DOTween · AnimationCurve 대조 제외)을 들여왔다.
- 그 전: 2026-08-24 (`df9ba4b` slide 개통까지), 2026-08-20 (W64), `3fe52e9b` (2026-08-05, 최초 복사 스냅샷)
- 범위: `Ease/`·`Motion/`·`Primitives/`·`Reduce/`·`State/`·`Tokens/`·`Transforms/`·`Tuning/` 소스와
  `Documentation~/` 규약 문서. 순수 테스트(UnityParity 제외)는
  `tests/Ked.Presentation.Core.Tests/`에 있다.

## 동기화 절차 (다음에도 이대로)

1. **감사** — 줄바꿈 정규화 diff(`tr -d '\r'`)로 세 갈래를 가른다:
   ⓐ 런타임에만 있는 파일(가져온다) ⓑ 내용이 다른 파일(런타임 본으로 교체)
   ⓒ 이쪽에만 있는 것 — `README.md`(이 파일)·`Ked.Presentation.Core.csproj`는 툴 살림이라
   유지, **코드 멤버가 이쪽에만 있으면 그건 과거 로컬 수정이다** — 목록으로 남기고
   저쪽 반영을 소유자에게 물은 뒤 처리한다 (2026-08-20 감사에서는 0건이었다).
2. **이식** — 저쪽 `Assets/Ked.Presentation/Core/`에서 `*.meta`·`*.asmdef` 제외 전부 복사.
   테스트는 저쪽 `Assets/Ked.Presentation/Tests/EditMode/*.cs` 중 `UnityEngine` · `DG.Tweening`을 쓰지 않는 것
   (UnityParity 제외)을 `tests/Ked.Presentation.Core.Tests/EditMode/`로.
3. **보수** — 소비자(Vn.Authoring·Vn.App)의 컴파일·의미 적응은 **소비자 쪽에서** 고친다.
   이 사본의 파일을 고쳐 맞추지 않는다 — 사본을 고치는 순간 다음 동기화가 그 수정을
   조용히 지운다. 저쪽에 문제가 있으면 목록으로 남겨 소유자가 전달한다.
4. **고정** — 전체 빌드 + 전체 테스트. 튜닝 실덤프 픽스처
   (`tests/Vn.Authoring.Tests/TuningFixtures/ExportedTuning/`)도 저쪽 `ExportedTuning/`
   폴더째 갱신한다(코어가 새 튜닝 축을 요구하면 낡은 픽스처가 진단을 낸다 —
   2026-08-20의 role-anchor가 그랬다). run-log에 항목을 남긴다.

zip 스냅샷 등 **빌드 검증 없이 건너온 코드**를 그대로 믿지 말 것 — 같은 컴파일
오류가 되살아난 전력이 있다. 복사 후 반드시 이쪽에서 빌드·테스트까지가 동기화다.

## 유니티 호환 안전핀

csproj가 원본 asmdef의 `noEngineReferences: true`와 유니티 C# 수준(LangVersion 9,
netstandard2.1)을 컴파일러 제약으로 유지한다 — 코드가 유니티로 못 돌아가는 상태가
되는 것을 막는다.
