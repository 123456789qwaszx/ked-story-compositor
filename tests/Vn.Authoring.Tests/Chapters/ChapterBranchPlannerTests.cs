using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 깊이 기반 분기 배치 (챕터 v2 8단계 · 2026-10-29 D3부터 깊이가 아래로 간다). 소유자 피드백 그대로의 사례를 고정한다:
/// "root에서 뻗어나온 두 에피소드 둘이서 다시 연결되는" 합류가 터무니없어 보이지 않아야 하고,
/// 서로 다른 부모의 자식이 겹치지 않아야 한다.
/// </summary>
public sealed class ChapterBranchPlannerTests
{
    private static ChapterEpisode Episode(string id, double x, double y) =>
        new(id, id, "10", $"Story_{id}", x, y,
            Memo: null, SourceRow: 2, AllowUnreachable: false);

    private static ChapterEdge Edge(string from, string to) =>
        new(from, to, OptionLabel: null, ConditionLabel: null,
            LockedMessage: null, SourceRow: 2);

    private static ChapterGraphModel Model(
        IReadOnlyList<ChapterEpisode> episodes, IReadOnlyList<ChapterEdge> edges) =>
        new("ch", "ch", episodes, edges, [], [], [], []);

    [Fact]
    public void 합류_노드의_깊이는_가장_깊은_부모_기준이다()
    {
        // root → a → c,  root → b → (b2) → c  — c는 depth 2가 아니라 3이어야
        // b2 → c 간선이 뒤로 꺾이지 않는다.
        ChapterGraphModel model = Model(
            [Episode("root", 0, 0), Episode("a", 220, 0), Episode("b", 220, 110),
             Episode("b2", 440, 110), Episode("c", 440, 0)],
            [Edge("root", "a"), Edge("root", "b"), Edge("b", "b2"),
             Edge("a", "c"), Edge("b2", "c")]);

        IReadOnlyDictionary<string, int> depths = ChapterBranchPlanner.Depths(model);

        Assert.Equal(0, depths["root"]);
        Assert.Equal(1, depths["a"]);
        Assert.Equal(1, depths["b"]);
        Assert.Equal(2, depths["b2"]);
        Assert.Equal(3, depths["c"]);
    }

    [Fact]
    public void 순환이_있어도_깊이_계산이_멈춘다()
    {
        ChapterGraphModel model = Model(
            [Episode("root", 0, 0), Episode("a", 220, 0), Episode("b", 440, 0)],
            [Edge("root", "a"), Edge("a", "b"), Edge("b", "a")]);

        IReadOnlyDictionary<string, int> depths = ChapterBranchPlanner.Depths(model);

        Assert.True(depths["a"] < model.Episodes.Count);
        Assert.True(depths["b"] < model.Episodes.Count);
    }

}
