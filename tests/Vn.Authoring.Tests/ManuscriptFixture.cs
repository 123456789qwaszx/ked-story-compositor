using Vn.Authoring.Chapters;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 테스트가 원고를 <b>손질하는</b> 자리 — 챕터의 간선 · 에피소드 한 줄을 모델에서 바로 고친다.
///
/// 원고를 고치는 편집 명령(간선 · 에피소드 수정 · 개명 · 이동)은 ked-story-director의 것이라
/// 이 툴에는 없다(2026-10-29, <c>docs/plans/compositor-split.md</c> C2b-2b). 이 툴이 하는 일은
/// 이미 쓰인 원고를 읽어 무대에 올리고 내보내는 것이므로, 테스트는 "director가 이렇게 써 둔
/// 원고"를 여기서 지어 놓고 읽는 쪽을 잰다. 되돌리기 · 변경 알림을 지나지 않는다 — 준비 전용이다.
/// </summary>
internal static class ManuscriptFixture
{
    /// <summary>(출발, 도착, 문구)로 찾은 간선 한 줄을 바꾼다.</summary>
    public static void Edge(
        StoryProject project,
        string chapterId,
        string fromEpisodeId,
        string toEpisodeId,
        string? optionLabel,
        Func<ChapterEdge, ChapterEdge> change)
    {
        List<ChapterEdge> edges = Chapter(project, chapterId).Edges;
        int index = edges.FindIndex(edge =>
            string.Equals(edge.FromEpisodeId, fromEpisodeId, StringComparison.Ordinal) &&
            string.Equals(edge.ToEpisodeId, toEpisodeId, StringComparison.Ordinal) &&
            string.Equals(edge.OptionLabel, optionLabel, StringComparison.Ordinal));

        Assert.True(index >= 0, $"간선 {fromEpisodeId} → {toEpisodeId} \"{optionLabel}\"가 없습니다.");
        edges[index] = change(edges[index]);
    }

    /// <summary>에피소드 한 줄을 바꾼다.</summary>
    public static void Episode(
        StoryProject project, string chapterId, string episodeId, Func<ChapterEpisode, ChapterEpisode> change)
    {
        List<ChapterEpisode> episodes = Chapter(project, chapterId).Episodes;
        int index = episodes.FindIndex(episode =>
            string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal));

        Assert.True(index >= 0, $"에피소드 '{episodeId}'가 없습니다.");
        episodes[index] = change(episodes[index]);
    }

    /// <summary>대사 노드의 <b>꼬리</b> — 마지막 줄 뒤에 실린 전환(대사 없는 조건 블록)을 적는다.</summary>
    public static void Trailing(DialogueNode node, params LineConditionTransition[] transitions) =>
        node.TrailingTransitions.AddRange(transitions);

    /// <summary>`스탯변화` 원문을 그 챕터의 스탯으로 읽는다 — 원고에 적히는 모양 그대로다.</summary>
    public static IReadOnlyList<StatDelta> Deltas(StoryProject project, string chapterId, string text)
    {
        StatDeltaParseResult parsed = StatDeltaParser.Parse(
            text, Chapter(project, chapterId).Stats.Select(stat => stat.Key).ToList());

        Assert.True(parsed.IsValid, string.Join(" / ", parsed.Problems.Select(problem => problem.Message)));
        return parsed.Deltas;
    }

    private static ChapterDocument Chapter(StoryProject project, string chapterId) =>
        project.Chapters.Single(chapter => string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal));
}
