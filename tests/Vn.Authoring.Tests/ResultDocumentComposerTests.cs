using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 발행 결과를 평평한 문서로 펼쳐도 조건 의미와 원본 식별자가 사라지지 않는지 검증한다.
/// Preview 문자열보다 Segment 목록이 먼저이며, Formatter는 그 목록을 읽기만 해야 한다.
///
/// 이전 <c>DialogueDocumentComposerTests</c>를 대체한다. 그때는 편집 중인 노드와 실시간으로
/// 연결된 연출을 직접 합성했다. 지금 정식 출력의 입력은 얼어붙은 결과 두 개뿐이다.
/// </summary>
public class ResultDocumentComposerTests
{

    [Fact]
    public void 조건_전환과_갈래_출구를_실행_순서대로_평평하게_합성한다()
    {
        var sample = new Sample();
        var (_, l1, l2, l3, l4, l5, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l3, sample.TargetB.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        RenderedDocument document = ResultDocumentComposer.Compose(result, project: sample.Project);

        RenderedSegment[] meaningful = document.Segments
            .Where(segment => segment.Kind != RenderedSegmentKind.SetAssignment)
            .ToArray();

        // 갈래 출구의 소유자는 여는 줄이지만(§4.2), 실행은 갈래의 끝에서 일어난다.
        // 출구가 여는 줄 바로 뒤에 나오면 갈래 본문이 그 아래 묻혀 실행되지 않는다.
        // 조건 갈래의 출구는 detour다 — 커스텀 씬을 재생하고 돌아온다.
        Assert.Equal(
            new[]
            {
                RenderedSegmentKind.NodeHeader,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.ConditionBegin,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.BranchDetour,
                RenderedSegmentKind.ConditionElseIf,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.BranchDetour,
                RenderedSegmentKind.ConditionEnd,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.DefaultJump,
                RenderedSegmentKind.NodeFooter
            },
            meaningful.Select(segment => segment.Kind));

        RenderedSegment firstJump = meaningful.Single(segment =>
            segment.Kind == RenderedSegmentKind.BranchDetour &&
            segment.TargetNodeId == sample.TargetA.Id);
        RenderedSegment secondJump = meaningful.Single(segment =>
            segment.Kind == RenderedSegmentKind.BranchDetour &&
            segment.TargetNodeId == sample.TargetB.Id);

        // 갈래 출구는 화면상 마지막 줄이 아니라 갈래를 여는 안정된 LineId를 소유한다.
        Assert.Equal(l1, firstJump.Source.LineId);
        Assert.Equal(l3, secondJump.Source.LineId);
        Assert.Equal(sample.Dialogue.Id, firstJump.Source.NodeId);
        Assert.Equal(result.Identity.ResultId, firstJump.Source.DialogueResultId);

        RenderedSegment end = meaningful.Single(segment =>
            segment.Kind == RenderedSegmentKind.ConditionEnd);
        Assert.Equal(l5, end.Source.LineId);

        Assert.Contains(meaningful, segment => segment.Source.LineId == l2);
        Assert.Contains(meaningful, segment => segment.Source.LineId == l4);
    }

    /// <summary>
    /// 발행 시점의 조건 이름과 식을 결과가 함께 얼린다.
    /// 나중에 조건을 지워도 이미 발행한 결과는 같은 문서를 다시 만들어야 한다.
    /// </summary>
    [Fact]
    public void 결과는_조건이_삭제된_뒤에도_같은_식을_재현한다()
    {
        var sample = new Sample();
        sample.Line("조건 대사", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        sample.Editor.RemoveCondition(sample.ConditionA.Id);

        RenderedDocument document = ResultDocumentComposer.Compose(result, project: sample.Project);
        RenderedSegment condition = Assert.Single(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.ConditionBegin);

        Assert.Equal("favor >= 5", condition.Expression);
        Assert.Equal(sample.ConditionA.Id, condition.Source.ConditionId);
    }

    [Fact]
    public void 대사_Segment는_결과와_LineId_원본을_유지한다()
    {
        var sample = new Sample();
        string line = sample.Line("원본 위치");

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        RenderedDocument document = ResultDocumentComposer.Compose(result, project: sample.Project);
        RenderedSegment segment = Assert.Single(document.Segments, item =>
            item.Kind == RenderedSegmentKind.DialogueLine);

        Assert.Equal(sample.Dialogue.Id, segment.Source.NodeId);
        Assert.Equal(line, segment.Source.LineId);
        Assert.Equal(result.Identity.ResultId, segment.Source.DialogueResultId);
    }


    [Fact]
    public void 비활성_Command는_결과에_들어가지_않으므로_문서에도_없다()
    {
        var context = new CompositionFixture();
        PresentationCommandInstance disabled = context.Sample.Editor.AddPresentationCommand(
            context.Presentation.Id,
            context.LineId,
            "screen.flash");
        context.Sample.Editor.SetPresentationCommandEnabled(
            context.Presentation.Id,
            disabled.Id,
            enabled: false);

        RenderedDocument document = ResultDocumentComposer.Compose(
            context.Dialogue,
            context.PublishPresentation(),
            context.Sample.Project);

        Assert.DoesNotContain(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.PresentationCommand);
    }

    [Fact]
    public void 연출이_없는_LineId는_대사만_출력된다()
    {
        var context = new CompositionFixture();
        RenderedDocument document = ResultDocumentComposer.Compose(
            context.Dialogue,
            context.PublishPresentation(),
            context.Sample.Project);

        Assert.DoesNotContain(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.PresentationCommand);
        Assert.Contains(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine && segment.Source.LineId == context.LineId);
    }
}

/// <summary>대사 결과 하나와 그것을 읽는 연출 노드를 갖춘 작은 설정.</summary>
internal sealed class CompositionFixture
{
    public CompositionFixture()
    {
        Sample = new Sample();
        LineId = Sample.Line("연출 대사");
        Dialogue = Sample.Editor.PublishDialogue(Sample.Dialogue.Id).Result;

        Presentation = Sample.Editor.AddPresentationNode(Sample.File.Id, name: "카메라 연출");
        Sample.Editor.SetPresentationSource(
            Presentation.Id,
            Dialogue.Identity.ResultId,
            Dialogue.Identity.Version);
    }

    public Sample Sample { get; }

    public string LineId { get; }

    public DialogueResult Dialogue { get; }

    public PresentationNode Presentation { get; }

    public PresentationResult PublishPresentation() =>
        Sample.Editor.PublishPresentation(Presentation.Id).Result;
}
