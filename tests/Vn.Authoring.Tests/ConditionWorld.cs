using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 조건 갈래 둘(if · elseif) · 본문 라인 · 갈래 출구 · 기본 출구 · 라인 연출을 갖춘 결정적 저작 상태 —
/// CSV · 흐름 · 재생 경로 테스트가 같은 월드 하나를 본다.
///
/// ⛔ 전에는 <c>ChoiceTests.BuildChoiceWorld</c>(선택 블록 둘)였다. 대사 안 선택지가 2026-11-04에 걷혀 조건 월드로 바뀌었다 —
/// 선택지는 챕터 간선에서만 선다.
/// </summary>
internal static class ConditionWorld
{
    internal sealed record World(
        Sample Sample,
        string Opener,
        string ElseIf,
        string Body1,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        PresentationResult Presentation);

    /// <summary>
    /// 숲 입구 → [호감 높음] 안전한 길(본문 + 연출) / [신뢰 높음] 깊은 숲 → 조건 뒤 한 줄 → 끝.
    /// 첫 갈래에 출구(A로 간다), 노드에 기본 출구.
    /// </summary>
    internal static World Build()
    {
        var sample = new Sample();

        // 실컴파일까지 가는 월드다 — 조건 식은 Yarn 문법($ 접두)이어야 한다 (골든 월드와 동일).
        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 높음", "stat(\"favor\") >= 5");
        sample.Editor.UpdateCondition(sample.ConditionB.Id, "신뢰 높음", "stat(\"trust\") >= 3");

        sample.Line("숲 입구에 도착했다.");

        string opener = sample.Line("안전한 길을 따라간다", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        string body1 = sample.Line("안전한 길에서 평범한 재료를 얻었다.");

        string elseIf = sample.Line("깊은 숲으로 들어간다", LineConditionTransition.BeginElseIf(sample.ConditionB.Id));
        sample.Line("숲 깊은 곳에서 희귀한 향신료를 발견했다.");

        sample.Line("좋아. 가보자.", LineConditionTransition.EndIf());
        sample.Line("끝.");

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "조건 연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, body1, "camera.closeup");
        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        return new World(sample, opener, elseIf, body1, dialogue, node, presentation);
    }
}
