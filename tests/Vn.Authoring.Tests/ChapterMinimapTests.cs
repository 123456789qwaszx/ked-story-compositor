using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 챕터 지도 (슬레이 더 스파이어의 미니맵 같은 노드 기반 연결 + 피커). 축제 전날 예제로 잰다 —
/// 장면 둘 · 분기 카드 다섯 · 갈림 둘 · 자동 간선 둘 · 끝 하나. 자리는 director의 것이다.
/// </summary>
public sealed class ChapterMinimapTests
{
    private static (ChapterMinimap Map, StoryProject Project) FestivalEve()
    {
        string manifest = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "RuntimeSamples", "FestivalEve", "festival_eve.vnproject.json"));
        StoryProject project = CompositionStore.Load(manifest).Project;
        ChapterGraphModel chapter = project.Chapters.Single().ToGraphModel("festival_eve");

        return (ChapterMinimap.Build(chapter, project), project);
    }

    [Fact]
    public void 점의_모양은_구조에서_나온다()
    {
        (ChapterMinimap map, _) = FestivalEve();

        Assert.Equal(ChapterMinimapShape.SceneRoot, map.Find("Festival_Classroom")!.Shape);   // 교실의 루트 · 갈림이기도 하다
        Assert.True(map.Find("Festival_Classroom")!.IsFork);
        Assert.Equal(ChapterMinimapShape.Episode, map.Find("Festival_Remake")!.Shape);
        Assert.Equal(ChapterMinimapShape.Fork, map.Find("Festival_Wrapup")!.Shape);          // 교무실로 가는 선택지 셋
        Assert.Equal(ChapterMinimapShape.SceneRoot, map.Find("Festival_Staffroom")!.Shape);  // 교무실 앞의 루트 = 챕터 끝이기도
        Assert.True(map.Find("Festival_Staffroom")!.IsEnd);
        Assert.Equal(ChapterMinimapShape.Branch, map.Find("Festival_Wrapup_Remade")!.Shape);
        Assert.Equal(ChapterMinimapShape.Branch, map.Find("Festival_Staffroom_Pass")!.Shape);
        Assert.Equal(5, map.Nodes.Count(node => node.Shape == ChapterMinimapShape.Branch));
    }

    [Fact]
    public void 선은_선택지_자동_분기_셋이다()
    {
        (ChapterMinimap map, _) = FestivalEve();

        Assert.Equal(2, map.Edges.Count(edge => edge.Kind == ChapterMinimapEdgeKind.Auto));
        // 선택지 9 = 기본 5 + Festival_Add 갈래 4.
        Assert.Equal(9, map.Edges.Count(edge => edge.Kind == ChapterMinimapEdgeKind.Choice));
        Assert.Equal(5, map.Edges.Count(edge => edge.Kind == ChapterMinimapEdgeKind.Detour));

        Assert.Contains(map.Edges, edge =>
            edge.Kind == ChapterMinimapEdgeKind.Detour &&
            edge.FromEpisodeId == "Festival_Wrapup" && edge.ToEpisodeId == "Festival_Wrapup_Sticker");
        Assert.Contains(map.Edges, edge =>
            edge.Kind == ChapterMinimapEdgeKind.Choice && edge.Label == "카스미에게 마무리를 부탁하고 간다");
    }

    [Fact]
    public void 장면_띠와_자리는_director의_것이다()
    {
        (ChapterMinimap map, StoryProject project) = FestivalEve();

        Assert.Equal(["교실", "교무실 앞"], map.Scenes.Select(scene => scene.SceneId).ToList());
        Assert.Contains("Festival_Wrapup_Remade", map.Scenes[0].EpisodeIds);

        ChapterEpisode episode = project.Chapters.Single().ToGraphModel("festival_eve").FindEpisode("Festival_Shortcut")!;
        ChapterMinimapNode node = map.Find("Festival_Shortcut")!;
        Assert.Equal((episode.X, episode.Y), (node.X, node.Y));

        // 카드가 있어야 누를 수 있다 — 점 ↔ 카드가 양쪽으로 찾아진다.
        Assert.NotNull(node.DialogueNodeId);
        Assert.Same(node, map.FindByDialogue(node.DialogueNodeId));
    }

    [Fact]
    public void 길은_간선과_분기를_차례로_적고_막힌_분기를_가른다()
    {
        (ChapterMinimap map, StoryProject project) = FestivalEve();
        ChapterGraphModel chapter = project.Chapters.Single().ToGraphModel("festival_eve");
        ChapterEdge Edge(string from, string to, string? label = null) => chapter.Edges.Single(edge =>
            edge.FromEpisodeId == from && edge.ToEpisodeId == to && (label is null || edge.OptionLabel == label));

        var trail = new ChapterRunTrail();
        Assert.Null(trail.Current);

        trail.Start("Festival_Classroom");
        trail.Follow(Edge("Festival_Classroom", "Festival_Shortcut"));
        trail.Follow(Edge("Festival_Shortcut", "Festival_Wrapup"));
        trail.Detoured("Festival_Wrapup", "Festival_Wrapup_Remade", played: false);   // 성실 0 — 막힘
        trail.Detoured("Festival_Wrapup", "Festival_Wrapup_Sticker", played: true);
        trail.Follow(Edge("Festival_Wrapup", "Festival_Staffroom", "교무실로 간다"));

        Assert.Equal("Festival_Staffroom", trail.Current);
        Assert.Equal(
            new[] { "Festival_Classroom", "Festival_Shortcut", "Festival_Wrapup", "Festival_Wrapup_Sticker", "Festival_Staffroom" }.Order().ToList(),
            trail.Visited.Order().ToList());
        Assert.All(trail.Visited, id => Assert.NotNull(map.Find(id)));
        Assert.Equal(["Festival_Wrapup_Remade"], trail.BlockedBranches.ToList());
        Assert.Contains(("Festival_Shortcut", "Festival_Wrapup"), trail.TakenEdges);
        Assert.Contains(("Festival_Wrapup", "Festival_Wrapup_Remade"), trail.TakenEdges);
        Assert.DoesNotContain(("Festival_Classroom", "Festival_Remake"), trail.TakenEdges);
        Assert.Equal(ChapterTrailStepKind.Auto, trail.Steps[2].Kind);

        // 지도 밖에서 온 간선은 출발점부터 다시 — 선이 허공에서 시작하지 않는다.
        trail.Follow(Edge("Festival_Classroom", "Festival_Remake"));
        Assert.Equal(2, trail.Steps.Count);
        Assert.Equal(ChapterTrailStepKind.Start, trail.Steps[0].Kind);
        Assert.Equal("Festival_Remake", trail.Current);

        trail.Clear();
        Assert.Empty(trail.Steps);
    }
}
