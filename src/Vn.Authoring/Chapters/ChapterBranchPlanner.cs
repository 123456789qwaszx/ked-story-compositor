namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터의 <b>깊이</b>(depth) — 시작 에피소드에서 그 에피소드까지의 <b>가장 긴 경로</b>. 합류 노드(두 분기가 다시
/// 만나는 곳)는 가장 깊은 부모보다 한 칸 깊다. 장면 묶음(<see cref="ChapterSceneGrouping"/>)이 읽는다.
///
/// ⛔ 이 깊이로 챕터 그래프를 펴던 배치(새 카드 자리 · 한 번 심기 · [자동 정렬])는 2026-10-29에 걷혔다 —
/// 이 툴에는 그래프가 없고, 그래프는 ked-story-director에 있다.
/// </summary>
public static class ChapterBranchPlanner
{
    /// <summary>
    /// 시작 에피소드에서 각 에피소드까지의 깊이(가장 긴 경로). 도달할 수 없는 노드는
    /// 사전에 없다. 순환이 있어도 멈춘다 — 깊이는 노드 수를 넘을 수 없기 때문이다.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Depths(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var depths = new Dictionary<string, int>(StringComparer.Ordinal);

        if (model.StartEpisode is not { } start)
        {
            return depths;
        }

        depths[start.EpisodeId] = 0;

        // 벨만-포드식 이완. 깊이 상한 = 노드 수 - 1 이므로 순환이 있어도 그 이상 자라지 않는다.
        int cap = model.Episodes.Count;
        bool changed = true;

        for (int pass = 0; changed && pass < cap; pass++)
        {
            changed = false;

            foreach (ChapterEdge edge in model.Edges)
            {
                if (!depths.TryGetValue(edge.FromEpisodeId, out int fromDepth))
                {
                    continue;
                }

                int candidate = fromDepth + 1;

                if (candidate >= cap)
                {
                    continue; // 순환 가드
                }

                if (!depths.TryGetValue(edge.ToEpisodeId, out int current) || candidate > current)
                {
                    depths[edge.ToEpisodeId] = candidate;
                    changed = true;
                }
            }
        }

        return depths;
    }

}
