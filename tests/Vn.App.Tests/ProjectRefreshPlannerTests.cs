using Vn.App.Services;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// 변경 한 번이 화면에 무엇을 다시 하게 하는가 — 다시 만드는 순간 사람이 누르던 칸이 사라지므로,
/// 꼭 필요한 것만 다시 한다. (⛔ 그래프 · 대사 편집기 쪽 계약은 2026-10-29에 그 화면들과 함께
/// ked-story-director에 남았다.)
/// </summary>
public class ProjectRefreshPlannerTests
{
    [Fact]
    public void 구조_변경은_챕터_목록과_연출_편집기를_다시_만든다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Structure,
            new PresentationNode(name: "연출"));

        Assert.True(plan.RefreshChapters);
        Assert.True(plan.RebuildInspector);
    }

    [Fact]
    public void Presentation_Command_편집은_현재_연출_편집기를_재생성하지_않고_무대만_다시_접는다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.PresentationContent,
            new PresentationNode(name: "연출"));

        Assert.False(plan.RefreshChapters);
        Assert.False(plan.RebuildInspector);
        Assert.True(plan.RefreshStagePreview);
    }

    [Fact]
    public void Dialogue_내용이_바뀌면_선택된_Presentation의_읽기_전용_행을_다시_만든다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.DialogueContent,
            new PresentationNode(name: "연출"));

        Assert.True(plan.RebuildInspector);
        Assert.False(plan.RefreshStagePreview);
    }

    [Fact]
    public void Presentation_link_변경은_선택된_Presentation의_대상과_orphan을_다시_계산한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Connections,
            new PresentationNode(name: "연출"));

        Assert.True(plan.RebuildInspector);
    }

    /// <summary>발행은 편집 행을 바꾸지 않는다 — 연출 편집기가 읽는 결과만 새로 읽는다.</summary>
    [Fact]
    public void 발행은_연출_편집기만_다시_읽고_챕터_목록은_그대로다()
    {
        ProjectRefreshPlan presentation = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Results,
            new PresentationNode(name: "연출"));
        ProjectRefreshPlan dialogue = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Results,
            new DialogueNode(name: "장면"));

        Assert.True(presentation.RebuildInspector);
        Assert.False(presentation.RefreshChapters);
        Assert.False(dialogue.RebuildInspector);
    }

    [Fact]
    public void 노드_이름이_바뀌면_씬_선택기가_읽는_목록을_다시_공급한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.NodeMetadata,
            new PresentationNode(name: "연출"));

        Assert.True(plan.RefreshChapters);
        Assert.False(plan.RebuildInspector);
    }

}
