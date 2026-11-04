# 런타임 예제 — 두 툴로 낸 것 (2026-11-04)

`ked-presentation-runtime`의 예제 두 챕터를 손 대본 대신 **director(원고) + compositor(연출)**로 다시 낸 것이다.
소유자: *"목표는 런타임 예제를 손 대본 대신 두 툴로 내는 것입니다."*

| 폴더 | 챕터 | 런타임에서 |
|---|---|---|
| `FestivalEve/` | `festival_eve` 축제 전날 — 장면 둘 · 에피소드 다섯 · 분기 카드 다섯 · 스탯 둘 | 진행 · 저장 예제 |
| `PresentationDemo/` | `presentation_demo` 연출 데모 — 에피소드 하나 | 연출 단독 예제(기본 노드 `Demo_Presentation`) |

## 파일

| 파일 | 주인 | 무엇 |
|---|---|---|
| `*.vnproject.json` · `story/` · `game.definition.json` | **ked-story-director** | 원고 — director `samples/RuntimeSamples`의 **사본**이다. 이 툴은 읽기만 한다(K3). 고칠 때는 director에서 고치고 여기로 다시 옮긴다 |
| `*.vncompose.json` · `results.vnresults.json` | 이 툴 | 연출 — 카드마다 연출 노드 하나(Setup · 줄마다 명령 · 줄 태그), 대사 · 연출 발행본 |
| `yarn/*.yarn` | 이 툴 | 게임이 읽는 대본 — 카드 하나에 파일 하나(`{챕터}_{카드}.yarn`) |

진행 JSON(`exported/{챕터}.progression.json`)은 director가 낸다 — director `samples/RuntimeSamples/*/exported/`.

## 런타임에 넣기

`ked-presentation-runtime/Assets/Samples/Dialogue/ChapterProgression/`에

- `yarn/*.yarn`을 그대로 넣고 손 대본(`FestivalEve.yarn` · `Demo_Presentation.yarn`)을 걷는다 — 둘이 함께 있으면 노드 이름이 겹쳐 Yarn 컴파일이 깨진다.
- director의 진행 JSON 내용을 `FestivalEve.progression.json` · `Demo_Presentation.progression.json`에 덮는다(파일 이름 · `.meta`를 지켜야 장면의 참조가 산다).
- 줄 신원(`#line:`)과 에피소드 Id가 바뀌었으니 `SampleBootstrap`의 `saveContentVersion`을 올린다 — 옛 저장은 맞지 않는다.

## 손 대본과 다른 점 (뜻은 같다)

대조는 주석 · 빈 줄 · `#line:`을 걷고 줄 단위로 했다. 남는 차이는 아래가 전부다.

- **조건 분기는 분기 카드다** — 툴에는 `<<else>>`가 없다(2026-11-04 소유자: *"detour를 한 뒤에 그 노드 안에서 조건이 맞으면 재생하는 방식"*).
  부르는 카드에는 조건 없는 `<<detour 카드>>` 줄이 서고, 다녀가는 카드가 제 첫머리 `<<if>>`로 본다. else 쪽은 여집합 조건(AND)이다.
  - `Festival_Wrapup` → `_Remade`(성실 ≥ 2) · `_Sticker`(성실 < 2)
  - `Festival_Staffroom` → `_Kasmi`(요령 ≥ 3) · `_Praise`(성실 ≥ 3 · 요령 < 3) · `_Pass`(성실 < 3 · 요령 < 3)
  - 분기 카드도 챕터의 에피소드라 진행 JSON에 노드로 실린다(들어오는 간선이 없고 `<<detour>>`로만 들어간다 — R7 P-6).
- **카스미의 퇴장은 카스미 갈래 안이다** — 손 대본은 챕터 끝에서 명령만 든 `<<if>>`로 내보냈는데, 툴의 명령은 대사 줄에 걸리므로
  걸 줄이 없다(Q4). 이제 카스미가 제 대사 뒤 하린의 줄에서 나간다.
- **주석이 없다**(Q3 — "그냥 주석을 없앱니다").
- **인자가 기본값까지 채워진다** — `<<show sigure>>` → `<<show sigure e1 14fr>>`. 채우는 값은 런타임 파서의 생략값과 같다
  (`tests/Vn.Authoring.Tests/TuningFixtures/runtime-defaults.txt`가 잠근다 — 이번에 `size`의 길이 0fr ↔ 10fr 어긋남을 고쳤다).
- **에피소드 Id = 카드 이름**(`EP01` → `Festival_Classroom`) — director에서 카드를 만들면 둘이 같은 이름으로 묶인다.

## 다시 내기

앱에서 `*.vnproject.json`을 열면 이 폴더의 연출이 함께 열린다. 내보내기 폴더는 `yarn`이다.
`tests/Vn.Authoring.Tests/RuntimeSampleTests.cs`가 다시 낸 Yarn이 `yarn/`과 같은지 본다 — 연출을 고치면 다시 내서 함께 싣는다.
무대 프리뷰의 초상 · 배경은 에셋 뿌리를 런타임 저장소의 `Assets/Samples/Portraits` · `Assets/Samples/Resources/Backgrounds`로 잡아야 보인다(저장소 사이 경로라 이 파일에는 적지 않았다).
