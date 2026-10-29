# ked-story-compositor

> **대사에 연출을 얹는 툴.** 2026-10-29에 [`ked-story-director`](https://github.com/123456789qwaszx/ked-story-director)
> (그 위로는 VnTool · `VNStoryEditor_avalonia`)에서 이력째 갈라졌다. 이야기의 뼈대(챕터 · 에피소드 · 선택지)와 대사는
> director가 쓰고, 이 툴은 그 프로젝트를 **원고로 읽어** 무대 위에서 연출을 붙이고 게임이 읽을 Yarn을 낸다.
> 계획과 단계는 [`docs/plans/compositor-split.md`](docs/plans/compositor-split.md).
> 아래 "문서" 표의 안내서 대부분은 아직 VnTool · director 시절(두 그래프 · 엑셀 워크플로)을 설명한다 —
> 이 툴의 지금 모양은 이 README와 `compositor-split.md`가 정본이다.

비주얼 노벨 연출 도구. C#/.NET 10 + Avalonia, 한국어 코드베이스.

## 하는 일

창은 **무대 하나**다.

- **챕터 · 씬 고르기** — 무대 위쪽의 선택기로 director가 써 둔 챕터와 씬(대사 노드)을 고른다. 판(씬)이 있는
  챕터만 목록에 오른다.
- **연출 붙이기** — 씬을 고르면 그 대사를 고정(발행)하고 연출 채널을 세운다. 터미널에서 줄을 골라 연출 명령을
  더하고(우클릭 [＋ 연출 추가]), 수치 인스펙터 · Setup 편집 · 빠른 명령 · 이징 곡선으로 다듬는다. 무대가 그
  자리에서 접어 보여 준다.
- **에셋** — 무대 안의 에셋 탐색기가 배경 · 초상화 · 소리를 보여 준다.
- **재생** — 줄 단위로 넘기고, 에피소드 끝에서는 챕터의 선택지가 무대에 선다.
- **내보내기** — 게임용 **Yarn 묶음**(대사 + 연출 명령)과 곡선을 낸다. Yarn은 이 툴만 낸다(director는 내지 않는다).

이 툴에는 그래프도 대사 편집기도 없다. 대사 · 화자 · 조건 · 챕터 구조를 고치려면 director에서 고치고, 이 툴에서
다시 연다.

## 원고는 읽기만, 연출은 원고 옆 파일에

director의 프로젝트(`.vnproject.json`과 그 `script/` · `story/`)는 **원고**다 — 이 툴은 그 파일을 한 바이트도 쓰지
않는다. 연출은 원고 옆 **`<이름>.vncompose.json`**에 산다. 열면 둘을 합쳐 한 프로젝트로 보고, 저장은 연출 쪽만 쓴다.

```text
MyStory/
├─ 예제.vnproject.json         원고 (director) — 이 툴은 읽기만 한다
├─ script/  story/             원고의 대본 · 판 (director)
├─ game.definition.json        초상화 매핑 · 연출 커맨드 어휘 · 스탯 · 화자
├─ 예제.vncompose.json         연출 — 연출 노드 · 배선 · 합성 · 곡선 · 빠른 명령 · 에셋 뿌리 · 내보내기 설정
├─ results.vnresults.json      발행본(대사 · 연출 고정본)
└─ assets/                     backgrounds · portraits · bgm · sfx
```

- **열기**는 원고(`.vnproject.json`)로도, 이 툴의 파일(`.vncompose.json`)로도 된다.
- 연출이 아직 원고 안에 있던 **옛 프로젝트**(VnTool · director의 연출 제거 전)는 그 연출을 가져온다. director가 옛
  프로젝트를 열며 남긴 `presentation-original/`에서도 가져온다. 처음 저장하면 원고 옆에 `.vncompose.json`이 생긴다.
- director가 판을 옮기거나 노드를 지웠으면, 연출 노드는 대사 노드의 판에 다시 서고 사라진 노드를 가리키던 배선은
  버린다(상태줄이 몇 개인지 말한다).

## 실행

```powershell
dotnet run --project .\src\Vn.App\Vn.App.csproj
```

## 검증

```powershell
dotnet test .\ked-story-compositor.sln
```

## 연출자에게 전달할 Windows 휴대용 패키지

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-windows.ps1
```

`artifacts\ked-story-compositor-win-x64.zip` — 압축을 풀고 `ked-story-compositor.exe`를 누르면 된다. .NET 런타임을
함께 담으므로 받는 PC에 개발 환경이 필요 없다.

## 문서

| 문서 | 무엇이 있나 |
|---|---|
| [`docs/plans/compositor-split.md`](docs/plans/compositor-split.md) | **이 툴의 정본.** 갈라 낸 경위 · 결정(K1~K7) · 단계(C1~C3) · 열린 것 |
| [`docs/plans/director-split.md`](docs/plans/director-split.md) | 그 위의 이력 — VnTool에서 director로 온 경위(D0~D4) |
| [`docs/runtime-contract.md`](docs/runtime-contract.md) | 유니티 런타임과의 계약 |
| [`docs/handoff/io-reference.md`](docs/handoff/io-reference.md) | 입출력의 형식 (VnTool 시절 기준) |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | 코드 구조 (VnTool 시절 기준 — 그래프 · 대본 쪽 서술은 이 툴에 없다) |
| [`docs/design/two-graphs.md`](docs/design/two-graphs.md) | director의 두 그래프 설계 — 이 툴에는 그래프가 없다 |

## Yarn 분석 도구

`Vn.Core`와 `Vn.Cli`는 Yarn 프로젝트를 읽고 검증하는 별도 도구다. 저작 경로와 분리돼 있다.

```powershell
dotnet run --project .\src\Vn.Cli\Vn.Cli.csproj -- .\samples\Valid\Demo.yarnproject .\samples\Valid\game.schema.json
```
