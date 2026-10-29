using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>개명은 참조를 끌고 간다</b> (2026-08-24 소유자: "설정 노드에서 아이템/능력의 이름을
/// 바꿨을 때 … 기존 것이 미등록으로 되면서 연결이 끊어지는데, 이게 연결이 계속 이어지도록").
///
/// 아이템·능력은 <b>이름이 곧 신원</b>이다. 그것을 붙드는 두 자리 — 조건식의 <c>$이름</c>과
/// 대사 줄의 <c>&lt;&lt;set 이름&gt;&gt;</c> — 도 이름 문자열 하나뿐이라, 등록 쪽에서만 갈면
/// 나머지가 전부 미아가 된다. 이 파일이 지키는 것은 그 <b>한 번의 개명이 세 자리를 함께
/// 움직이는가</b>이다.
///
/// 반대편 빗장도 함께 지킨다: <b>추측해서 잇지 않는다</b>. 줄이 생기거나 사라졌을 때,
/// 이름이 비슷하기만 할 때, A계층 공급 노드의 식일 때는 손대지 않는다.
///
/// (화자 개명이 줄을 끌고 가던 검사는 2026-10-29에 화자 개명과 함께 ked-story-director에 남았다.)
/// </summary>
public sealed class RenameCarriesReferencesTests
{
    private sealed record World(
        ProjectEditor Editor, StoryFile File, SetNode SetNode, DialogueNode Dialogue, string LineId);

    /// <summary>설정노드 하나(아이템 `열쇠`) · 그것을 쓰는 조건 하나 · 그것을 쓰는 대사 줄 하나.</summary>
    private static World Build(bool ability = false)
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");

        SetNode setNode = editor.AddSetNode(file.Id, name: "ch01 설정");

        editor.AddCondition(setNode.Id, "열쇠 있음", ability ? "$열쇠 == true" : "$열쇠 >= 1");

        ScriptDocument script = editor.AddScript("본문");
        ScriptLine line = editor.InsertScriptLine(script.Id);
        editor.SetScriptLineText(script.Id, line.Id, "라루", "문을 연다");

        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "본문", scriptId: script.Id);

        return new World(editor, file, setNode, dialogue, line.Id);
    }

    // ⛔ <b>변수 개명 전파 검사는 2026-09-17에 통째로 걷혔다</b> (소유자: *"작가 변수라는
    //    개념 자체를 지웁시다"*). 아이템·능력 이름을 바꾸면 조건식과 줄의 `<<set>>`이
    //    함께 따라가는지를 재던 자리인데, 그 어휘가 없어져 끌고 갈 참조가 없다.
    //
    // 남은 것은 <b>조건</b>과 <b>화자</b>의 개명이다 — 둘 다 그대로 산다.

    // ── 조건 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 조건_이름을_바꿔도_매달린_갈래는_그대로다()
    {
        // 작가가 설정노드에서 고치는 조건 이름 — 줄에 매달린 전환은 <b>Id</b>로 잇는다.
        // 이 성질이 깨지는 날은 조건에 Id가 없어진 날이고, 그러면 이름 한 글자에 그 조건을
        // 쓰던 갈래가 전부 "알 수 없는 조건"이 된다.
        World world = Build();
        ConditionDefinition condition = world.SetNode.Conditions[0];

        world.Editor.SetLineTransitions(world.Dialogue.Id, world.LineId, [
            LineConditionTransition.BeginIf(condition.Id),
            LineConditionTransition.EndIf()
        ]);

        world.Editor.UpdateCondition(condition.Id, "보물열쇠 있음", condition.Expression);

        Assert.Equal("보물열쇠 있음", world.SetNode.Conditions[0].Name);
        Assert.Equal(
            condition.Id,
            world.Dialogue.FindExtension(world.LineId)!.Transitions[0].ConditionId);

        // 그리고 그 조건이 여전히 <b>고를 수 있는 목록</b>에 새 이름으로 서 있다.
        AvailableCondition? available = AvailableConditionResolver
            .Resolve(world.Editor.Project, world.Dialogue.Id)
            .Find(condition.Id);

        Assert.Equal("보물열쇠 있음", available?.DisplayName);
    }

    // ── 화자 ────────────────────────────────────────────────────────────────

}
