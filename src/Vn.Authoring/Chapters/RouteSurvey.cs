using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Chapters;

/// <summary>갈림의 선택지 하나 — 설문의 보기 한 줄.</summary>
/// <param name="Visible">길 위에서 이 자리에 섰을 때의 숨김 판정. 길이 닿지 않으면 null.</param>
/// <param name="Unlock">길 위에서 이 자리에 섰을 때의 해금 판정. 길이 닿지 않으면 null.</param>
public sealed record RouteSurveyChoice(
    ChapterEdge Edge,
    string Label,
    bool IsSelected,
    ChapterGateVerdict? Visible,
    ChapterGateVerdict? Unlock)
{
    public bool IsAuto => Edge.Auto;

    /// <summary>길 위에서 지금 고를 수 있나 — 숨김도 해금도 열려 있을 때.</summary>
    public bool? IsSelectable => Visible is null || Unlock is null
        ? null
        : Visible == ChapterGateVerdict.Open && Unlock == ChapterGateVerdict.Open;
}

/// <summary>에피소드가 줄 앞에서 다녀오는 분기 카드 — "한 번 갔다가 진행"을 그대로 적는다.</summary>
/// <param name="Passes">길 위 그 자리의 스탯으로 첫머리 조건이 성립하는가. 길이 닿지 않으면 null, 조건이 평가 불가여도 null.</param>
/// <param name="LineId">분기 표식이 붙은 줄 — 이 줄 <b>앞에서</b> 다녀온다.</param>
/// <param name="LineNumber">그 줄이 카드의 살아 있는 줄 중 몇 번째인가(1부터). 무대의 줄 번호와 같다.</param>
/// <param name="Speaker">그 줄의 화자(주 언어). 없으면 빈 문자열.</param>
/// <param name="Text">그 줄의 대사(주 언어). 없으면 빈 문자열.</param>
public sealed record RouteSurveyDetour(
    string BranchEpisodeId,
    string Title,
    string? Condition,
    bool? Passes,
    bool Reached,
    string LineId,
    int LineNumber,
    string Speaker,
    string Text);

/// <summary>설문의 문항 하나 = 에피소드 하나.</summary>
/// <param name="StepIndex">길 위의 몇 번째인가(0부터). 길이 닿지 않으면 -1.</param>
/// <param name="StatsOnArrival">들어설 때의 스탯 — 시트 순서. 길이 닿지 않으면 null.</param>
public sealed record RouteSurveyEpisode(
    string EpisodeId,
    string Title,
    string SceneId,
    bool IsBranchCard,
    int StepIndex,
    IReadOnlyList<ChapterRunStatValue>? StatsOnArrival,
    IReadOnlyList<RouteSurveyChoice> Choices,
    IReadOnlyList<RouteSurveyDetour> Detours)
{
    public bool OnRoute => StepIndex >= 0;

    /// <summary>묻는 자리인가 — 고를 선택지가 둘 이상(자동 간선 하나뿐이면 묻지 않는다).</summary>
    public bool IsFork => Choices.Count(choice => !choice.IsAuto) >= 2;
}

/// <summary>
/// <b>루트 설문</b> (2026-11-04 소유자: *"특정 장면 내 에피소드별 선택지가 별도 탭에서 아주 정확하게 일종의 설문처럼 표시가 되어
/// 있고, 그걸 원하는 대로 세팅해 두는 방식 … 각 선택지를 고르는 것에 따라서 스탯이랑, 선택지 루트 같은 게 도달 가능한지,
/// 스탯 변화가 진짜 실제로 어떻게 되는지 같은 걸 수기로 체크한다는 느낌"*).
///
/// 챕터의 에피소드를 장면 순 · 길 순으로 늘어놓고, 문항마다 보기(선택지)와 그 자리의 스탯 · 숨김/해금 판정 · 다녀오는 분기와
/// 그 조건의 성립 여부를 적는다. 판정은 전부 재생과 같은 함수(<see cref="ChapterGateJudge"/> · <see cref="ConditionExpression"/>)로 —
/// 설문이 말하는 것과 무대가 하는 것이 다르면 안 된다. 순수 모델이고 화면은 그리기만 한다.
/// </summary>
public sealed class RouteSurvey
{
    private RouteSurvey(
        ChapterGraphModel chapter,
        PreviewRoute? route,
        PreviewRouteWalker.Result walk,
        IReadOnlyList<RouteSurveyEpisode> episodes,
        IReadOnlyList<ChapterMinimapScene> scenes)
    {
        Chapter = chapter;
        Route = route;
        Walk = walk;
        Episodes = episodes;
        Scenes = scenes;
    }

    public ChapterGraphModel Chapter { get; }

    public PreviewRoute? Route { get; }

    public PreviewRouteWalker.Result Walk { get; }

    /// <summary>장면 순, 장면 안에서는 길 순(길에 없는 것은 뒤에 시트 순).</summary>
    public IReadOnlyList<RouteSurveyEpisode> Episodes { get; }

    public IReadOnlyList<ChapterMinimapScene> Scenes { get; }

    public RouteSurveyEpisode? Find(string episodeId) =>
        Episodes.FirstOrDefault(episode => string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal));

    /// <summary>길이 닿은 에피소드 중 묻는 자리인데 루트에 선택이 없는 것 — 설문에서 아직 안 고른 문항.</summary>
    public IReadOnlyList<RouteSurveyEpisode> Unanswered =>
        Episodes.Where(episode => episode.OnRoute && episode.IsFork && !episode.Choices.Any(choice => choice.IsSelected)).ToList();

    public static RouteSurvey Build(ChapterGraphModel chapter, StoryProject? project, GameDefinition? definition, PreviewRoute? route)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        // 분기 — 카드 줄의 표식에서. 첫머리 조건은 다녀갈 카드의 첫 줄 BeginIf의 식(공급된 조건).
        // 어느 줄에 붙었는지도 같이 적는다 (2026-11-04 소유자: "정확히 어떤 줄에서 생기는지를 모르겠습니다").
        var detourTargets = new Dictionary<string, List<DetourSource>>(StringComparer.Ordinal);
        var branchCards = new HashSet<string>(StringComparer.Ordinal);

        if (project is not null)
        {
            Dictionary<string, DialogueNode> cards = chapter.Episodes
                .Select(episode => (episode.EpisodeId, Card: EpisodeNaming.CardFor(project, chapter.ChapterId, episode.EpisodeId)))
                .Where(pair => pair.Card is not null)
                .ToDictionary(pair => pair.EpisodeId, pair => pair.Card!, StringComparer.Ordinal);
            Dictionary<string, string> episodeByNodeId = cards.ToDictionary(pair => pair.Value.Id, pair => pair.Key, StringComparer.Ordinal);

            foreach ((string episodeId, DialogueNode card) in cards)
            {
                int lineNumber = 0;

                foreach (ScriptLine line in card.Script.ActiveLines)
                {
                    lineNumber++;

                    foreach (string targetNodeId in card.FindExtension(line.Id)?.DetourTargetNodeIds ?? [])
                    {
                        if (!episodeByNodeId.TryGetValue(targetNodeId, out string? branchEpisode) ||
                            !cards.TryGetValue(branchEpisode, out DialogueNode? branchCard))
                        {
                            continue;
                        }

                        branchCards.Add(branchEpisode);

                        if (!detourTargets.TryGetValue(episodeId, out List<DetourSource>? list))
                        {
                            detourTargets[episodeId] = list = [];
                        }

                        LocalizedLine text = card.Script.Text(line.Id);
                        list.Add(new DetourSource(branchEpisode, HeadCondition(project, definition, branchCard), line.Id, lineNumber, text.Speaker, text.Text));
                    }
                }
            }
        }

        PreviewRouteWalker.Result walk = PreviewRouteWalker.Walk(
            chapter, route, null,
            episodeId => detourTargets.TryGetValue(episodeId, out List<DetourSource>? list)
                ? list.Select(source => (source.BranchEpisodeId, source.Condition)).ToList()
                : []);

        var titles = chapter.Episodes.ToDictionary(
            episode => episode.EpisodeId,
            episode => string.IsNullOrWhiteSpace(episode.Title) ? episode.EpisodeId : episode.Title,
            StringComparer.Ordinal);

        var episodes = new List<RouteSurveyEpisode>();
        List<ChapterMinimapScene> scenes = ChapterSceneGrouping.Of(chapter)
            .Select(scene => new ChapterMinimapScene(scene.SceneId, scene.DisplayName, scene.Episodes.Select(episode => episode.EpisodeId).ToList()))
            .ToList();

        foreach (ChapterMinimapScene scene in scenes)
        {
            IEnumerable<string> ordered = scene.EpisodeIds
                .OrderBy(id => walk.EpisodeIds.Contains(id) ? walk.EpisodeIds.ToList().IndexOf(id) : int.MaxValue)
                .ThenBy(id => chapter.Episodes.ToList().FindIndex(episode => episode.EpisodeId == id));

            foreach (string episodeId in ordered)
            {
                int step = walk.EpisodeIds.ToList().IndexOf(episodeId);
                int[]? stats = walk.StatsOnArrival.TryGetValue(episodeId, out int[]? arrival) ? arrival : null;

                List<RouteSurveyChoice> choices = chapter.Edges
                    .Where(edge => string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal))
                    .Select(edge => new RouteSurveyChoice(
                        edge,
                        string.IsNullOrWhiteSpace(edge.OptionLabel) ? (edge.Auto ? "(자동)" : "(문구 없음)") : edge.OptionLabel,
                        route?.ChoiceAt(episodeId) is { } pick && string.Equals(pick.OptionLabel, edge.OptionLabel, StringComparison.Ordinal),
                        stats is null ? null : ChapterGateJudge.Judge(chapter, edge.VisibleConditionLabel, stats),
                        stats is null ? null : ChapterGateJudge.Judge(chapter, edge.ConditionLabel, stats)))
                    .ToList();

                List<RouteSurveyDetour> detours = detourTargets.TryGetValue(episodeId, out List<DetourSource>? targets)
                    ? targets.Select(target =>
                    {
                        PreviewRouteWalker.Detour? judged = walk.Detours.FirstOrDefault(detour =>
                            detour.FromEpisodeId == episodeId && detour.BranchEpisodeId == target.BranchEpisodeId);
                        return new RouteSurveyDetour(
                            target.BranchEpisodeId,
                            titles.GetValueOrDefault(target.BranchEpisodeId, target.BranchEpisodeId),
                            target.Condition,
                            judged?.Passes,
                            judged is not null,
                            target.LineId,
                            target.LineNumber,
                            target.Speaker,
                            target.Text);
                    }).ToList()
                    : [];

                episodes.Add(new RouteSurveyEpisode(
                    episodeId,
                    titles[episodeId],
                    scene.SceneId,
                    branchCards.Contains(episodeId),
                    step,
                    stats is null ? null : chapter.Stats.Select((stat, index) => new ChapterRunStatValue(stat, stats[index])).ToList(),
                    choices,
                    detours));
            }
        }

        return new RouteSurvey(chapter, route, walk, episodes, scenes);
    }

    /// <summary>판에서 읽은 분기 표식 하나 — 어느 줄 앞에서 어느 카드로, 그 카드의 첫머리 조건은 무엇인가.</summary>
    private sealed record DetourSource(string BranchEpisodeId, string? Condition, string LineId, int LineNumber, string Speaker, string Text);

    /// <summary>분기 카드의 첫머리 조건 — 첫 줄의 BeginIf가 가리키는 공급 조건의 식. 없으면 null(무조건 돈다).</summary>
    private static string? HeadCondition(StoryProject project, GameDefinition? definition, DialogueNode branchCard)
    {
        if (branchCard.Script.ActiveLines.FirstOrDefault() is not { } first)
        {
            return null;
        }

        LineConditionTransition? head = branchCard.FindExtension(first.Id)?.Transitions
            .FirstOrDefault(transition => transition.Kind == ConditionTransitionKind.BeginIf);

        if (head?.ConditionId is not { } conditionId)
        {
            return null;
        }

        AvailableCondition? condition = AvailableConditionResolver.Resolve(project, branchCard.Id, definition).Find(conditionId)
            ?? AvailableConditionResolver.FindKnown(project, definition, conditionId);

        // 조건은 있는데 식을 모른다 — "무조건"이 아니라 "평가 불가"여야 한다. 빈 식은 평가기가 실패로 돌린다.
        return condition?.Expression ?? string.Empty;
    }
}
