using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>챕터를 짓는 일이 프로젝트 안에서 일어난다</b> (R-F · 2026-09-16, 지시서 §4.1).
///
/// 옛 <c>ChapterWorkbookWriter</c>는 셀 하나를 고치는 외과수술이었다 — 열 번호와
/// 시트 이름이 부르는 쪽까지 스며 있었고, 고친 결과를 보려면 파일을 다시 읽어야 했다.
/// 이제 고치는 것은 모델이다(2026-10-29 · D2에 워크북 출력도 걷혔다).
///
/// ⛔ <b>이 묶음이 지키는 것은 "규칙이 안 사라졌다"이다.</b> 자리를 옮기는 변경에서 제일
/// 흔한 손실이 <i>옛 코드에만 적혀 있던 규칙</i>이다 — 간선의 신원(v9), 스탯변화를 적는 자리에서
/// 읽는 것, 조건이 식만 드는 것. (개명 · 삭제 · 배선 고치기 같은 원고 수정은 2026-10-29에
/// ked-story-director에 남았다 — 이 툴은 원고를 읽기만 한다. 여기 남은 것은 테스트가 원고를 짓는 길이다.)
/// </summary>
public sealed class ChapterEditingTests
{
    private readonly ProjectEditor _editor = new(new StoryProject());

    private ChapterDocument Chapter => _editor.FindChapter("ch01")!;

    public ChapterEditingTests()
    {
        _editor.EnsureChapter("ch01");
        _editor.AddEpisode("ch01", "ep01", "복도", 0, 0);
        _editor.AddEpisode("ch01", "ep02", "옥상", 200, 0);
    }

    [Fact]
    public void 에피소드를_세우면_대사엔트리가_Id와_같다()
    {
        // ⚠ v3 규약. 임포터와 [대본] 탭의 노드 이름 규칙이 같은 값을 보므로, 어긋나면
        //    같은 에피소드에 대사노드가 둘이 된다.
        Assert.Equal("ep01", Chapter.Episodes[0].DialogueEntry);
    }

    [Fact]
    public void 같은_Id로_두_번_세우지_못한다()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => _editor.AddEpisode("ch01", "ep01", "또 복도", 0, 0));

        Assert.Contains("ep01", error.Message);
    }

    [Fact]
    public void 다음_에피소드는_에피소드와_간선이_한_번에_선다()
    {
        // 분기 저작의 핵심 동작 — 둘 중 하나만 선 채로 남지 않는다.
        _editor.AddNextEpisode("ch01", "ep02", "ep03", "복도 끝", 400, 0, optionLabel: "더 간다");

        Assert.Contains(Chapter.Episodes, episode => episode.EpisodeId == "ep03");
        Assert.Contains(Chapter.Edges, edge =>
            edge.FromEpisodeId == "ep02" && edge.ToEpisodeId == "ep03" && edge.OptionLabel == "더 간다");

        // 사전에 없던 낱말은 사전에도 올라간다 — 다음부터 드롭다운에서 고른다.
        Assert.Contains("더 간다", Chapter.ChoiceOptions.Select(option => option.Text));
    }

    [Fact]
    public void 간선의_신원은_출발_도착_문구다()
    {
        // v9 — 같은 곳으로 가되 스탯변화·관문이 다른 선택지 둘을 여럿 둘 수 있다(흔한 패턴).
        _editor.AddEdge("ch01", "ep01", "ep02", optionLabel: "같이 간다");
        _editor.AddEdge("ch01", "ep01", "ep02", optionLabel: "혼자 간다");

        Assert.Equal(2, Chapter.Edges.Count);

        // 같은 문구로 또 만들지는 못한다.
        Assert.Throws<InvalidOperationException>(
            () => _editor.AddEdge("ch01", "ep01", "ep02", optionLabel: "같이 간다"));
    }

    [Fact]
    public void 스탯변화는_문법을_읽어_담고_못_읽으면_막는다()
    {
        // ⚠ 옛 경로에서는 셀에 원문이 남아 리더가 다음 읽기에 짚어 줬다. 이제 다시 읽는
        //    일이 없어 여기서 안 막으면 영영 사라진다.
        Chapter.Stats.Add(new ChapterStat("trust", "신뢰", 0, 0, 10, SourceRow: 0));

        _editor.AddEdge("ch01", "ep01", "ep02", optionLabel: "간다", statChanges: "trust +3");

        Assert.Equal(
            [("trust", 3)],
            Chapter.Edges[0].StatChanges.Select(delta => (delta.Key, delta.Amount)));

        Assert.Throws<InvalidOperationException>(() => _editor.AddEdge(
            "ch01", "ep01", "ep02", optionLabel: "안 간다", statChanges: "없는스탯 +1"));

        // 막힌 뒤에도 길이 그대로다 — 절반만 반영되지 않는다.
        Assert.Equal(["간다"], Chapter.Edges.Select(edge => edge.OptionLabel));
    }

    [Fact]
    public void 조건은_해석을_담지_않고_식만_든다()
    {
        // 해석은 ToGraphModel이 식에서 다시 푼다 — 담아 두면 고친 식과 갈린다.
        Chapter.Stats.Add(new ChapterStat("trust", "신뢰", 0, 0, 10, SourceRow: 0));

        _editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3", "친해졌을 때");

        Assert.Equal("trust >= 3", Chapter.Conditions[0].Expression);
        Assert.Equal("친해졌을 때", Chapter.Conditions[0].Description);
        Assert.Empty(Chapter.Conditions[0].Parsed);

        ChapterGraphModel model = Chapter.ToGraphModel("ch01");

        Assert.True(model.Conditions[0].IsValid);
        Assert.Equal(["trust"], model.Conditions[0].Parsed.Select(term => term.Key));
    }

    [Fact]
    public void 되돌리면_챕터_편집도_되돌아간다()
    {
        // 워크북이 원본이던 시절에는 되돌릴 곳이 파일이라 여기 없었다 — 이제 있다.
        _editor.AddEpisode("ch01", "ep03", "지하", 400, 0);
        Assert.Equal(3, Chapter.Episodes.Count);

        _editor.Undo();

        Assert.Equal(2, _editor.FindChapter("ch01")!.Episodes.Count);
    }
}
