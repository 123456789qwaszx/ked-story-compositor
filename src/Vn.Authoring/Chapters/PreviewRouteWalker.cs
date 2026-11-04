using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 루트대로 걸으면 어디로 가나 — 재생 <b>전에</b> 지도에 예정 길을 그리기 위한 순수 계산 (2026-11-04).
/// director의 `ChapterFixtureWalker`와 같은 걸음이되 선택은 <b>문구</b>로 고르고, 관문 판정은 재생과 같은
/// <see cref="ChapterGateJudge"/>를 쓴다.
///
/// 멈추는 자리: 갈림인데 루트에 선택이 없다 · 고른 선택지가 숨었거나 잠겼다 · 같은 에피소드를 다시 만났다(순환).
/// 멈춘 이유를 말하되 <b>지나온 길은 그대로 돌려준다</b> — 지도는 거기까지 파랗게 그리고 멈춘 점에 ⚠을 찍는다.
/// </summary>
public static class PreviewRouteWalker
{
    /// <param name="EpisodeIds">지나간 에피소드, 순서대로. 첫 항목은 시작 에피소드.</param>
    /// <param name="Edges">탄 간선, 순서대로.</param>
    /// <param name="StoppedAt">멈춘 에피소드 — 끝까지 갔으면 null.</param>
    /// <param name="StoppedBecause">멈춘 이유 — 끝까지 갔으면 null.</param>
    /// <param name="FinalStats">끝(또는 멈춘 자리)의 스탯 — `스탯` 시트 순서.</param>
    public sealed record Result(
        IReadOnlyList<string> EpisodeIds,
        IReadOnlyList<ChapterEdge> Edges,
        string? StoppedAt,
        string? StoppedBecause,
        IReadOnlyList<ChapterRunStatValue> FinalStats)
    {
        public bool ReachedEnd => StoppedBecause is null;
    }

    /// <summary>루트의 시작 스탯 — 시트 초기값 위에 덮어쓴 것, 시트 순서.</summary>
    public static int[] StartStats(ChapterGraphModel chapter, PreviewRoute? route) =>
        chapter.Stats
            .Select(stat => route is not null && route.Stats.TryGetValue(stat.Key, out int value) ? Math.Clamp(value, stat.Minimum, stat.Maximum) : stat.Initial)
            .ToArray();

    public static Result Walk(ChapterGraphModel chapter, PreviewRoute? route, string? startEpisodeId = null)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var path = new List<string>();
        var taken = new List<ChapterEdge>();
        int[] stats = StartStats(chapter, route);

        IReadOnlyList<ChapterRunStatValue> Values() =>
            chapter.Stats.Select((stat, index) => new ChapterRunStatValue(stat, stats[index])).ToList();

        string? current = startEpisodeId ?? chapter.StartEpisode?.EpisodeId;

        if (current is null)
        {
            return new Result(path, taken, null, "에피소드가 없습니다.", Values());
        }

        while (true)
        {
            if (path.Contains(current))
            {
                return new Result(path, taken, current, $"'{current}'를 다시 만나 순환에서 멈췄습니다.", Values());
            }

            path.Add(current);

            List<ChapterEdge> outgoing = chapter.Edges
                .Where(edge => string.Equals(edge.FromEpisodeId, current, StringComparison.Ordinal))
                .Where(edge => chapter.FindEpisode(edge.ToEpisodeId) is not null)
                .ToList();

            if (outgoing.Count == 0)
            {
                return new Result(path, taken, null, null, Values());   // 챕터 끝.
            }

            ChapterEdge? next;

            if (outgoing.Count == 1 && outgoing[0].Auto)
            {
                next = outgoing[0];
            }
            else
            {
                PreviewRouteChoice? pick = route?.ChoiceAt(current);

                if (pick is null)
                {
                    List<ChapterEdge> passable = outgoing.Where(edge => IsPassable(chapter, edge, stats)).ToList();

                    if (passable.Count == 1)
                    {
                        next = passable[0];   // 고를 것이 하나뿐 — 묻지 않아도 길은 하나다.
                    }
                    else
                    {
                        return new Result(path, taken, current, $"'{current}'의 선택이 루트에 없습니다.", Values());
                    }
                }
                else
                {
                    next = outgoing.FirstOrDefault(edge =>
                        string.Equals(edge.OptionLabel, pick.OptionLabel, StringComparison.Ordinal));

                    if (next is null)
                    {
                        return new Result(path, taken, current, $"'{current}'에 선택지 '{pick.OptionLabel}'이 없습니다.", Values());
                    }

                    if (!IsPassable(chapter, next, stats))
                    {
                        string why = ChapterGateJudge.Judge(chapter, next.VisibleConditionLabel, stats) != ChapterGateVerdict.Open
                            ? $"숨김 조건 '{next.VisibleConditionLabel}'"
                            : $"해금 조건 '{next.ConditionLabel}'";
                        return new Result(path, taken, current,
                            $"'{current}'의 '{pick.OptionLabel}'은 지금 스탯으로 {why}에 막힙니다.", Values());
                    }
                }
            }

            taken.Add(next);
            stats = ChapterReachabilityProver.ApplyDeltas(chapter, stats, next.StatChanges);
            current = next.ToEpisodeId;
        }
    }

    private static bool IsPassable(ChapterGraphModel chapter, ChapterEdge edge, int[] stats) =>
        ChapterGateJudge.Judge(chapter, edge.VisibleConditionLabel, stats) == ChapterGateVerdict.Open &&
        ChapterGateJudge.Judge(chapter, edge.ConditionLabel, stats) == ChapterGateVerdict.Open;
}
