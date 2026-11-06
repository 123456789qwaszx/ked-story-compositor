using Avalonia.Controls;
using Avalonia.Input.Platform;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// 직접 조작의 편집 대상. null이면 조작할 것이 없는 화면이고,
/// <see cref="DisabledReason"/>이 있으면 보이지만 잠긴 화면이다(발행 결과 등) —
/// 기능 잠금이 아니라 안내가 함께 간다.
/// </summary>
internal sealed record StageEditContext(
    string PresentationNodeId,
    string? LineId,
    string? DisabledReason = null)
{
    public bool Editable => DisabledReason is null && LineId is not null;
}

/// <summary>
/// 선택지 제시 화면의 옵션 버튼 하나 — 에피소드 끝의 챕터 간선 선택지다.
/// <see cref="Choose"/>가 클릭의 뜻이고 도착 에피소드로 넘어간다.
/// <see cref="IsDisabled"/>는 지금 스탯으로 못 지나는 길이다(숨김·잠김·깨진 조건) —
/// 게임에서와 같이 못 고르되, 저작 도구라 지우는 대신 흐리게 보이고 사유를 문구가 든다.
/// <see cref="IsSelected"/>는 루트가 고르기로 한 것(테두리를 밝힌다).
/// 대사 안 선택지의 갈래는 여기서 고르지 않는다.
/// </summary>
internal sealed record StageChoiceOption(
    string Text,
    bool IsSelected,
    Action? Choose = null,
    bool IsDisabled = false);

/// <summary>
/// 이 라인의 소리 커맨드 표시 문자열 — 정지 프레임에 그릴 것이 없는 오디오를
/// ♪ 칩으로 알린다. "오디오" 판정은 카테고리 id <c>audio</c> 규약이다: 다른 id를 쓰는
/// 게임 정의에서는 칩이 안 뜰 뿐, 잘못 접히는 것은 없다(표시 편의이지 해석 규칙이 아니다).
/// </summary>
internal static class StageAudioCues
{
    public static IReadOnlyList<string>? Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands)
    {
        string[]? cues = AudioOf(catalog, commands)?
            .Select(command => CommandText.Format(
                catalog.Find(command.DefinitionId), command.DefinitionId, command.Arguments))
            .ToArray();

        return cues is { Length: > 0 } ? cues : null;
    }

    /// <summary>이 라인의 오디오 커맨드 원본 — 실재생 라우터가 소화한다. 판정 규약은 칩과 같다.</summary>
    public static IReadOnlyList<PresentationResultCommand>? AudioOf(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands)
    {
        if (commands is null || commands.Count == 0)
        {
            return null;
        }

        PresentationResultCommand[] audio = commands
            .Where(command => string.Equals(
                catalog.Find(command.DefinitionId)?.CategoryId, "audio", StringComparison.Ordinal))
            .ToArray();

        return audio.Length > 0 ? audio : null;
    }
}

/// <summary>
/// 무대에서 수치를 만질 수 있는 이동 커맨드 하나 — <b>이동 편집기(슬라이더·이징)와
/// 휴지 배치</b>가 이것 하나를 본다.
/// 좌표는 <b>루트 공간 픽셀</b>이고, 화면으로 옮기는 것은 <see cref="StageSceneView"/>가
/// 샷 규약(<c>ShotIntentMath</c>)으로 한다.
/// </summary>
/// <param name="Arguments">현재 인자 토큰 — 슬라이더의 시작값이자 되돌릴 자리다.</param>
internal sealed record StageMotionCue(
    string CommandId,
    string DefinitionId,
    string SlotKey,
    double DeltaX,
    double DeltaY,
    double DurationFrames,
    string? Ease,
    IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// 이 라인의 이동 커맨드를 무대가 만질 수 있는 모양으로 펼친다.
///
/// 무엇이 이동인지는 <b>카탈로그의 모션 선언</b>만이 정한다 — 이름으로 추측하지 않으므로
/// 선언이 없는 커맨드는 여기 오지 않고 지금처럼 텍스트·갤러리로만 편집된다.
/// 값 계산은 전부 <see cref="MotionInspection"/>이 하고 이 클래스는 고르기만 한다.
/// </summary>
internal static class StageMotionCues
{
    public static IReadOnlyList<StageMotionCue>? Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<MiniStageFoldLine> foldLines,
        IReadOnlyList<PresentationResultCommand>? lineCommands,
        Ked.Presentation.Core.StageReducerTuning? tuning)
    {
        if (tuning is null || lineCommands is null || lineCommands.Count == 0)
        {
            return null;
        }

        var cues = new List<StageMotionCue>();

        foreach (PresentationResultCommand command in lineCommands)
        {
            if (catalog.Find(command.DefinitionId) is not { Motion: not null } definition)
            {
                continue;
            }

            // 시작 자리는 "이 커맨드 직전"의 상태다 — 접힌 자리에서 빼서 구하지 않는다.
            Ked.Presentation.Core.StageState? before = CoreStageFold
                .Fold(catalog, setupCommands, foldLines, tuning, stopBeforeCommandId: command.CommandId)
                .CoreState;

            if (before is null ||
                MotionInspection.Inspect(definition, command, before, tuning) is not { } segment)
            {
                continue;
            }

            cues.Add(new StageMotionCue(
                segment.CommandId,
                command.DefinitionId,
                segment.SlotKey,
                segment.Delta.X,
                segment.Delta.Y,
                segment.DurationFrames,
                segment.Ease,
                command.Arguments));
        }

        return cues.Count > 0 ? cues : null;
    }
}

/// <summary>공유 무대 프리뷰에 밀어 넣는 요청 하나. 폴드는 호출자가 이미 끝냈다.</summary>
/// <param name="HasPresentation">false면 연출 공급이 없는 것 — 오류가 아니라 화자만 표시한다.</param>
/// <param name="LineIndex">문서에서 선택 라인의 0기준 위치. 없으면 -1 — 창 하단 표시용.</param>
/// <param name="Notice">선택 라인이 발행본에 없다는 등 호출자가 덧붙이는 알림.</param>
/// <param name="ChoiceOptions">
/// 에피소드 끝의 챕터 간선 선택지(<see cref="MiniStagePreview.TryPresentEpisodeChoices"/>가 채운다). 대사창 대신
/// 화면 중앙에 버튼 묶음으로 제시된다 — 런타임의 선택지 제시 근사. 줄 요청에서는 비어 있다.
/// </param>
/// <param name="CoreState">
/// 코어 리듀서가 접은 확정 무대 상태. 있으면 배치가 실제 좌표(리그 트리 + 샷)로
/// 그려지고, 없으면(tuning 미수입) 기존 균등 나열 근사 + "좌표 근사" 뱃지다.
/// </param>
internal sealed record MiniStagePreviewRequest(
    string ContextLabel,
    MiniStageState State,
    bool HasPresentation,
    string? SelectedLineId,
    string? SpeakerName,
    string? LineText,
    string? Notice = null,
    int LineIndex = -1,
    int LineCount = 0,
    StageEditContext? EditContext = null,
    IReadOnlyList<StatFold.StatValue>? Stats = null,
    IReadOnlyList<StageChoiceOption>? ChoiceOptions = null,
    Ked.Presentation.Core.StageState? CoreState = null,
    IReadOnlyList<BranchFlow.Block>? BranchBlocks = null,
    double TransitionSeconds = 0,
    IReadOnlyList<string>? AudioCues = null,
    IReadOnlyList<string>? AutoBranchBlocks = null,
    IReadOnlyList<PresentationResultCommand>? AudioCommands = null,
    IReadOnlyList<StageMotionCue>? MotionCues = null,
    IReadOnlyList<PresentationScriptRow>? ScriptRows = null,
    StageMotionPlan? MotionPlan = null,
    // 이 라인의 페이드(slotKey → 방향·초) — 불투명도는 라인 시계가 아니라
    // 제 duration으로 흐르고, fade_in이 적힌 슬롯은 기준선과 무관하게 등장으로 판정한다.
    // 항목 없는 슬롯의 가시성 변화(show 등)는 즉시다.
    IReadOnlyDictionary<string, StageFade>? Fades = null,
    // 챕터 런의 현재 스탯 — 편집기가 아니라 프리뷰가 Render에서 얹는다
    // (챕터 런 상태의 주인이 프리뷰라, 요청을 짓는 편집기는 이 값을 모른다).
    IReadOnlyList<ChapterRunStatValue>? ChapterStats = null);

/// <summary>
/// 무대 프리뷰 판(중앙 탭) — 좌측 대본 터미널 + 우측 무대.
/// 무대 그리기는 <see cref="StageSceneView"/>가 하고, 이 판은 뱃지·알림·재생과
/// 대본 패널의 신호 배선을 맡는다.
/// </summary>
public partial class MiniStagePreview : UserControl
{
    private AuthoringSession? _session;
    private MiniStagePreviewRequest? _current;
    private readonly StageSceneView _scene = new();
    private PreviewAssetLibrary? _renderedLibrary;
    private readonly Avalonia.Threading.DispatcherTimer _playbackTimer;

    /// <summary>재생 진행 모델. 재생 컨트롤과 무대 클릭이 전부 이 하나를 본다.</summary>
    internal StagePlayback Playback { get; } = new();

    /// <summary>무대 뷰 — 조절창의 주인. 검증이 담기 모드를 켜는 손잡이이기도 하다.</summary>
    internal StageSceneView Scene => _scene;

    /// <summary>이전/다음·재생 자동 진행. delta(-1/+1)를 활성 편집기가 소화한다.</summary>
    internal event Action<int>? LineMoveRequested;

    /// <summary>
    /// 문서 끝에서 다음 노드로 이어 재생 — 활성 편집기가 실행 출구를 따라
    /// 노드 전환에 성공하면 true를 돌려준다.
    /// </summary>
    internal Func<bool>? NodeExitRequested;

    /// <summary>
    /// 새 전체 재생이 지금 줄에서 섰다 — 활성 편집기가 그 줄 앞의 <c>&lt;&lt;detour&gt;&gt;</c>를 먼저
    /// 다녀온다. 나갔으면 true(무대는 분기 카드로 넘어간다), 아니면 지금 줄이 그대로 재생된다.
    /// </summary>
    internal Func<bool>? PlayEntryRequested;

    /// <summary>무대 직접 조작이 편집을 만들었다 — 편집기가 다시 그린다.</summary>
    internal event Action? ManipulationApplied;

    /// <summary>대본 패널의 대사 행 클릭 — 활성 편집기가 그 라인을 선택한다.</summary>
    internal event Action<string>? LineSelectRequested;

    /// <summary>
    /// detour에서 돌아왔다 — <b>지금 활성인</b> 편집기가 그 줄을 고르고 한 줄
    /// 나아간다. 셸이 라우팅하는 이유는 하나다: 나가는 노드의 편집기와 돌아가는 노드의
    /// 편집기가 서로 다를 수 있다(연출 노드 ↔ 커스텀 대사 노드).
    /// </summary>
    /// <param name="atLine">true = 그 줄<b>부터</b>(줄 앞 분기에서 돌아옴), false = 그 줄 <b>다음</b>부터(갈래 출구에서 돌아옴).</param>
    internal event Action<string, bool>? DetourResumeRequested;

    /// <summary>편집기가 복귀를 확정한 뒤 부른다 — 선택은 이미 그 노드로 옮겨져 있다.</summary>
    internal void RequestDetourResume(string lineId, bool atLine) => DetourResumeRequested?.Invoke(lineId, atLine);

    // ── 에피소드 끝 선택지 ──
    //
    // 전이 규칙 v9①: 에피소드 대본이 끝나면 챕터 `간선`의 선택지가 제시된다(뜨는 순서 =
    // `간선` 시트의 행 순서). 프리뷰 재생도 노드 끝에서 멈추지 않고 그 선택지를 세운다.
    // 간선의 원천은 셸이 공급하는 챕터 목록 하나(SupplyChapters)이고, 고르면 씬 선택기와
    // 같은 길(SceneChosen → EnsurePresentationChannel)로 도착 에피소드가 무대에 오른다.

    /// <summary>지금 무대에 선 요청 — 테스트가 제시된 선택지를 들여다보는 창이다.</summary>
    internal MiniStagePreviewRequest? CurrentRequest => _current;

    /// <summary>
    /// 재생을 이 대사 노드로 넘긴다 — 씬 선택기가 하는 그 일이다(연출 채널은 셸이 세운다).
    /// 노드가 없으면 false — 부른 쪽이 재생을 멈춘다.
    /// </summary>
    internal bool RequestSceneChange(string nodeId)
    {
        if (_session?.Project.FindNode(nodeId) is not DialogueNode)
        {
            return false;
        }

        Playback.OnNodeSwitch();
        SceneChosen?.Invoke(nodeId);
        return true;
    }

    /// <summary>
    /// 나갈 곳 없는 노드 끝 (detour 복귀·자유 씬 다음 자리 모두 소진) — 이 노드가 에피소드라면
    /// 챕터 간선의 선택지를 무대 중앙에 세우고 클릭을 기다린다. 세울 것이 없으면 false —
    /// 나가는 간선이 없는 에피소드는 챕터 종료이므로 지금처럼 멈추는 것이 맞다.
    /// </summary>
    internal bool TryPresentEpisodeChoices(string? nodeId)
    {
        if (_session is null || _current is null || nodeId is null)
        {
            return false;
        }

        if (PlayingDialogueOf(nodeId) is not { MarkedEpisodeId: { } episodeId } source)
        {
            return false; // 커스텀 씬의 끝은 에피소드 끝이 아니다
        }

        StoryFile? file = _session.Project.FindFileContainingNode(source.Id);
        ChapterGraphModel? chapter = file is null
            ? null
            : _chapters.FirstOrDefault(entry =>
                string.Equals(entry.ChapterId, file.Name, StringComparison.Ordinal))?.Model;

        if (chapter is null)
        {
            return false; // 챕터 판이 아니거나 챕터를 못 찾았다 — 선택지를 지어낼 원천이 없다
        }

        ChapterRunState run = EnsureChapterRunFor(chapter);
        ChapterRunAdvance? advance = run.Resolve(episodeId);

        if (advance is null)
        {
            StageChoiceOption[] broken = chapter.Edges
                .Where(edge => string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal))
                .Where(edge => !string.IsNullOrWhiteSpace(edge.OptionLabel))
                .Select(edge => BuildAuthoringFallbackOption(run, chapter, file!, source, edge))
                .ToArray();

            if (broken.Length == 0)
            {
                return false;
            }

            _current = _current with { ChoiceOptions = broken };
            Render();
            Playback.WaitForChoiceInput();
            return true;
        }

        if (advance.Kind == Ked.Progression.ChapterAdvanceKind.ChapterEnded)
        {
            return false;
        }

        if (advance.Kind == Ked.Progression.ChapterAdvanceKind.AutoAdvance)
        {
            ChapterEdge automatic = advance.Options[0].Edge;
            ChooseEpisodeEdge(chapter, file!, source, automatic);
            return true;
        }

        // 미리 정한 루트 — 이 갈림의 선택이 루트에 있고 지금 고를 수 있으면, 선택지를 띄워 그것을 비추고
        // 잠깐 뒤에 스스로 고른다(루트 탭에서 이미 정해 둔 선택이다). 루트에 없거나 지금 스탯으로 막혔으면 사람이 고른다 — 이유는 상태줄에.
        PreviewRoute? route = ActiveRoute(chapter.ChapterId);
        PreviewRouteChoice? pick = route?.ChoiceAt(episodeId);
        string? wanted = pick?.OptionLabel;
        ChapterRunOption? planned = wanted is null
            ? null
            : advance.Options.FirstOrDefault(option =>
                string.Equals(option.Edge.OptionLabel, wanted, StringComparison.Ordinal));
        bool autoPick = planned is { IsSelectable: true };

        if (pick is not null && !autoPick)
        {
            _session.SetStatus(planned is null
                ? $"루트 '{route!.Name}'의 '{pick.OptionLabel}'이 {episodeId}에 없습니다(숨김 조건?) — 직접 고르세요."
                : $"루트 '{route!.Name}'의 '{pick.OptionLabel}'이 지금 스탯으로 잠겨 있습니다 — 직접 고르세요.");
        }

        StageChoiceOption[] options = advance.Options
            .Select(option => BuildEpisodeOption(chapter, file!, source, option, highlight: autoPick && ReferenceEquals(option, planned)))
            .ToArray();

        _current = _current with { ChoiceOptions = options };
        Render();
        Playback.WaitForChoiceInput();

        if (autoPick)
        {
            _session.SetStatus($"루트 '{route!.Name}'대로 — {episodeId}에서 '{pick!.OptionLabel}'을 고릅니다.");
            ArmAutoChoice(options, () => ChooseEpisodeEdge(chapter, file!, source, planned!.Edge));
        }

        return true;
    }

    // ── 루트 자동 선택 ─────────────────────────────────────────────────────

    /// <summary>선택지가 선 뒤 루트대로 고르기까지의 여유 — 무엇이 골라지는지 눈에 들어올 만큼만. 배속을 따른다.</summary>
    private const double AutoChoiceDelaySeconds = 0.9;

    private Avalonia.Threading.DispatcherTimer? _autoChoice;

    /// <summary>
    /// 띄운 선택지(<paramref name="presented"/>)가 그대로 서 있고 재생이 계속 중일 때만 고른다 — 그 사이 사람이 눌렀거나
    /// (화면이 바뀜) 일시정지했으면 고르지 않는다.
    /// </summary>
    private void ArmAutoChoice(StageChoiceOption[] presented, Action choose)
    {
        CancelAutoChoice();

        var timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoChoiceDelaySeconds / Math.Max(0.25, Playback.SpeedMultiplier))
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (!ReferenceEquals(_autoChoice, timer))
            {
                return;
            }

            _autoChoice = null;

            if (!Playback.IsPlaying || !ReferenceEquals(_current?.ChoiceOptions, presented) || _session is null)
            {
                return;
            }

            UiGuard.Run(_session, "루트 자동 선택", choose);
        };

        _autoChoice = timer;
        timer.Start();
    }

    private void CancelAutoChoice()
    {
        _autoChoice?.Stop();
        _autoChoice = null;
    }

    // ── 챕터 런 상태 — 선택을 따라가며 스탯 HUD가 챕터 단위로 누적된다.
    //    시작은 `스탯` 시트 초기값, 변화는 간선 커밋뿐, 새 전체 재생이
    //    처음으로 되돌린다(detour 이력과 같은 수명 — Playback.RunReset 하나가 청소 신호다).

    private ChapterRunState? _chapterRun;

    /// <summary>전체 재생이 지나온 길 — 지도의 덧칠. 런과 같은 수명이다(RunReset이 비운다).</summary>
    private readonly ChapterRunTrail _trail = new();

    /// <summary>지도(셸의 왼쪽 기둥)가 다시 그려야 한다 — 길 · 자리 · 루트가 바뀌었다.</summary>
    internal event Action? MinimapChanged;

    /// <summary>지도가 그릴 것 전부 — 챕터 지도(자리는 director의 것) · 지나온 길 · 지금 카드 · 루트의 예정 길(분기 판정 포함).</summary>
    internal (ChapterMinimap? Map, ChapterRunTrail Trail, string? CurrentDialogueId, PreviewRouteWalker.Result? Plan) MinimapState()
    {
        ChapterGraphModel? model = ActiveChapterModel();

        if (model is null || _session is null)
        {
            return (null, _trail, CurrentComboSceneId(), null);
        }

        RouteSurvey survey = RouteSurvey.Build(model, _session.Project, _session.Definition, ActiveRoute(model.ChapterId));

        return (ChapterMinimap.Build(model, _session.Project), _trail, CurrentComboSceneId(), survey.Walk);
    }

    private ChapterGraphModel? ActiveChapterModel() =>
        _session?.ActiveFile?.Name is { } chapterId
            ? _chapters.FirstOrDefault(entry => string.Equals(entry.ChapterId, chapterId, StringComparison.Ordinal))?.Model
            : null;

    private void RefreshMinimap() => MinimapChanged?.Invoke();

    /// <summary>
    /// 지도의 점을 눌렀다 — 드롭다운과 같은 길로 그 씬이 무대에 오른다. 루트가 그 점까지 닿으면 런이 <b>그 자리의 스탯</b>으로
    /// 선다 — 어느 에피소드부터 시작하든, 중간 점을 눌러도 무리 없이 이어진다.
    /// </summary>
    internal void EnterSceneFromMap(string dialogueNodeId)
    {
        if (_session?.Project.FindDialogue(dialogueNodeId) is not { } node)
        {
            return;
        }

        StartTrailAt(node);
        RebuildScenePicker(dialogueNodeId);
        SceneChosen?.Invoke(dialogueNodeId);
    }

    /// <summary>
    /// 분기 카드에서 돌아왔다 — 길에 적는다. 줄을 하나도 재생하지 않았으면 첫머리 조건에 막힌 것이다(빈 네모로 보인다).
    /// </summary>
    internal void NoteDetourReturn(string callerNodeId, string branchNodeId, bool played)
    {
        // 재생 노드는 연출 노드다 — 그 노드가 말하는 카드로 푼다(카드 Id로만 찾으면 조용히 아무것도 안 적힌다).
        if (_session is null ||
            PlayingDialogueOf(callerNodeId) is not { } caller ||
            PlayingDialogueOf(branchNodeId) is not { } branch)
        {
            return;
        }

        _trail.Detoured(EpisodeNaming.EpisodeIdOf(caller), EpisodeNaming.EpisodeIdOf(branch), played);
        RefreshMinimap();
    }

    /// <summary>이 챕터의 런 하나 — 챕터가 바뀌면 새로 선다(챕터를 넘는 스탯은 없다).</summary>
    private ChapterRunState EnsureChapterRunFor(ChapterGraphModel chapter)
    {
        if (_chapterRun is { } run && string.Equals(run.ChapterId, chapter.ChapterId, StringComparison.Ordinal))
        {
            return run;
        }

        // 미리 정한 루트의 시작 스탯으로 선다 — 없으면 시트 초기값.
        _chapterRun = new ChapterRunState(chapter, ActiveRoute(chapter.ChapterId)?.Stats);
        PublishRunStats();
        return _chapterRun;
    }

    /// <summary>런의 스탯을 세션에 올린다 — 대본의 첫머리 조건(stat("키"))이 이 값으로 판정된다.</summary>
    private void PublishRunStats()
    {
        if (_session is not null)
        {
            _session.ChapterStatValues = _chapterRun?.AsConditionValues()
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>이 챕터의 활성 루트 — 없으면 null(갈림마다 묻는다).</summary>
    private PreviewRoute? ActiveRoute(string chapterId) => _session?.Editor.ActivePreviewRoute(chapterId);

    /// <summary>활성 판(=챕터)의 런 — HUD가 매 렌더에 읽는다. 챕터 판이 아니면 null.</summary>
    private ChapterRunState? ActiveChapterRun()
    {
        if (_session?.ActiveFile?.Name is not { } chapterId)
        {
            return null;
        }

        ChapterGraphModel? model = _chapters
            .FirstOrDefault(entry => string.Equals(entry.ChapterId, chapterId, StringComparison.Ordinal))
            ?.Model;

        return model is null ? null : EnsureChapterRunFor(model);
    }

    /// <summary>
    /// 간선 하나 → 버튼 하나. 관문은 <b>지금 스탯으로 실제 판정한다</b>(판정은 커밋 전 값) —
    /// 게임과 같은 <c>ChapterTransition.Resolve</c> 결과를 그대로 받는다. 표시조건 미달은
    /// 목록에서 빠지고 해금조건 미달만 잠긴 채 남는다. 다른 갈래를 억지로 확인하려면
    /// 씬 선택기가 뒷길이다.
    /// </summary>
    /// <param name="highlight">루트가 고르기로 한 선택지 — 테두리를 밝혀 "이것이 곧 골라진다"를 보인다.</param>
    private StageChoiceOption BuildEpisodeOption(
        ChapterGraphModel chapter,
        StoryFile file,
        DialogueNode source,
        ChapterRunOption option,
        bool highlight = false)
    {
        ChapterEdge edge = option.Edge;
        if (!option.IsSelectable)
        {
            string reason = string.IsNullOrWhiteSpace(option.LockedReason)
                ? $"〔해금조건 미달 · {edge.ConditionLabel}〕"
                : $"— {option.LockedReason}";

            return new StageChoiceOption(
                $"🔒 {edge.OptionLabel} {reason}", IsSelected: false, IsDisabled: true);
        }

        return new StageChoiceOption(
            (highlight ? "▶ " : string.Empty) + edge.OptionLabel!,
            IsSelected: highlight,
            Choose: () => ChooseEpisodeEdge(chapter, file, source, edge));
    }

    /// <summary>
    /// 코어가 싣을 수 없는 편집 중 모형(예: 아직 Episodes 행이 없음)만을 위한 오류 표시.
    /// 발행 가능한 모델의 진행 판정에는 이 경로가 쓰이지 않는다.
    /// </summary>
    private StageChoiceOption BuildAuthoringFallbackOption(
        ChapterRunState run,
        ChapterGraphModel chapter,
        StoryFile file,
        DialogueNode source,
        ChapterEdge edge)
    {
        ChapterGateVerdict visible = run.Judge(edge.VisibleConditionLabel);
        ChapterGateVerdict unlock = run.Judge(edge.ConditionLabel);

        if (visible == ChapterGateVerdict.Broken || unlock == ChapterGateVerdict.Broken)
        {
            string broken = visible == ChapterGateVerdict.Broken
                ? edge.VisibleConditionLabel!
                : edge.ConditionLabel!;
            return new StageChoiceOption(
                $"{edge.OptionLabel} 〔조건 해석 불가 · {broken}〕", IsSelected: false, IsDisabled: true);
        }

        if (visible == ChapterGateVerdict.Blocked)
        {
            return new StageChoiceOption(
                $"{edge.OptionLabel} 〔표시조건 미달 · {edge.VisibleConditionLabel}〕",
                IsSelected: false,
                IsDisabled: true);
        }

        if (unlock == ChapterGateVerdict.Blocked)
        {
            string reason = string.IsNullOrWhiteSpace(edge.LockedMessage)
                ? $"〔해금조건 미달 · {edge.ConditionLabel}〕"
                : $"— {edge.LockedMessage}";
            return new StageChoiceOption(
                $"🔒 {edge.OptionLabel} {reason}", IsSelected: false, IsDisabled: true);
        }

        return new StageChoiceOption(
            edge.OptionLabel!,
            IsSelected: false,
            Choose: () => ChooseEpisodeEdge(chapter, file, source, edge));
    }

    /// <summary>
    /// 재생 노드 → 그 노드가 말하는 대사 노드. 연출 노드는 자기 공급 연결의 대상
    /// (<c>PresentationSupply</c>)이 그 대사다 — 채널 상태줄이 보는 것과 같은 자리 하나다.
    /// </summary>
    private DialogueNode? PlayingDialogueOf(string nodeId)
    {
        StoryProject project = _session!.Project;

        return project.FindNode(nodeId) switch
        {
            DialogueNode dialogue => dialogue,
            PresentationNode presentation => project.Links.FirstOrDefault(link =>
                    link.Kind == NodeLinkKind.PresentationSupply &&
                    link.IsEnabled &&
                    string.Equals(link.SourceNodeId, presentation.Id, StringComparison.Ordinal))
                is { } supply
                    ? project.FindNode(supply.TargetNodeId) as DialogueNode
                    : null,
            _ => null,
        };
    }

    /// <summary>
    /// 선택지 클릭 — 도착 에피소드의 노드로 씬을 넘긴다.
    ///
    /// 간선에 매달린 연출 씬은 없다 — 재생 순서는 <b>에피소드 한 칸</b>이 말한다.
    /// <b>간선을 타는 순간 그 간선의 `스탯변화`가 1회 커밋된다</b>(전이 규칙
    /// v9① 그대로). 커밋은 이동이 성사될 때만이다 — 도착이 없어 제자리에 남으면 스탯도 그대로다.
    /// </summary>
    private void ChooseEpisodeEdge(
        ChapterGraphModel chapter, StoryFile file, DialogueNode source, ChapterEdge edge)
    {
        if (_session is null)
        {
            return;
        }

        CancelAutoChoice();   // 사람이 먼저 눌렀든 자동이든, 고르는 것은 한 번이다.

        // EpisodeId는 챕터 안에서만 유일하다 — 같은 판에서만 찾는다.
        //
        // 다른 세 자리(대본 탭·개명·삭제)와 같은 한 함수다 — 표식이 없으면 이름을 뒷길로 쓴다.
        //    여기만 <b>표식으로만</b> 찾으면 표식 없는 구판 카드에서 <b>저쪽은 찾고 프리뷰만
        //    못 찾아</b> "아직 판에 노드가 없습니다"로 멈춘다.
        DialogueNode? target = EpisodeNaming.CardFor(_session.Project, file.Name, edge.ToEpisodeId);

        if (target is null)
        {
            // 아직 동기화 전인 에피소드 — 판에 카드가 없으니 넘어갈 곳도 없다.
            _session.SetStatus(
                $"'{edge.ToEpisodeId}' 에피소드는 아직 판에 노드가 없어 이어 재생할 수 없습니다 — " +
                "ked-story-director에서 그 에피소드의 대사 카드를 세워 주세요.");
            return;
        }

        // 제시 시점의 런이 아니라 지금의 런에 커밋한다 — 제시와 클릭 사이에 새 재생이
        // 런을 되돌렸을 수 있다(그 사이 화면의 선택지는 옛 판의 것이지만, 커밋만은
        // 지금 서 있는 판의 것이어야 잃어버린 스탯이 생기지 않는다).
        EnsureChapterRunFor(chapter).Commit(edge);
        PublishRunStats();
        _trail.Follow(edge);

        RequestSceneChange(target.Id);
    }

    /// <summary>
    /// 씬 선택기에서 대사 노드 하나를 골랐다 — MainWindow가 연출 채널을
    /// 확보하고(EnsurePresentationChannel) 그 채널을 연다. 값은 대사 노드 Id다.
    /// </summary>
    internal event Action<string>? SceneChosen;

    private bool _updatingSceneCombo;

    // ── 챕터 고르기 — 챕터 드롭다운. 목록의 원천은 셸이 공급하는 하나(SupplyChapters — 세션이
    //    프로젝트에서 편다), 선택은 ActiveFile 하나를 비춘다.

    /// <summary>셸이 공급한 챕터 목록 — 이 뷰는 챕터를 직접 읽지 않는다.</summary>
    private IReadOnlyList<ChapterEntry> _chapters = Array.Empty<ChapterEntry>();

    /// <summary>선택을 비추는 동안에는 SelectionChanged가 선택을 바꾸지 않게 한다.</summary>
    private bool _syncingChapter;

    internal void SupplyChapters(IReadOnlyList<ChapterEntry> entries)
    {
        _chapters = entries;
        RefreshChapterCombo();
        RefreshMinimap();
    }

    /// <summary>
    /// 에셋 탐색기 자리. 자리는 이 뷰의 것,
    /// 내용은 셸의 것이다.
    /// </summary>
    internal ContentControl AssetExplorerHost => AssetHost;

    /// <summary>챕터 목록과 지금 활성인 판을 콤보에 비춘다 — 판 이름 = ChapterId (1:1).</summary>
    private void RefreshChapterCombo()
    {
        _syncingChapter = true;

        try
        {
            // 씬은 판에 산다 — 판이 없는 챕터에는 무대에 올릴 것이 없다.
            string[] ids = _session is null
                ? []
                : _chapters
                    .Where(entry => EpisodeNaming.BoardOf(_session.Project, entry.ChapterId) is not null)
                    .Select(entry => entry.ChapterId)
                    .ToArray();

            if (!ids.SequenceEqual(
                    (ChapterCombo.ItemsSource as IEnumerable<string>) ?? [], StringComparer.Ordinal))
            {
                ChapterCombo.ItemsSource = ids;
            }

            ChapterCombo.SelectedItem = _session?.ActiveFile?.Name is { } name && ids.Contains(name)
                ? name
                : null;
        }
        finally
        {
            _syncingChapter = false;
        }
    }

    private readonly PresentationScriptPanel _script = new();

    /// <summary>
    /// 선택 커맨드 하나의 Inspector 공급자 —
    /// 활성 편집기가 커맨드 행 + 수치 조절을 한 판으로 짓는다. MainWindow가 배선한다.
    /// </summary>
    internal Func<string, Control?>? CommandDetailProvider { get; set; }

    /// <summary>
    /// 연출 추가 콘솔 공급자 — (lineId, setup, close)를 받아 콘솔 한 판을 짓는다.
    /// 터미널 우클릭 [＋ 연출 추가]가 아래쪽에 펼치는 대신 플라이아웃으로 띄운다.
    /// MainWindow가 배선한다.
    /// </summary>
    internal Func<string?, bool, Action, Control?>? AddConsoleProvider { get; set; }

    /// <summary>터미널의 Setup 구획이 선택돼 있다 — 작업대가 라인 대신 Setup을 보인다.</summary>
    private bool _setupSelected;

    /// <summary>
    /// 복사한 커맨드(복사/붙여넣기 + Ctrl+C/V) — 결과 행의 해석된
    /// 값을 담으므로 프리셋 참조 없이 자립한다. 정적이라 노드를 오가며 붙일 수 있다.
    /// </summary>
    private static (string DefinitionId, Dictionary<string, string> Arguments, string? Note)? _commandClipboard;

    public MiniStagePreview()
    {
        InitializeComponent();

        SceneHost.Content = _scene;
        ScriptHost.Content = _script;

        // 고른 챕터의 판을 활성으로 — 씬 선택기가 그 판의 씬만 담는다.
        // 주의: 콤보를 비추는 중에는 안 돈다(_syncingChapter) — 비추는 일이 바꾸는 일이 되면
        //    서로를 부른다.
        // 판을 <b>찾기만</b> 한다. 원고는 ked-story-director의 것이라 여기서 판을
        //    세우면 저장되지 않는 판이 화면에만 선다 — 판이 없는 챕터는 목록에 오르지 않는다.
        ChapterCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingChapter || _session is null ||
                ChapterCombo.SelectedItem is not string chapterId)
            {
                return;
            }

            _session.SelectFile(EpisodeNaming.BoardOf(_session.Project, chapterId)?.Id);
        };

        // 목록은 열 때마다 다시 짓는다 — 노드 생성·개명·동기화를 따로 구독하지 않아도
        // 여는 순간이 곧 최신이다.
        SceneCombo.DropDownOpened += (_, _) => RebuildScenePicker(CurrentComboSceneId());
        SceneCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingSceneCombo &&
                SceneCombo.Tag is List<DialogueNode> scenes &&
                SceneCombo.SelectedIndex >= 0 &&
                SceneCombo.SelectedIndex < scenes.Count)
            {
                StartTrailAt(scenes[SceneCombo.SelectedIndex]);
                SceneChosen?.Invoke(scenes[SceneCombo.SelectedIndex].Id);
            }
        };
        _script.LineClicked += lineId =>
        {
            // 라인 선택은 Setup 선택을 걷는다. 같은 라인 재클릭이라 편집기가 다시 밀지
            // 않아도 작업대 전환은 보여야 하므로 여기서도 한 번 그린다.
            _setupSelected = false;
            LineSelectRequested?.Invoke(lineId);
            Render();
        };
        _script.SetupClicked += () =>
        {
            _setupSelected = true;
            Render();
        };
        // 커맨드 선택 = Inspector 전환 — 점은 선택이 아니라 켜고 끄기다.
        _script.CommandSelected += _ =>
        {
            Render();
        };
        _script.CommandToggleRequested += ApplyScriptCommandToggle;
        _script.CommandMoveRequested += ApplyScriptCommandMove;
        _script.CommandRemoveRequested += ApplyScriptCommandRemove;
        // 우클릭 메뉴 + Ctrl+C/V/D.
        _script.AddCommandRequested += OnAddCommandRequested;
        _script.CommandCopyRequested += CopyScriptCommand;
        _script.CommandDuplicateRequested += ApplyScriptCommandDuplicate;
        _script.CommandPasteRequested += ApplyScriptCommandPaste;
        // 조절창은 무대 오른쪽 붙박이 기둥이다 — 무대 뷰가 짓고
        // 여기가 자리를 준다. 이후 갱신은 무대가 다시 그릴 때마다 스스로 한다.
        ConsoleHost.Content = _scene.BuildDockedConsole();

        _script.CommandPinRequested += PinQuickCommand;
        // 조절창의 [＋ 이 라인 통째로]도 같은 담기 함수로 들어온다 — 입구는 둘, 규칙은 하나.
        _scene.QuickPinRequested += PinQuickCommands;
        _script.HasClipboardCommand = () => _commandClipboard is not null;
        // 담기 모드가 켜지고 꺼졌다 — 터미널의 ★를 다시 그린다. 조절창은 안 건드린다
        // (Render는 캔버스와 터미널만 만지고 팝업은 제 자리에 남는다).
        _scene.QuickEditModeChanged += Render;
        _scene.ManipulationApplied += () => ManipulationApplied?.Invoke();
        // 갈래 선택은 편집이 아니지만 다시 접어야 한다 — 같은 재렌더 경로를 탄다.
        _scene.BranchSelectionChanged += () => ManipulationApplied?.Invoke();

        // 재생 배선 — 시간의 원천은 이 타이머 하나, 라인 이동은 기존 선택 경로다.
        Playback.MoveRequested += delta => LineMoveRequested?.Invoke(delta);
        Playback.NodeExitRequested = () => NodeExitRequested?.Invoke() == true;
        Playback.RunStarted += () => PlayEntryRequested?.Invoke();
        // 챕터 런 스탯은 detour 이력과 같은 수명이다 — 새 전체 재생이 처음값으로 되돌린다.
        //
        // "처음값"은 <b>지금 무대에 선 씬의</b> 것이다: 지도 점을 눌러 중간에서 들어왔으면 루트가 그 자리까지
        // 걸어온 스탯(StartTrailAt과 같은 규칙). 런을 비워 버리면 중간에서 ▶를 누를 때 시작 스탯으로 되돌아가
        // 루트의 선택지가 숨김·잠김에 걸려 멈추고, 분기 카드의 첫머리 조건도 평가 불가가 된다.
        Playback.RunReset += () =>
        {
            CancelAutoChoice();
            _trail.Clear();

            if (CurrentComboSceneId() is { } sceneId &&
                _session?.Project.FindDialogue(sceneId) is { MarkedEpisodeId: not null } scene)
            {
                StartTrailAt(scene);
            }
            else
            {
                _chapterRun = null;
                PublishRunStats();
            }

            RefreshMinimap();
        };
        _scene.PlaybackAdvance = Playback.TryAdvanceByInput;
        // 재생 컨트롤은 타임라인을 사이에 두고 양 끝으로 갈린다 —
        // 왼쪽은 지금 다듬는 라인의 것, 오른쪽 끝은 가끔 쓰는 것(노드 전체 재생·배속).
        PlaybackHost.Content = StagePlaybackControls.BuildLeading(Playback);
        LineStepHost.Content = StagePlaybackControls.BuildLineStep(Playback);
        PlaybackTrailingHost.Content = StagePlaybackControls.BuildTrailing(Playback);

        // 재생 배율은 툴 편의 설정 — 세션을 넘어 기억된다.
        Playback.SpeedMultiplier = AppSettingsService.LoadPlaybackSpeed();
        double savedSpeed = Playback.SpeedMultiplier;
        Playback.StateChanged += () =>
        {
            if (Math.Abs(Playback.SpeedMultiplier - savedSpeed) > 0.0001)
            {
                savedSpeed = Playback.SpeedMultiplier;
                AppSettingsService.SavePlaybackSpeed(savedSpeed);
            }
        };

        _playbackTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playbackTimer.Tick += (_, _) => Playback.Tick(0.05);
        Playback.StateChanged += () => _playbackTimer.IsEnabled = Playback.IsPlaying;

        // 타자기: 글자 수 변화는 대사창 텍스트 한 곳만 갱신한다 — 전체 재렌더 없이.
        Playback.TypingProgress += SyncTypewriter;

        // 전이: 진행도 변화는 무대 자리 보간만 갱신한다 — 전체 재렌더 없이.
        Playback.TransitionChanged += SyncTransition;

        // 오디오 실재생: 정지·일시정지는 소리도 멈추고, 재생 시작은 현재 라인의
        // 소리부터 낸다 — BGM 재개는 곡 처음부터라는 근사(진행 위치 기억 없음).
        Playback.StateChanged += () =>
        {
            if (Playback.IsPlaying == _audioWasPlaying)
            {
                return;
            }

            _audioWasPlaying = Playback.IsPlaying;
            _audioFiredLineId = null;

            if (Playback.IsPlaying)
            {
                FireLineAudio(_current);
            }
            else
            {
                AudioPreview.StopAll();
            }
        };
    }

    private bool _audioWasPlaying;
    private string? _audioFiredLineId;

    /// <summary>
    /// 재생 중 새 라인에 도달했을 때 그 라인의 오디오 커맨드를 소리로 낸다.
    /// 같은 라인의 재요청(편집 반영·에셋 새로 고침)에는 다시 울리지 않는다.
    /// </summary>
    private void FireLineAudio(MiniStagePreviewRequest? request)
    {
        if (!Playback.IsPlaying || _session is null || request?.SelectedLineId is not { } lineId ||
            string.Equals(_audioFiredLineId, lineId, StringComparison.Ordinal))
        {
            return;
        }

        _audioFiredLineId = lineId;

        if (request.AudioCommands is { Count: > 0 } audio)
        {
            AudioCueRouter.Fire(_session, audio);
        }
    }

    private void SyncTypewriter()
    {
        int? visible = Playback.VisibleCharacters;
        _scene.SetDialogueVisibleCharacters(visible);

    }

    private void SyncTransition()
    {
        double? progress = Playback.TransitionProgress;
        _scene.SetTransitionProgress(progress);

    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
        _scene.Attach(session);
        RebuildScenePicker(null);

        // 에셋 루트가 바뀌면(탐색기에서 지정·새로 고침·되돌리기) 같은 요청을 새 에셋으로 다시 그린다.
        session.Changed += (_, _) =>
        {
            if (_current is not null && !ReferenceEquals(_renderedLibrary, session.AssetLibrary))
            {
                Render();
            }
        };
    }

    /// <summary>무대 뷰의 수치 조절 내용 — Inspector 공급자(MainWindow 배선)가 조합해 쓴다.</summary>
    internal Control? BuildSceneInspector(PresentationResultCommand command) =>
        _scene.BuildInspectorContent(command);

    // 진단의 복사 통로는 <b>줄 드래그</b> 하나다 — 알림·미반영 줄은
    //   SelectableTextBlock이라 마우스로 긁어 가져간다. 챕터 그래프의 [보고 복사]는
    //   그쪽 검증 보고의 것이고 이것과 무관하다.

    // ── 씬 선택기 ──────────────────────────────────────────────

    /// <summary>지금 콤보가 가리키는 대사 노드 Id — 목록을 다시 지어도 선택을 지킨다.</summary>
    private string? CurrentComboSceneId()
    {
        return SceneCombo.Tag is List<DialogueNode> scenes &&
            SceneCombo.SelectedIndex >= 0 &&
            SceneCombo.SelectedIndex < scenes.Count
                ? scenes[SceneCombo.SelectedIndex].Id
                : null;
    }

    /// <summary>
    /// 바깥(그래프 클릭 등)에서 씬이 바뀌었다 — 콤보를 따라오게 한다. 이벤트는 안 쏜다.
    /// null이면 선택만 비운다(대사·연출이 아닌 노드).
    /// </summary>
    internal void SetCurrentScene(string? dialogueNodeId) => RebuildScenePicker(dialogueNodeId);

    /// <summary>
    /// 연출할 수 있는 씬 목록 — <b>고른 챕터(활성 판)의 대사 노드만</b> 담는다.
    /// 프로젝트 전체를 담으면 챕터가 늘수록 목록이 섞인다. 에피소드(📄)가 먼저,
    /// 커스텀 씬(✎)이 뒤고, 무리 안에서는 <b>이름 순</b>이다 — 파일 순서는 판의 생성
    /// 순서라 찾는 사람의 눈에는 무작위다.
    /// </summary>
    /// <summary>씬 선택기(드롭다운 · 지도)로 들어갔다 — 이어진 길이 아니므로 거기서 길이 새로 선다.</summary>
    private void StartTrailAt(DialogueNode node)
    {
        if (node.MarkedEpisodeId is null)
        {
            return;
        }

        string episodeId = EpisodeNaming.EpisodeIdOf(node);
        _trail.Start(episodeId);

        // 루트가 이 점까지 닿으면 런을 그 자리의 스탯으로 세운다 — 앞을 안 거쳐도 뒤가 맞는다. 안 닿으면 시작 스탯 그대로.
        if (ActiveChapterModel() is { } model && _session is not null)
        {
            PreviewRoute? route = ActiveRoute(model.ChapterId);
            IReadOnlyDictionary<string, int>? stats = PreviewRouteWalker.Walk(model, route).StatsAt(model, episodeId);
            _chapterRun = new ChapterRunState(model, stats ?? route?.Stats);
            PublishRunStats();
        }
    }

    private void RebuildScenePicker(string? selectedDialogueId)
    {
        if (_session is null)
        {
            return;
        }

        List<DialogueNode> scenes = (_session.ActiveFile?.Nodes ?? [])
            .OfType<DialogueNode>()
            .OrderBy(node => node.MarkedEpisodeId is null ? 1 : 0)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ToList();

        _updatingSceneCombo = true;

        try
        {
            SceneCombo.ItemsSource = scenes
                .Select(node => $"{(node.MarkedEpisodeId is null ? "✎" : "📄")} {node.Name}")
                .ToList();
            SceneCombo.Tag = scenes;
            SceneCombo.SelectedIndex = selectedDialogueId is null
                ? -1
                : scenes.FindIndex(node =>
                    string.Equals(node.Id, selectedDialogueId, StringComparison.Ordinal));
        }
        finally
        {
            _updatingSceneCombo = false;
        }

        RefreshMinimap();
    }

    /// <summary>점 클릭 = 켜고 끄기 — 꺼진 커맨드도 행으로 남아 다시 켤 수 있다.</summary>
    private void ApplyScriptCommandToggle(PresentationResultCommand command, bool currentlyEnabled)
    {
        if (_session is null || _current?.EditContext is not { DisabledReason: null } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 켜고 끄기", () =>
        {
            _session.Editor.SetPresentationCommandEnabled(
                context.PresentationNodeId, command.CommandId, !currentlyEnabled);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>null이면 보여 줄 라인이 없는 상태다(노드 미선택 등).</summary>
    internal void Show(MiniStagePreviewRequest? request)
    {
        _current = request;
        Render();
    }

    /// <summary>드래그 이동 적용 — 편집 통로 하나(<see cref="ProjectEditor.MovePresentationCommand"/>), undo 한 번.</summary>
    private void ApplyScriptCommandMove(
        PresentationResultCommand command, string? targetLineId, int insertIndex)
    {
        if (_session is null || _current?.EditContext is not { Editable: true } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 자리 이동", () =>
        {
            _session.Editor.MovePresentationCommand(
                context.PresentationNodeId, command.CommandId, targetLineId, insertIndex);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>
    /// 작업대가 비었을 때의 안내 — <b>영역은 그대로 두고</b> 글자만 바꾼다 —
    /// 클릭마다 접혔다 펴지면 터미널 높이가 흔들린다. 이 높이를 바꾸는 것은
    /// 사람의 스플리터 드래그뿐이다.
    /// </summary>
    private static Control BuildEmptyDetailHint() => new TextBlock
    {
        Text = "터미널에서 커맨드를 클릭하면 여기에서 수치를 조절합니다. " +
            "우클릭 메뉴로 연출 추가·복사·붙여넣기(Ctrl+C/V/D)를 씁니다.",
        FontSize = 11,
        Opacity = 0.4,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };

    /// <summary>
    /// 우클릭 [＋ 연출 추가] — 그 자리(라인 또는 Setup)를 선택 상태로 맞추고 연출 추가
    /// 콘솔을 아래쪽에 펼치는 대신 플라이아웃으로 띄운다. 플라이아웃의 과녁은 터미널 패널 자신이다 —
    /// 선택 이동이 터미널을 다시 그려 우클릭한 행이 사라져도 콘솔은 제자리에 남는다.
    /// </summary>
    private void OnAddCommandRequested(string? lineId, bool setup)
    {
        if (setup)
        {
            _setupSelected = true;
        }
        else if (lineId is not null)
        {
            _setupSelected = false;

            if (!string.Equals(_current?.SelectedLineId, lineId, StringComparison.Ordinal))
            {
                LineSelectRequested?.Invoke(lineId);
            }
        }

        Render();

        var flyout = new Flyout();
        Control? console = AddConsoleProvider?.Invoke(setup ? null : lineId, setup, () => flyout.Hide());

        if (console is null)
        {
            return;
        }

        flyout.Content = console;
        flyout.ShowAt(_script, showAtPointer: true);
    }

    /// <summary>복사 = 결과 행의 해석된 값을 담는다 — 붙여넣은 커맨드는 원본과 독립이다.</summary>
    private void CopyScriptCommand(PresentationResultCommand command)
    {
        _commandClipboard = (
            command.DefinitionId,
            new Dictionary<string, string>(command.Arguments, StringComparer.Ordinal),
            command.Note);
        _session?.SetStatus("커맨드를 복사했습니다 — 붙여넣기(Ctrl+V)로 원하는 라인에 답니다.");
    }

    /// <summary>
    /// 터미널 행 우측 ★ — 이 커맨드를 [★ 자주 쓰는] 칩으로 담는다.
    ///
    /// 세 가지가 이 함수의 요구다:
    /// ① <b>인자를 통째로 복사한다</b> — 슬롯도 duration도 그대로("그대로 복사하도록 하는게
    ///    포인트"). 도구가 무엇을 남길지 고르지 않는다.
    /// ② <b>조절창을 닫지 않는다</b> — 담기는 <c>Content</c> 변경이라 셸이 화면을 다시
    ///    짓지 않고(<c>ProjectRefreshPlanner</c>), 팝업의 라이트 디스미스도 꺼져 있다.
    /// ③ <b>바로 보인다</b> — <c>RefreshConsole()</c>이 열린 판을 그 자리에서 다시 그린다.
    ///
    /// 이름은 묻지 않는다(흐름이 끊긴다). 정의 이름을 붙이되 <b>같은 이름이 이미 있으면
    /// 번호를 단다</b> — 같은 커맨드를 값만 달리해 담는 것이 이 판의 정상 쓰임이라 이름이
    /// 겹치는 것이 예외가 아니다. 고치는 자리는 조절창 [편집]의 이름 칸이다.
    /// </summary>
    /// <summary>담기 모드 행 툴팁의 문장 — 조절창이 정한 목적지를 그대로 옮긴다.</summary>
    private string QuickPinHint()
    {
        IReadOnlyList<StageQuickCommand> chips =
            _session?.Project.EffectiveQuickCommands ?? [];

        return _scene.QuickPinTarget is { } target && target >= 0 && target < chips.Count
            ? $"클릭하면 '{chips[target].DisplayName}'의 {chips[target].Steps.Count + 1}번째 단계로 담깁니다."
            : "클릭하면 [★ 자주 쓰는]에 새 칩으로 담습니다 (슬롯·시간 그대로).";
    }

    private void PinQuickCommand(PresentationResultCommand command) => PinQuickCommands([command]);

    /// <summary>
    /// 담기의 유일한 통로(묶음 칩) — 커맨드 목록 하나가 칩이 된다.
    /// 터미널 행 하나를 집는 것도, 조절창의 [＋ 이 라인 통째로]도 여기로 들어온다:
    /// 이름 짓기와 <b>담을 곳 판정</b>이 두 벌이 되면 두 입구가 다르게 굴기 시작한다.
    ///
    /// <b>담을 곳은 조절창이 정한다</b>(<see cref="StageSceneView.QuickPinTarget"/>):
    /// 편집 중에 펼쳐 놓은 칩이 있으면 그 뒤에 단계로 붙고, 없으면 새 칩이다.
    /// 규칙이 하나라 "이번 클릭이 어디로 가나"를 화면(안내 줄)이 늘 말할 수 있다.
    /// </summary>
    private void PinQuickCommands(IReadOnlyList<PresentationResultCommand> commands)
    {
        if (_session is null || commands.Count == 0)
        {
            return;
        }

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(_session.Definition);

        List<(PresentationCommandDefinition Definition, StageQuickStep Step)> steps = commands
            .Select(command => (Definition: catalog.Find(command.DefinitionId), Command: command))
            .Where(pair => pair.Definition is not null)
            .Select(pair => (pair.Definition!, new StageQuickStep(
                pair.Definition!.Id,
                new Dictionary<string, string>(pair.Command.Arguments, StringComparer.Ordinal))))
            .ToList();

        if (steps.Count == 0)
        {
            _session.SetStatus("카탈로그에 없는 커맨드는 담을 수 없습니다.");
            return;
        }

        IReadOnlyList<StageQuickCommand> chips = _session.Project.EffectiveQuickCommands;

        if (_scene.QuickPinTarget is { } target && target >= 0 && target < chips.Count)
        {
            string chipName = chips[target].DisplayName;
            int total = chips[target].Steps.Count + steps.Count;

            UiGuard.Run(_session, "자주 쓰는 데 담기", () =>
            {
                _session.Editor.AppendQuickCommandSteps(target, steps.Select(pair => pair.Step).ToList());
                _session.SetStatus($"'{chipName}'에 {steps.Count}개를 이어 담았습니다 (총 {total}단계).");
            });

            _scene.RefreshConsole();
            return;
        }

        // 새 칩의 이름 — 한 개면 그 커맨드 이름, 묶음이면 "첫 커맨드 외 N". 묻지 않는 이유는
        // 담는 순간 이름을 물으면 흐름이 끊기기 때문이다. 고치는 자리는 이름 칸이다.
        string displayName = _session.Editor.UniqueQuickCommandName(steps.Count == 1
            ? steps[0].Definition.DisplayName
            : $"{steps[0].Definition.DisplayName} 외 {steps.Count - 1}");

        // 중복 거르개가 <b>조용히</b> 돌면 안 된다. PinQuickCommand는 단계가
        //    똑같은 칩이 이미 있으면 담지 않고 그 자리를 돌려준다 — 그 답을 버리고 늘
        //    "담았습니다"라고 말하면, 담기지 않았는데 담겼다는 말에 사람은 [자주 쓰는]을
        //    뒤지게 된다. 거르개는 옳다(같은 것을 두 번 담는 것은 실수다). 보고가 그 답을 따른다.
        //
        //    묶음에 담자마자 대상을 놓고 같은 커맨드를 또 집으면 바로 이 길로 온다.
        int had = chips.Count;

        UiGuard.Run(_session, "자주 쓰는 데 담기", () =>
        {
            int at = _session.Editor.PinQuickCommand(new StageQuickCommand(
                displayName,
                steps.Select(pair => pair.Step).ToList()));

            if (at < had)
            {
                _session.SetStatus(
                    $"이미 '{_session.Project.EffectiveQuickCommands[at].DisplayName}'에 " +
                    $"똑같은 것이 있어 담지 않았습니다.");
                return;
            }

            _session.SetStatus(steps.Count == 1
                ? $"'{displayName}' 칩을 담았습니다."
                : $"'{displayName}' 칩을 담았습니다 ({steps.Count}단계).");
        });

        _scene.RefreshConsole();
    }

    /// <summary>Ctrl+D / [복제] — 원본 바로 뒤에, 편집 통로 하나(undo 한 번)로.</summary>
    private void ApplyScriptCommandDuplicate(PresentationResultCommand command)
    {
        if (_session is null || _current?.EditContext is not { DisabledReason: null } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 복제", () =>
        {
            _session.Editor.DuplicatePresentationCommand(context.PresentationNodeId, command.CommandId);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>Ctrl+V / [붙여넣기] — 클립보드 커맨드를 그 자리(라인 끝 또는 Setup 끝)에 단다.</summary>
    private void ApplyScriptCommandPaste(string? lineId, bool setup)
    {
        if (_session is null || _commandClipboard is not { } clip ||
            _current?.EditContext is not { DisabledReason: null } context ||
            (!setup && lineId is null))
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 붙여넣기", () =>
        {
            if (setup)
            {
                _session.Editor.AddPresentationSetupCommand(
                    context.PresentationNodeId, clip.DefinitionId, clip.Arguments, note: clip.Note);
            }
            else
            {
                _session.Editor.AddPresentationCommand(
                    context.PresentationNodeId, lineId!, clip.DefinitionId, clip.Arguments, note: clip.Note);
            }

            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>터미널 행 우측 X — 제거 통로다.</summary>
    private void ApplyScriptCommandRemove(PresentationResultCommand command)
    {
        // Setup 커맨드는 LineId 없이도 편집 대상이다 — Editable(라인 필요) 대신
        // 잠금 사유 없음만 본다.
        if (_session is null || _current?.EditContext is not { DisabledReason: null } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 제거", () =>
        {
            _session.Editor.RemovePresentationCommand(context.PresentationNodeId, command.CommandId);
            ManipulationApplied?.Invoke();
        });
    }

    private void Render()
    {
        // 챕터 런 스탯은 프리뷰가 얹는다 — 요청을 짓는 편집기는 런 상태를
        // 모르고, 여기는 모든 요청이 지나는 하나의 문이다.
        if (_current is not null &&
            ActiveChapterRun() is { } chapterRun &&
            chapterRun.Values is { Count: > 0 } chapterStats)
        {
            _current = _current with { ChapterStats = chapterStats };
        }

        MiniStagePreviewRequest? request = _current;
        PreviewAssetLibrary library = _session?.AssetLibrary ?? PreviewAssetLibrary.Empty;
        _renderedLibrary = library;

        // 활성 판이 바뀌면 챕터 콤보도 따라간다 — 다른 탭에서 고른 것이 여기에도 보인다.
        RefreshChapterCombo();
        _scene.Render(request);

        bool editable = request?.EditContext?.Editable == true;
        // Editable(라인 선택 필요)과 달리 Setup은 라인 없이도 편집 대상 — 잠금 사유만 본다.
        bool unlocked = request?.EditContext is { DisabledReason: null };
        // 담기 모드의 주인은 조절창 하나다 — 여기서 매번 물어보므로 두 화면의
        // 모드가 어긋날 자리가 없다(사본 금지).
        _script.PinMode = _scene.IsQuickPinMode;
        _script.PinHint = QuickPinHint();
        _script.Show(request?.ScriptRows, request?.SelectedLineId, unlocked, _setupSelected);
        ScriptHost.IsVisible = _script.IsVisible;

        // 작업대 = Inspector — 평소에는 비어 있고, 커맨드를 고르면
        // 그 하나의 편집 행 + 수치 조절이 선다. 추가 UI는 여기 안 선다 — 터미널 우클릭
        // [＋ 연출 추가]가 콘솔을 플라이아웃으로 띄운다(OnAddCommandRequested).
        // 공급자가 같은 구성을 캐시하므로 칩 편집 중 재렌더에 팝업이 닫히지 않는다.
        Control? detail = null;

        if (unlocked)
        {
            if (_script.SelectedCommandId is { } selectedCommandId)
            {
                detail = CommandDetailProvider?.Invoke(selectedCommandId);
            }
        }

        DetailHost.Content = detail ?? BuildEmptyDetailHint();

        // 프레임 타임라인 — 무대 아래 재생 줄에 이 라인의 내부 시간이 선다.
        TimelineHost.Content = _scene.BuildTimelineScrubber();

        if (request is null)
        {
            BadgeRow.Children.Clear();
            UnhandledHost.Children.Clear();
            NoticeHost.Children.Clear();
            StageReportExpander.Header = "연출 보고";
        }
        else
        {
            string? summary = StageIndicators.FillBadges(
                request,
                BadgeRow,
                UnhandledHost,
                _session?.BranchSelection,
                () => ManipulationApplied?.Invoke());
            StageIndicators.FillNotices(
                library,
                _session?.TuningLibrary ?? RuntimeTuningLibrary.Empty,
                request,
                NoticeHost,
                includeRootHint: true);

            // 접힌 보고의 머리글이 요약을 든다 — 챕터 그래프 검증 보고와 같은 문법.
            // 미반영이 없어도 알림이 있으면 개수는 보인다(펴기 전에 "볼 것이 있다"가 서야 한다).
            StageReportExpander.Header = summary is not null
                ? $"연출 보고 — {summary}"
                : NoticeHost.Children.Count > 0
                    ? $"연출 보고 — 알림 {NoticeHost.Children.Count}건"
                    : "연출 보고";
        }


        // 재생 모델에 현재 라인 위치를 알린다 — 이동 요청이 반영됐다는 신호이기도 하다.
        Playback.OnRequest(
            request?.LineIndex ?? -1,
            request?.LineCount ?? 0,
            request?.LineText,
            isChoice: request?.ChoiceOptions is { Count: > 0 },
            transitionSeconds: request?.TransitionSeconds ?? 0);
        SyncTypewriter();
        SyncTransition();
        FireLineAudio(request);
    }
}
