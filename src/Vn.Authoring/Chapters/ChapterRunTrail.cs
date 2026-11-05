namespace Vn.Authoring.Chapters;

public enum ChapterTrailStepKind
{
    /// <summary>씬 선택기(지도)로 들어왔다 — 간선을 타지 않은 시작점.</summary>
    Start,

    /// <summary>선택지 간선을 탔다.</summary>
    Choice,

    /// <summary>자동 간선으로 넘어왔다.</summary>
    Auto,

    /// <summary>분기 카드로 다녀왔다. <see cref="ChapterTrailStep.Played"/>가 거짓이면 첫머리 조건에 막혀 곧장 돌아왔다.</summary>
    Detour
}

/// <param name="FromEpisodeId">어디서 왔나. 시작점은 null.</param>
/// <param name="Played">분기 카드에서 줄을 하나라도 재생했나(조건이 성립했나). 분기가 아니면 true.</param>
public sealed record ChapterTrailStep(
    string EpisodeId,
    ChapterTrailStepKind Kind,
    string? FromEpisodeId,
    bool Played = true);

/// <summary>
/// <b>전체 재생이 지나온 길</b> — 어떤 루트와 선을 타며 어떤 에피소드들을 지나는지 지도에 정확히 표시한다.
/// 챕터 런(<see cref="ChapterRunState"/>)은 스탯만 들고 길은 잊는다 — 이 기록이
/// 지도의 덧칠(지나온 점 · 탄 선 · 막힌 분기)이다. 수명은 런과 같다(새 전체 재생이 비운다).
/// </summary>
public sealed class ChapterRunTrail
{
    private readonly List<ChapterTrailStep> _steps = [];

    public IReadOnlyList<ChapterTrailStep> Steps => _steps;

    /// <summary>지금 서 있는 에피소드 — 마지막으로 들어간 곳. 분기에서 돌아왔으면 그 분기를 부른 에피소드.</summary>
    public string? Current { get; private set; }

    public void Clear()
    {
        _steps.Clear();
        Current = null;
    }

    /// <summary>씬 선택기로 들어왔다 — 이전 길은 뜻을 잃는다(이어진 길이 아니다).</summary>
    public void Start(string episodeId)
    {
        _steps.Clear();
        _steps.Add(new ChapterTrailStep(episodeId, ChapterTrailStepKind.Start, null));
        Current = episodeId;
    }

    /// <summary>간선을 탔다.</summary>
    public void Follow(ChapterEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (Current is null || !string.Equals(Current, edge.FromEpisodeId, StringComparison.Ordinal))
        {
            // 지도 밖에서 왔거나 길이 끊겼다 — 출발점을 세워 선이 허공에서 시작하지 않게 한다.
            _steps.Clear();
            _steps.Add(new ChapterTrailStep(edge.FromEpisodeId, ChapterTrailStepKind.Start, null));
        }

        _steps.Add(new ChapterTrailStep(
            edge.ToEpisodeId,
            edge.Auto ? ChapterTrailStepKind.Auto : ChapterTrailStepKind.Choice,
            edge.FromEpisodeId));
        Current = edge.ToEpisodeId;
    }

    /// <summary>분기 카드로 다녀왔다 — 돌아온 뒤에 적는다(그때 재생했는지를 안다). 현재 위치는 부른 에피소드 그대로다.</summary>
    public void Detoured(string fromEpisodeId, string branchEpisodeId, bool played)
    {
        if (Current is null)
        {
            Start(fromEpisodeId);
        }

        _steps.Add(new ChapterTrailStep(branchEpisodeId, ChapterTrailStepKind.Detour, fromEpisodeId, played));
    }

    /// <summary>지나온 에피소드들 — 분기 카드는 재생했을 때만.</summary>
    public IReadOnlySet<string> Visited =>
        _steps.Where(step => step.Played).Select(step => step.EpisodeId).ToHashSet(StringComparer.Ordinal);

    /// <summary>다녀갔지만 조건에 막혀 곧장 돌아온 분기 카드들.</summary>
    public IReadOnlySet<string> BlockedBranches =>
        _steps.Where(step => step.Kind == ChapterTrailStepKind.Detour && !step.Played)
            .Select(step => step.EpisodeId)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>탄 선 — (어디서, 어디로). 분기도 선이다.</summary>
    public IReadOnlySet<(string From, string To)> TakenEdges =>
        _steps.Where(step => step.FromEpisodeId is not null)
            .Select(step => (step.FromEpisodeId!, step.EpisodeId))
            .ToHashSet();
}
