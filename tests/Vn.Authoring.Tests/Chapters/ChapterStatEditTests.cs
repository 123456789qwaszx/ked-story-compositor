using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>스탯과 조건을 툴에서 정의한다</b> (2026-09-17 소유자: *"이제 엑셀이 아닌 챕터그래프에서
/// 직접 조건과 스탯을 정의하려고 합니다"*).
///
/// R-A~R-F가 이미 워크북을 <b>산출물</b>로 만들었고 스탯·조건은 <c>ChapterDocument</c>에
/// 산다 — 그래서 이건 새 정본이 아니라 <b>마지막 남은 창구</b>를 옮기는 일이다.
/// </summary>
public sealed class ChapterStatEditTests
{
    [Fact]
    public void 스탯을_세운다()
    {
        ProjectEditor editor = World();

        ChapterStat stat = editor.AddChapterStat(
            "ch01", "courage", displayName: "용기", initial: 2, minimum: 0, maximum: 5);

        Assert.Equal("용기", stat.DisplayName);
        Assert.Contains(Chapter(editor).Stats, item => item.Key == "courage");
    }

    [Fact]
    public void 표시이름을_안_주면_키를_쓴다()
    {
        Assert.Equal("courage", World().AddChapterStat("ch01", "courage").DisplayName);
    }

    [Fact]
    public void 키가_겹치면_거절한다()
    {
        // ⛔ 같은 키가 둘이면 어느 행이 이기는지 말할 수 없다 — 진단으로 넘길 상태가 아니다.
        ProjectEditor editor = World();

        Assert.Throws<InvalidOperationException>(() => editor.AddChapterStat("ch01", "trust"));
        Assert.Single(Chapter(editor).Stats, stat => stat.Key == "trust");
    }

    [Fact]
    public void 공백만_다른_키는_이제_서로_다른_스탯이다()
    {
        // ⚠ <b>뒤집혔다</b> (2026-09-17, 런타임 회신 §6.1). `호감 도`와 `호감_도`는 한때
        //    내보낼 때 정규화돼 게임에서 하나가 됐고, 그래서 막았다. `stat("키")`의 인자가
        //    <b>문자열 리터럴</b>이 되면서 정규화가 없어졌으니 막을 이유도 없다.
        ProjectEditor editor = World();
        editor.AddChapterStat("ch01", "호감_도");

        editor.AddChapterStat("ch01", "호감 도");

        Assert.Equal(2, Chapter(editor).Stats.Count(stat => stat.Key.StartsWith("호감", StringComparison.Ordinal)));
    }

    [Fact]
    public void 공백이_든_키가_대사에_원본_그대로_나간다()
    {
        // ⛔ <b>진행 JSON은 키를 원본 그대로 낸다.</b> 대사만 정규화하면 둘이 갈리고,
        //    산출도 컴파일도 통과한 뒤 <b>그 대사가 재생될 때</b> 런타임에서 터진다
        //    (런타임 회신 §6.1이 잡았다).
        ProjectEditor editor = World();
        editor.AddChapterStat("ch01", "호감도 (윌로우)");
        editor.AddChapterCondition("ch01", "친함", "호감도 (윌로우) >= 3");

        Assert.Equal(
            "stat(\"호감도 (윌로우)\") >= 3",
            ConditionYarnTranslator.Translate(
                Chapter(editor).ToGraphModel("ch01", null).Conditions.Single(item => item.Label == "친함")).Yarn);
    }

    [Fact]
    public void 경계가_어긋나도_막지_않고_진단에_맡긴다()
    {
        // ⚠ <c>ChapterDocument</c>가 이미 짚는다. 여기서만 막으면 툴로 만든 것과 워크북에서
        //    읽은 것이 <b>서로 다른 규칙</b>을 살게 되고, 값을 채워 가는 중에 막힌다.
        ProjectEditor editor = World();

        editor.AddChapterStat("ch01", "이상함", initial: 9, minimum: 3, maximum: 1);

        Assert.Contains(Chapter(editor).Stats, stat => stat.Key == "이상함");
        Assert.Contains(
            Chapter(editor).ToGraphModel("ch01", null).Diagnostics,
            item => item.Message.Contains("이상함", StringComparison.Ordinal));
    }

    // ── 조건 삭제 ───────────────────────────────────────────────────────────

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterDocument Chapter(ProjectEditor editor) => editor.FindChapter("ch01")!;

    private static ProjectEditor World()
    {
        var editor = new ProjectEditor(new StoryProject());
        ChapterDocument chapter = editor.EnsureChapter("ch01");

        chapter.Stats.Add(new ChapterStat("trust", "trust", 0, 0, 10, SourceRow: 0));

        editor.AddEpisode("ch01", "root", title: "root", 0, 0);
        editor.AddEpisode("ch01", "a", title: "a", 0, 0);
        editor.AddEdge("ch01", "root", "a", optionLabel: "믿는다");
        editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");

        return editor;
    }

    /// <summary>위에 더해, 판과 공급된 조건까지 선 상태.</summary>
    private static ProjectEditor WorldWithBoard()
    {
        ProjectEditor editor = World();
        string fileId = editor.EnsureChapterBoard("ch01");

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor,
            new Vn.Authoring.Definition.GameDefinition(),
            fileId,
            editor.FindChapter("ch01")!.ToGraphModel("ch01", null));

        return editor;
    }
}
