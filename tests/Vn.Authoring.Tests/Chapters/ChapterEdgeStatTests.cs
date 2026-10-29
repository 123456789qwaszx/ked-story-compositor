using System.Text.Json;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 간선 `스탯변화` (2026-08-14 소유자 결정) — 스탯이 변하는 유일한 자리는 에피소드 사이,
/// 간선을 타는 순간의 1회 커밋이다. 모델→도달성 증명→내보내기(무대의 챕터 재생이 읽는 JSON)가 한 값을 본다.
/// </summary>
public sealed class ChapterEdgeStatTests
{
    private readonly ProjectEditor _editor = new(new StoryProject());

    private ChapterDocument Chapter => _editor.FindChapter("ch01")!;

    private ChapterGraphModel Model() => Chapter.ToGraphModel("ch01");

    public ChapterEdgeStatTests()
    {
        _editor.EnsureChapter("ch01");
        Chapter.Stats.Add(new ChapterStat("trust", "신뢰", 0, 0, 10, SourceRow: 0));

        _editor.AddEpisode("ch01", "ep1", title: "", 0, 0);
        _editor.AddEpisode("ch01", "ep2", title: "", 1, 0);
        _editor.AddEdge("ch01", "ep1", "ep2", optionLabel: "계속");
    }

    /// <summary>ep2 →(신뢰높음 관문)→ ep3, 그리고 ep1→ep2가 trust +3을 커밋한다.</summary>
    private void AddGatedThirdEpisode()
    {
        _editor.AddEpisode("ch01", "ep3", title: "", 2, 0);
        _editor.AddEdge("ch01", "ep2", "ep3", optionLabel: "계속", conditionLabel: "신뢰높음");
        _editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");
        ManuscriptFixture.Edge(_editor.Project, "ch01", "ep1", "ep2", "계속", edge =>
            edge with { StatChanges = ManuscriptFixture.Deltas(_editor.Project, "ch01", "trust +3") });
    }

    [Fact]
    public void 미등록_스탯키는_간선에_적는_순간_막힌다()
    {
        // 2026-10-29 · D2 — 예전에는 셀에 원문이 남고 리더가 다음 읽기에 오류로 짚었다.
        // 이제 다시 읽는 일이 없으므로 편집기가 적는 자리에서 막는다.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            _editor.AddEdge("ch01", "ep2", "ep1", optionLabel: "되돌아간다", statChanges: "karma +1"));

        Assert.Contains("karma", error.Message);
        Assert.Empty(Assert.Single(Model().Edges).StatChanges);
    }

    [Fact]
    public void 도달성_증명이_간선_증감으로_관문을_연다()
    {
        // 같은 챕터를 증명기로 — 간선 +3이 없으면 ep3는 도달 불가였을 구조다.
        AddGatedThirdEpisode();

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(Model());

        Assert.Contains("ep3", result.ReachableEpisodeIds);
    }

    [Fact]
    public void 길_하나가_선택지_하나이고_문구는_챕터의_사전에서_온다()
    {
        // v9 (2026-08-17 소유자) — "선택지는 인덱스를 가져오는 게 아니라 그냥 깡으로 대사만."
        // 간선에 문구가 그대로 들어가고, 신원은 (출발, 도착, 문구)다.
        Assert.Equal("계속", Assert.Single(Model().Edges).OptionLabel); // v12 — 문구 없는 길은 폐지

        // 같은 도착으로 가는 길을 문구만 달리해 둘 더 낸다 — 흔한 패턴이고 허용된다.
        _editor.AddEdge("ch01", "ep1", "ep2", optionLabel: "믿는다");
        _editor.AddEdge("ch01", "ep1", "ep2", optionLabel: "무시한다");

        ChapterGraphModel model = Model();
        Assert.Equal(3, model.Edges.Count);
        Assert.False(model.HasErrors);

        // 쓴 문구는 사전에도 올라 다음번 드롭다운 재료가 된다.
        Assert.Equal(["계속", "믿는다", "무시한다"], model.ChoiceOptions.Select(option => option.Text));

        // 셋(출발·도착·문구)이 다 같은 길을 두 번 낼 수는 없다.
        Assert.Throws<InvalidOperationException>(
            () => _editor.AddEdge("ch01", "ep1", "ep2", optionLabel: "믿는다"));

        // 문구는 어느 에피소드에서든 다시 쓴다 — 사전은 챕터 전체의 것이다.
        _editor.AddEdge("ch01", "ep2", "ep1", optionLabel: "믿는다");
        // v12 — 툴이 놓는 첫 길에 "계속"이 들어가므로 사전 낱말이 셋이다.
        Assert.Equal(3, Model().ChoiceOptions.Count);
    }

    [Fact]
    public void 내보내기에_간선_스탯변화가_실린다()
    {
        ManuscriptFixture.Edge(_editor.Project, "ch01", "ep1", "ep2", "계속", edge =>
            edge with { StatChanges = ManuscriptFixture.Deltas(_editor.Project, "ch01", "trust +2") });

        ChapterExportResult result = ChapterProgressionExporter.Export(Model());

        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using JsonDocument document = JsonDocument.Parse(result.Json!);
        JsonElement option = document.RootElement
            .GetProperty("Nodes")[0].GetProperty("NextOptions")[0];

        JsonElement change = option.GetProperty("StatChanges")[0];
        Assert.Equal("trust", change.GetProperty("Key").GetString());
        Assert.Equal(2, change.GetProperty("Amount").GetInt32());
    }
}
