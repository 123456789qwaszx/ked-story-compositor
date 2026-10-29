using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

public class EditingTests
{
    [Fact]
    public void 새_노드는_파일_순서의_마지막에_생긴다()
    {
        var sample = new Sample();
        int before = sample.File.Nodes.Count;

        // 그래프에서 왼쪽 위에 놓아도 지정한 파일에서는 맨 뒤다.
        DialogueNode added = sample.Editor.AddDialogueNode(
            sample.File.Id,
            x: -500,
            y: -500,
            name: "나중에 만든 것");

        Assert.Equal(before + 1, sample.File.Nodes.Count);
        Assert.Same(added, sample.File.Nodes[^1]);
        Assert.Equal(-500, added.Layout.X);
    }

    [Fact]
    public void 첫_노드가_시작_노드가_된다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_start", "시작 파일");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        SetNode first = editor.AddSetNode(file.Id);
        editor.AddDialogueNode(file.Id);

        Assert.Equal(first.Id, editor.Project.StartNodeId);
    }

    [Fact]
    public void 되돌리기가_편집을_되살린다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");

        sample.Editor.SetScriptLineText(sample.Dialogue.Id, line, "윌로", "고친 뒤");
        Assert.Equal("고친 뒤", TextOf(sample, line));

        sample.Editor.Undo();
        Assert.Equal("처음", TextOf(sample, line));

        sample.Editor.Redo();
        Assert.Equal("고친 뒤", TextOf(sample, line));
    }

    [Fact]
    public void 편집할_때마다_알린다()
    {
        var sample = new Sample();
        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        ScriptLine line = sample.Editor.InsertScriptLine(sample.Dialogue.Id);
        sample.Editor.SetScriptLineText(sample.Dialogue.Id, line.Id, "윌로", "안녕");
        sample.Editor.SetLineTransition(
            sample.Dialogue.Id,
            line.Id,
            LineConditionTransition.BeginIf(sample.ConditionA.Id));

        Assert.Equal(3, notifications);
    }

    [Fact]
    public void 같은_값으로_다시_설정하면_기록을_남기지_않는다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        sample.Editor.SetScriptLineText(sample.Dialogue.Id, line, string.Empty, "처음");

        Assert.Equal(0, notifications);
    }

    /// <summary>
    /// 대사 노드는 본문을 소유하지 않는다. 한 줄을 고쳤을 때 바뀌는 것은 대본 하나뿐이다.
    /// </summary>
    [Fact]
    public void 화자와_대사의_권위는_대본에만_있다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");

        sample.Editor.SetScriptLineText(sample.Dialogue.Id, line, "윌로", "고친 뒤");

        Assert.Equal(new LocalizedLine("윌로", "고친 뒤"), sample.Script.Text(line));

        // 이 줄에는 조건이 없으므로 노드에 아무 항목도 남지 않는다. 본문 복사본은 어디에도 없다.
        Assert.Empty(sample.Dialogue.LineExtensions);
    }

    // ⛔ `같은_대본을_읽는_두_노드는_같은_본문을_본다`는 대본이 카드 안으로 들어오며(2026-10-29) 걷혔다 — 대본을 나눠
    //    쓰는 길이 없다. 카드마다 제 대본이다.

    private static string TextOf(Sample sample, string lineId) =>
        sample.Script.Text(lineId).Text;
    [Fact]
    public void 조건_이름과_식_수정은_구조_변경이_아니다()
    {
        var sample = new Sample();
        ProjectChangedEventArgs? change = null;
        sample.Editor.Changed += (_, args) => change = args;

        sample.Editor.UpdateCondition(sample.ConditionA.Id, "새 이름", "$favor >= 10");

        Assert.NotNull(change);
        Assert.Equal(ProjectChangeKind.ConditionDefinition, change!.Kind);
        Assert.False(change!.NeedsInspectorRebuild);
    }

    [Fact]
    public void 조건_수정은_출구_라벨에_즉시_반영된다()
    {
        var sample = new Sample();
        string opener = sample.Line(
            "조건 시작",
            LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Branch,
            opener,
            sample.TargetA.Id);

        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 매우 높음", "$favor >= 10");

        ExitPort branchPort = Assert.Single(
            NodeConnections.PortsOf(sample.Dialogue, sample.Project),
            port => port.Kind == ExitPortKind.Branch);
        Assert.Equal("호감 매우 높음", branchPort.Label);
    }

    // ⛔ <b>assignment 알림 검사 셋은 2026-09-17에 걷혔다</b> (작가 변수 폐지). 설정노드가
    //    변수를 들지 않으므로 `SetAssignments`라는 창구 자체가 없어졌다. 같은 자리의
    //    <b>조건</b> 알림 규율은 위아래에 그대로 남아 있다.


    [Fact]
    public void 대사_본문_수정은_DialogueContent로_알린다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        ProjectChangedEventArgs? change = null;
        sample.Editor.Changed += (_, args) => change = args;

        sample.Editor.SetScriptLineText(sample.Dialogue.Id, line, "라루", "수정");

        Assert.Equal(ProjectChangeKind.DialogueContent, change!.Kind);
        Assert.False(change.NeedsInspectorRebuild);
    }

}
