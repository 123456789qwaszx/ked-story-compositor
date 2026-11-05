using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 갈래 인식. 선택된 갈래의 라인만 접히고, 미선택 블록은 기존 근사(전부) +
/// Unresolved 표시로 남으며, 커서가 있는 갈래는 선택을 덮는다(보고 있는 라인은 사라지지 않는다).
///
/// 갈래는 조건(if · elseif · endif)뿐이고 프레임은 0~1개다.
/// </summary>
public class BranchFlowTests
{
    private static DialogueResultLine Line(
        int index, string lineId, string text = "대사",
        ConditionTransitionKind? kind = null, string? name = null)
    {
        return new DialogueResultLine(
            index, lineId, 1, "화자", text,
            kind is { } transitionKind
                ? new DialogueResultTransition(transitionKind, "cd_x", name, "x >= 1")
                : null);
    }

    /// <summary>조건 블록: 일반 → [if 사과, 대사A] [elseif 포도, 대사B] → EndIf 라인 → 일반.</summary>
    private static DialogueResultLine[] ConditionDocument() =>
    [
        Line(0, "ln_0"),
        Line(1, "ln_ifA", "사과가 있다", ConditionTransitionKind.BeginIf, name: "사과 있음"),
        Line(2, "ln_a1", "사과 갈래 대사"),
        Line(3, "ln_elifB", "포도가 있다", ConditionTransitionKind.BeginElseIf, name: "포도 있음"),
        Line(4, "ln_b1", "포도 갈래 대사"),
        Line(5, "ln_end", "합류 대사", ConditionTransitionKind.EndIf),
    ];

    private static BranchFlow.Analysis<DialogueResultLine> Analyze(
        IReadOnlyList<DialogueResultLine> lines, StageBranchSelection selection, string? cursor)
    {
        return BranchFlow.Analyze(
            lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            // BranchAwareLines와 같은 라벨 규칙: 조건=이름(없으면 식).
            line => line.Transition?.ConditionName ?? line.Transition?.Expression ?? string.Empty,
            selection,
            cursor);
    }

    [Fact]
    public void 미선택_블록은_전부_적용하고_근사임을_표시한다()
    {
        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(ConditionDocument(), new StageBranchSelection(), cursor: "ln_end");

        Assert.All(analysis.Lines, line => Assert.True(line.Taken || line.Unresolved));
        Assert.True(analysis.Lines[2].Unresolved);  // 갈래 안 라인
        Assert.False(analysis.Lines[0].Unresolved); // 일반 흐름
        Assert.False(analysis.Lines[5].Unresolved); // End 라인부터 일반

        BranchFlow.Block block = Assert.Single(analysis.Blocks);
        Assert.Null(block.SelectedBranch);
        Assert.Equal(["사과 있음", "포도 있음"], block.Branches.Select(branch => branch.Label));
    }

    [Fact]
    public void 갈래를_고르면_그_갈래만_접힌다()
    {
        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_ifA", 1); // 포도 갈래 선택

        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(ConditionDocument(), selection, cursor: "ln_end");

        Assert.False(analysis.Lines[1].Taken); // 사과 여는 줄
        Assert.False(analysis.Lines[2].Taken); // 사과 대사
        Assert.True(analysis.Lines[3].Taken);  // 포도 여는 줄
        Assert.True(analysis.Lines[4].Taken);  // 포도 대사
        Assert.True(analysis.Lines[5].Taken);  // 합류
        Assert.All(analysis.Lines, line => Assert.False(line.Unresolved)); // 근사 아님

        Assert.Equal(1, Assert.Single(analysis.Blocks).SelectedBranch);
    }

    [Fact]
    public void 커서가_있는_갈래가_선택을_덮는다()
    {
        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_ifA", 1); // 포도 선택 상태에서

        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(ConditionDocument(), selection, cursor: "ln_a1"); // 사과 갈래 라인을 본다

        Assert.True(analysis.Lines[2].Taken);  // 보고 있는 라인은 접힌다
        Assert.False(analysis.Lines[4].Taken); // 포도는 이번엔 아님
        Assert.Equal(0, Assert.Single(analysis.Blocks).SelectedBranch);
    }

    [Fact]
    public void 조건_블록은_참_갈래나_전부_거짓을_고를_수_있다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_if", "호감 높은 대사", ConditionTransitionKind.BeginIf, name: "호감 높음"),
            Line(2, "ln_if2", "이어지는 대사"),
            Line(3, "ln_elif", "호감 낮은 대사", ConditionTransitionKind.BeginElseIf, name: "호감 낮음"),
            Line(4, "ln_end", "합류", ConditionTransitionKind.EndIf),
        ];

        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_if", 0);

        BranchFlow.Analysis<DialogueResultLine> first = Analyze(document, selection, cursor: "ln_end");
        Assert.True(first.Lines[1].Taken);
        Assert.True(first.Lines[2].Taken);
        Assert.False(first.Lines[3].Taken);
        Assert.Equal(["호감 높음", "호감 낮음"], Assert.Single(first.Blocks).Branches.Select(branch => branch.Label));

        selection.SelectCondition("ln_if", StageBranchSelection.SkipAllBranches);

        BranchFlow.Analysis<DialogueResultLine> skipped = Analyze(document, selection, cursor: "ln_end");
        Assert.False(skipped.Lines[1].Taken);
        Assert.False(skipped.Lines[2].Taken);
        Assert.False(skipped.Lines[3].Taken);
        Assert.True(skipped.Lines[4].Taken); // 합류는 일반 흐름
    }

    [Fact]
    public void 시작_없는_elseif는_새_블록으로_취급해_조용히_삼키지_않는다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_elif", "홀로 선 elseif", ConditionTransitionKind.BeginElseIf, name: "홀로"),
            Line(2, "ln_end", "합류", ConditionTransitionKind.EndIf),
        ];

        BranchFlow.Analysis<DialogueResultLine> analysis = Analyze(document, new StageBranchSelection(), cursor: null);

        BranchFlow.Block block = Assert.Single(analysis.Blocks);
        Assert.Equal("ln_elif", block.BlockLineId);
        Assert.Equal(["홀로"], block.Branches.Select(branch => branch.Label));
        Assert.True(analysis.Lines[1].Unresolved);
    }

    [Fact]
    public void 칩_순환은_갈래들을_돌고_건너뜀을_거쳐_근사로_돌아온다()
    {
        var selection = new StageBranchSelection();
        var block = new BranchFlow.Block(
            "ln_if",
            [new BranchFlow.Branch("ln_if", "참"), new BranchFlow.Branch("ln_elif", "다른 참")],
            SelectedBranch: null);

        selection.Cycle(block); // → 0
        Assert.True(selection.TryGetCondition("ln_if", out int index) && index == 0);

        selection.Cycle(block); // → 1
        Assert.True(selection.TryGetCondition("ln_if", out index) && index == 1);

        selection.Cycle(block); // → 건너뜀
        Assert.True(selection.TryGetCondition("ln_if", out index) &&
            index == StageBranchSelection.SkipAllBranches);

        selection.Cycle(block); // → 미선택(근사)
        Assert.False(selection.TryGetCondition("ln_if", out _));
    }

    [Fact]
    public void 갈래_인식_폴드_입력은_선택_라인까지_선택_갈래만_담는다()
    {
        DialogueResult dialogue = new(
            new ResultIdentity("rs_bf", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_bf", "장면", "sc_bf", 1, "ko-KR",
            ConditionDocument(),
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);

        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_ifA", 0); // 사과 갈래

        BranchAwareLines.Result result = BranchAwareLines.UpTo(
            dialogue, Array.Empty<PresentationResultBinding>(), "ln_end", selection);

        Assert.Equal(
            ["ln_0", "ln_ifA", "ln_a1", "ln_end"],
            result.FoldLines.Select(line => line.LineId));
        Assert.All(result.FoldLines, line => Assert.False(line.HasBranchTransition)); // 근사 아님
        Assert.Equal(result.FoldLines.Select(line => line.LineId), result.TakenLines.Select(line => line.LineId));

        BranchFlow.Block block = Assert.Single(result.Blocks);
        Assert.Equal("ln_ifA", block.BlockLineId);

        // 커서가 블록 앞이면 블록은 아직 칩에 없다.
        BranchAwareLines.Result before = BranchAwareLines.UpTo(
            dialogue, Array.Empty<PresentationResultBinding>(), "ln_0", selection);
        Assert.Empty(before.Blocks);
    }

    [Fact]
    public void 미선택이면_폴드_입력이_기존_근사와_같고_근사_표시가_남는다()
    {
        DialogueResult dialogue = new(
            new ResultIdentity("rs_bf2", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_bf2", "장면", "sc_bf2", 1, "ko-KR",
            ConditionDocument(),
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);

        BranchAwareLines.Result result = BranchAwareLines.UpTo(
            dialogue, Array.Empty<PresentationResultBinding>(), "ln_end", new StageBranchSelection());

        // 기존 LinesUpTo와 같은 라인 집합(전부) — 갈래 라인에만 근사 표시.
        Assert.Equal(
            MiniStageFold.LinesUpTo(dialogue, Array.Empty<PresentationResultBinding>(), "ln_end")
                .Select(line => line.LineId),
            result.FoldLines.Select(line => line.LineId));
        Assert.True(result.FoldLines.First(line => line.LineId == "ln_a1").HasBranchTransition);
        Assert.False(result.FoldLines.First(line => line.LineId == "ln_0").HasBranchTransition);
    }
}
