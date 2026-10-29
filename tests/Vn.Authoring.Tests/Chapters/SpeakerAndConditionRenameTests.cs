using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>조건의 이름을 편집해도 연결이 이어진다</b> (2026-08-24 소유자: "조건이나 화자의
/// 이름을 편집한 경우도 마찬가지입니다").
///
/// 줄에 매달린 전환은 <c>ConditionId</c>로 잇는다. 그래서 라벨이 바뀌었을 때 조건을 <em>새로</em>
/// 만들면 Id가 갈려 이미 매달린 갈래가 전부 고아가 된다. 같은 식이면 같은 조건이므로 <b>이름만</b>
/// 갈아 끼운다. (화자 개명은 2026-10-29에 ked-story-director에 남았다 — 원고의 줄을 고치는 일이다.)
///
/// (2026-10-29 · D2: 대본 워크북을 다시 내던 일과 그 검사(잠김·미룸)는 엑셀 연동과 함께 걷혔다.)
/// </summary>
public sealed class SpeakerAndConditionRenameTests
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" } ] }
        """)!;

    // ── 조건 라벨 ───────────────────────────────────────────────────────────

    /// <summary>스탯 하나를 견주는 챕터 조건 하나짜리 모델. 라벨만 갈아 끼울 수 있다.</summary>
    private static ChapterGraphModel ChapterWith(string label) => new(
        chapterId: "ch01",
        sourcePath: "ch01",
        episodes: [],
        edges: [],
        conditions: [
            new ChapterCondition(
                label,
                "trust >= 3",
                Description: null,
                Parsed: [new ConditionTerm(ConditionTermKind.StatComparison, "trust", ConditionComparison.AtLeast, 3)],
                IsValid: true,
                SourceRow: 2)
        ],
        stats: [],
        fixtures: [],
        diagnostics: []);

    [Fact]
    public void 조건_라벨을_바꾸면_이름만_따라가고_매달린_갈래는_산다()
    {
        // ⛔ 이것이 무너지면 라벨 한 글자에 그 조건을 쓰던 갈래가 전부 "알 수 없는 조건"이 된다.
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");

        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "본문");
        ScriptLine line = dialogue.Script.ActiveLines.Single();   // 카드는 빈 첫 줄을 들고 선다

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        SetNode supply = file.Nodes.OfType<SetNode>()
            .Single(node => ChapterBoardSupply.IsConditionSupplyNode(node, file));
        string conditionId = supply.Conditions.Single().Id;

        editor.SetLineTransitions(dialogue.Id, line.Id, [
            LineConditionTransition.BeginIf(conditionId),
            LineConditionTransition.EndIf()
        ]);

        // 라벨이 바뀌었다 — 식은 그대로다.
        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰가 높다"));

        ConditionDefinition condition = Assert.Single(supply.Conditions);

        Assert.Equal(conditionId, condition.Id);          // 신원은 그대로 — 갈래가 산다
        Assert.Equal("신뢰가 높다", condition.Name);       // 이름은 따라간다
        Assert.Equal(
            conditionId,
            dialogue.FindExtension(line.Id)!.Transitions[0].ConditionId);
    }

    [Fact]
    public void 라벨이_그대로면_조건에_손대지_않는다()
    {
        // 같은 값을 다시 쓰면 되돌리기 기록이 쌓이고 화면이 다시 그려진다.
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        long revision = editor.Revision;

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        Assert.Equal(revision, editor.Revision);
    }

    // ── 화자 ────────────────────────────────────────────────────────────────

    private sealed record World(ProjectEditor Editor, ScriptDocument Script, ScriptLine Line);

    /// <summary>챕터 ch01 · 그 판의 에피소드 카드 하나 · 화자가 적힌 줄 하나가 선 프로젝트.</summary>
    private static World BuildProject(string speaker, string episodeId = "main01.01")
    {
        var editor = new ProjectEditor(new StoryProject());
        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");

        // 챕터 판에 세우면 에피소드도 함께 선다 — 이름이 곧 EpisodeId다.
        DialogueNode node = editor.AddDialogueNode(fileId, name: episodeId);
        ScriptDocument script = node.Script;
        ScriptLine line = script.ActiveLines.First();
        editor.SetScriptLineText(node.Id, line.Id, speaker, "문을 연다");

        return new World(editor, script, line);
    }

}
