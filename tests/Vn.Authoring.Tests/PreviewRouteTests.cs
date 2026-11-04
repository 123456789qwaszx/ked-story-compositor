using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 미리 정한 루트 (2026-11-04 소유자: "그 선택지가 있는 곳에서 무슨 선택지를 고를지 일종의 픽스처 느낌으로 미리 루트를 설정").
/// 이 툴의 파일에 살고(재생 설정 — 원고가 아니다), 지도가 재생 전에 예정 길을 걷고, 분기 카드의 첫머리 조건은 런의 스탯으로 판정된다.
/// </summary>
public sealed class PreviewRouteTests
{
    private static (StoryProject Project, ChapterGraphModel Chapter) FestivalEve()
    {
        string manifest = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "RuntimeSamples", "FestivalEve", "festival_eve.vnproject.json"));
        StoryProject project = CompositionStore.Load(manifest).Project;
        return (project, project.Chapters.Single().ToGraphModel("festival_eve"));
    }

    [Fact]
    public void 루트는_챕터마다_여럿이고_활성은_하나다()
    {
        (StoryProject project, _) = FestivalEve();
        var editor = new ProjectEditor(project);

        PreviewRoute first = editor.AddPreviewRoute("festival_eve");
        PreviewRoute second = editor.AddPreviewRoute("festival_eve", "요령 끝까지");

        Assert.Equal("루트 1", first.Name);
        Assert.Equal(2, editor.PreviewRoutesOf("festival_eve").Count);
        Assert.Same(second, editor.ActivePreviewRoute("festival_eve"));   // 새로 만든 것이 활성

        editor.SetActivePreviewRoute("festival_eve", first.Id);
        Assert.Same(first, editor.ActivePreviewRoute("festival_eve"));

        editor.SetActivePreviewRoute("festival_eve", null);
        Assert.Null(editor.ActivePreviewRoute("festival_eve"));

        editor.Undo();
        Assert.Same(first, editor.FindPreviewRoute(first.Id) is { IsActive: true } ? first : null);
    }

    [Fact]
    public void 갈림의_선택은_문구로_적고_같은_갈림은_하나다()
    {
        (StoryProject project, _) = FestivalEve();
        var editor = new ProjectEditor(project);
        PreviewRoute route = editor.AddPreviewRoute("festival_eve");

        editor.SetPreviewRouteChoice(route.Id, "Festival_Classroom", "있는 걸로 빨리 끝낸다");
        editor.SetPreviewRouteChoice(route.Id, "Festival_Wrapup", "교무실로 간다");
        editor.SetPreviewRouteChoice(route.Id, "Festival_Wrapup", "카스미에게 마무리를 부탁하고 간다");   // 바뀐다
        editor.SetPreviewRouteStat(route.Id, "성실", 3);

        Assert.Equal(2, route.Choices.Count);
        Assert.Equal("카스미에게 마무리를 부탁하고 간다", route.ChoiceAt("Festival_Wrapup")!.OptionLabel);
        Assert.Equal(3, route.Stats["성실"]);

        editor.SetPreviewRouteChoice(route.Id, "Festival_Wrapup", null);
        Assert.Null(route.ChoiceAt("Festival_Wrapup"));
        editor.SetPreviewRouteStat(route.Id, "성실", null);
        Assert.Empty(route.Stats);
    }

    [Fact]
    public void 루트는_이_툴의_파일에_왕복한다()
    {
        string folder = Path.Combine(Path.GetTempPath(), "vn-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            (StoryProject project, _) = FestivalEve();
            string manifest = Path.Combine(folder, "project.vnproject.json");
            ProjectStore.Save(manifest, project);

            var editor = new ProjectEditor(CompositionStore.Load(manifest).Project);
            PreviewRoute route = editor.AddPreviewRoute("festival_eve", "성실 경로");
            editor.SetPreviewRouteChoice(route.Id, "Festival_Classroom", "처음부터 다시 그린다");
            editor.SetPreviewRouteStat(route.Id, "요령", 1);
            CompositionStore.Save(CompositionStore.PathFor(manifest), manifest, editor.Project);

            StoryProject reopened = CompositionStore.Load(manifest).Project;
            PreviewRoute loaded = Assert.Single(reopened.PreviewRoutes);
            Assert.Equal(route.Id, loaded.Id);
            Assert.Equal("성실 경로", loaded.Name);
            Assert.True(loaded.IsActive);
            Assert.Equal("festival_eve", loaded.ChapterId);
            Assert.Equal(1, loaded.Stats["요령"]);
            Assert.Equal("처음부터 다시 그린다", loaded.ChoiceAt("Festival_Classroom")!.OptionLabel);

            // 원고 파일은 그대로다 — 루트는 .vncompose.json의 것이다.
            Assert.DoesNotContain("previewRoutes", File.ReadAllText(manifest));
            Assert.Contains("previewRoutes", File.ReadAllText(CompositionStore.PathFor(manifest)));

            // 되돌리기 스냅샷에도 실린다.
            StoryProject snapshot = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(editor.Project));
            Assert.Single(snapshot.PreviewRoutes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void 걷기는_루트대로_가고_막히면_자리와_이유를_말한다()
    {
        (StoryProject project, ChapterGraphModel chapter) = FestivalEve();
        var editor = new ProjectEditor(project);

        // 루트 없음 — 첫 갈림에서 멈춘다.
        PreviewRouteWalker.Result none = PreviewRouteWalker.Walk(chapter, null);
        Assert.Equal("Festival_Classroom", none.StoppedAt);
        Assert.Contains("선택이 루트에 없습니다", none.StoppedBecause);

        // 요령 경로 — 자동 간선은 저절로, 끝까지.
        PreviewRoute route = editor.AddPreviewRoute("festival_eve");
        editor.SetPreviewRouteChoice(route.Id, "Festival_Classroom", "있는 걸로 빨리 끝낸다");
        editor.SetPreviewRouteChoice(route.Id, "Festival_Wrapup", "카스미에게 마무리를 부탁하고 간다");

        PreviewRouteWalker.Result walked = PreviewRouteWalker.Walk(chapter, route);
        Assert.True(walked.ReachedEnd, walked.StoppedBecause);
        Assert.Equal(["Festival_Classroom", "Festival_Shortcut", "Festival_Wrapup", "Festival_Staffroom"], walked.EpisodeIds);
        Assert.Equal(3, walked.Edges.Count);
        Assert.Equal(3, walked.FinalStats.Single(value => value.Stat.Key == "요령").Value);   // +2 +1

        // 성실 0으로 '보고한다'를 고르면 숨김 조건에 막힌다 — 멈춘 자리와 이유.
        editor.SetPreviewRouteChoice(route.Id, "Festival_Wrapup", "선생님께 진행 상황도 보고한다");
        PreviewRouteWalker.Result blocked = PreviewRouteWalker.Walk(chapter, route);
        Assert.False(blocked.ReachedEnd);
        Assert.Equal("Festival_Wrapup", blocked.StoppedAt);
        Assert.Contains("숨김 조건", blocked.StoppedBecause);

        // 시작 스탯을 올리면 지나간다.
        editor.SetPreviewRouteStat(route.Id, "성실", 2);
        Assert.True(PreviewRouteWalker.Walk(chapter, route).ReachedEnd);
    }

    [Fact]
    public void 런의_스탯이_분기_카드의_첫머리_조건을_판정한다()
    {
        (_, ChapterGraphModel chapter) = FestivalEve();

        var run = new ChapterRunState(chapter, new Dictionary<string, int> { ["성실"] = 3 });
        IReadOnlyDictionary<string, string> values = run.AsConditionValues();

        Assert.Equal("3", values["stat(\"성실\")"]);
        Assert.Equal("0", values["stat(\"요령\")"]);

        Assert.True(ConditionExpression.TryEvaluate("stat(\"성실\") >= 2", values, out bool remade) && remade);
        Assert.True(ConditionExpression.TryEvaluate("stat(\"성실\") < 2", values, out bool sticker) && !sticker);
        Assert.True(ConditionExpression.TryEvaluate("stat(\"성실\") >= 3 && stat(\"요령\") < 3", values, out bool praise) && praise);
        Assert.True(ConditionExpression.TryEvaluate("stat(\"성실\") < 3 && stat(\"요령\") < 3", values, out bool pass) && !pass);

        // 한 항이라도 못 읽으면 평가 실패 — 반만 보고 판정하지 않는다.
        Assert.False(ConditionExpression.TryEvaluate("stat(\"성실\") >= 3 && $없음 == 1", values, out _));

        // HUD와 Resolve가 같은 값을 본다.
        Assert.Equal(3, run.Values.Single(value => value.Stat.Key == "성실").Value);
    }
}
