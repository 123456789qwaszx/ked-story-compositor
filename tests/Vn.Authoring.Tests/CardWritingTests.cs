using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>대사 노드 카드 안의 글 칸이 쓰는 길</b> (D4 · 2026-10-29, <c>docs/design/two-graphs.md</c> §7 D4) — 편집기 수준.
///
/// 카드는 <see cref="WorkingDialoguePreview.ScenarioText"/>를 보이고, 넣을 때
/// <see cref="ProjectEditor.ApplyScenarioText"/>를 부른다. 그 길이 지켜야 할 셋: 새 카드의 빈 첫 줄이 거절을 부르지
/// 않는다 · 넣기 한 번이 되돌리기 한 칸이다 · 보인 글을 그대로 넣으면 아무 줄도 안 바뀐다(왕복).
/// </summary>
public sealed class CardWritingTests
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" }, { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    [Fact]
    public void 새_카드에_여러_줄을_넣으면_빈_첫_줄이_첫_줄이_된다()
    {
        // ⛔ 조사에서 찾은 함정 — 새 카드는 빈 줄 하나를 들고 태어나, 여러 줄을 처음 넣으면
        //    "어느 줄이 어느 줄인지 확신할 수 없다"로 통째로 거절됐다.
        (ProjectEditor editor, DialogueNode node) = NewCard();
        string blank = Lines(editor, node).Single().Id;

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(
            node.Id, "라루: 하나\n윌로: 둘\n셋째는 지문이다.", Definition);

        Assert.True(outcome.Applied);
        Assert.Empty(outcome.Problems);

        List<ScriptLine> lines = Lines(editor, node);
        Assert.Equal(blank, lines[0].Id);
        Assert.Equal(
            [("라루", "하나"), ("윌로", "둘"), ("", "셋째는 지문이다.")],
            lines.Select(line => Said(editor, node, line.Id)));
    }

    [Fact]
    public void 넣기_한_번은_되돌리기_한_칸이다()
    {
        // 줄 동기화와 조건 전환이 따로 Mutate이던 때는 한 번 물렀을 때 반쪽이 남았다(§5-7).
        (ProjectEditor editor, DialogueNode node) = NewCard();
        ConditionDefinition condition = Condition(editor, node);
        int changes = 0;
        editor.Changed += (_, _) => changes++;

        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(node.Id, """
            라루: 처음
            <<if favor >= 5>>
            윌로: 갈래 안
            <<endif>>
            라루: 끝
            """, Definition);

        Assert.True(outcome.Applied);
        Assert.Equal(1, changes);
        Assert.Contains(
            editor.Project.FindDialogue(node.Id)!.LineExtensions.SelectMany(extension => extension.Transitions),
            transition => transition.ConditionId == condition.Id);

        editor.Undo();

        DialogueNode restored = editor.Project.FindDialogue(node.Id)!;
        Assert.Equal([("", "")], Lines(editor, restored).Select(line => Said(editor, restored, line.Id)));
        Assert.Empty(restored.LineExtensions.SelectMany(extension => extension.Transitions));
    }

    [Fact]
    public void 카드에_보인_글을_그대로_넣으면_아무_줄도_안_바뀐다()
    {
        // 쓰는 면은 왕복해야 한다 — 보인 글을 손대지 않고 넣었는데 줄이 고쳐지거나 조건이 풀리면 안 된다.
        (ProjectEditor editor, DialogueNode node) = NewCard();
        Condition(editor, node);
        editor.ApplyScenarioText(node.Id, """
            라루: 처음
            <<if favor >= 5>>
            윌로: 갈래 안
            <<endif>>
            지문 한 줄.
            """, Definition);

        string shown = WorkingDialoguePreview.ScenarioText(editor.Project, node.Id, Definition);
        ScenarioPasteOutcome again = editor.ApplyScenarioText(node.Id, shown, Definition);

        Assert.DoesNotContain("[", shown, StringComparison.Ordinal);   // 머리글 · 진단 줄이 없다
        Assert.Empty(again.Problems);
        Assert.All(again.Plan!.Entries, entry => Assert.Equal(ScriptSyncKind.Unchanged, entry.Kind));
    }

    [Fact]
    public void 새_카드의_글_칸은_비어_있다()
    {
        (ProjectEditor editor, DialogueNode node) = NewCard();

        Assert.Equal(string.Empty, WorkingDialoguePreview.ScenarioText(editor.Project, node.Id, Definition));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (ProjectEditor Editor, DialogueNode Node) NewCard()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_card", "카드", "story/card.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        return (editor, editor.AddDialogueNode(file.Id, name: "장면"));
    }

    /// <summary>favor >= 5 — 그 노드가 쓸 수 있게 설정 노드로 잇는다.</summary>
    private static ConditionDefinition Condition(ProjectEditor editor, DialogueNode node)
    {
        SetNode set = editor.AddSetNode(editor.Project.Files[0].Id, name: "설정");
        ConditionDefinition condition = editor.AddCondition(set.Id, "호감 높음", "favor >= 5");
        editor.AddSettingsLink(set.Id, node.Id);
        return condition;
    }

    private static List<ScriptLine> Lines(ProjectEditor editor, DialogueNode node) =>
        editor.Project.FindScript(node.ScriptId)!.ActiveLines.ToList();

    private static (string Speaker, string Text) Said(ProjectEditor editor, DialogueNode node, string lineId)
    {
        LocalizedLine line = editor.Project.FindScript(node.ScriptId)!.Text(lineId);
        return (line.Speaker, line.Text);
    }
}
