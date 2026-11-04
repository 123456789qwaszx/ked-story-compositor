using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 줄 태그 (2026-11-04 소유자: "줄 태그는 compositor가 저작한다"). 런타임은 2026-11-03에 대사창 커맨드를 걷고
/// 대사창 표시 · 넘김을 줄 태그(<c>#boxhide</c> · <c>#auto</c> · <c>#letterbox</c> …)로만 정했다. 원고는 읽기만 하므로(K3)
/// 태그는 연출 층이 든다 — 카탈로그 항목(execution <c>lineTag</c>)으로 줄에 붙이고, 내보내기가 그 줄의 태그로 낸다.
/// </summary>
public sealed class LineTagTests
{
    private static readonly GameDefinition Definition = GameDefinition.Parse(File.ReadAllText(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "game.definition.json"))))!;

    private sealed record Built(Sample Sample, string LineId, DialogueResult Dialogue, PresentationNode Node);

    private static Built Build(string speaker, string text)
    {
        var sample = new Sample();
        string line = sample.Line(text);
        sample.Editor.SetScriptLineText(sample.Dialogue.Id, line, speaker, text);
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);

        return new Built(sample, line, dialogue, node);
    }

    private static YarnBundle Emit(Built built) =>
        YarnBundleEmitter.Emit(
            built.Dialogue,
            built.Sample.Editor.PublishPresentation(built.Node.Id).Result,
            built.Sample.Project,
            Definition,
            bundleName: "tag_ep");

    [Fact]
    public void 줄_태그는_본문_뒤_line_태그_앞에_붙는다()
    {
        Built built = Build("", "(하린 이리저리)");
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.box_hide");
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.auto");

        YarnBundle bundle = Emit(built);

        Assert.Contains($"(하린 이리저리) #boxhide #auto #line:{built.LineId}", bundle.StoryText, StringComparison.Ordinal);
        Assert.False(bundle.HasBlockingProblems);
    }

    [Fact]
    public void 줄_태그는_커맨드_줄로_나가지_않는다()
    {
        Built built = Build("하린", "편지에는 이렇게 적혀 있었다.");
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.box_letterbox");

        YarnBundle bundle = Emit(built);

        Assert.DoesNotContain("<<#", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("편지에는 이렇게 적혀 있었다. #letterbox #line:", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 커맨드와_태그가_같은_줄에_있으면_커맨드는_줄_앞_태그는_줄_끝이다()
    {
        Built built = Build("", "캐릭터 퇴장");
        built.Sample.Editor.AddPresentationCommand(
            built.Node.Id, built.LineId, "control.frame_wait", new Dictionary<string, string> { ["n"] = "24" });
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.box_hide");
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.auto");

        string story = Emit(built).StoryText;

        int wait = story.IndexOf("<<24fr>>", StringComparison.Ordinal);
        int line = story.IndexOf("캐릭터 퇴장 #boxhide #auto #line:", StringComparison.Ordinal);

        Assert.True(wait >= 0 && line > wait, story);
    }

    [Fact]
    public void 같은_태그를_두_번_붙여도_한_번_나간다()
    {
        Built built = Build("", "연출만");
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.box_hide");
        built.Sample.Editor.AddPresentationCommand(built.Node.Id, built.LineId, "line_tag.box_hide");

        string story = Emit(built).StoryText;

        Assert.Contains("연출만 #boxhide #line:", story, StringComparison.Ordinal);
        Assert.DoesNotContain("#boxhide #boxhide", story, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup의_줄_태그는_빠지고_경고한다()
    {
        Built built = Build("하린", "첫 줄");
        built.Sample.Editor.AddPresentationSetupCommand(built.Node.Id, "line_tag.box_hide");

        YarnBundle bundle = Emit(built);

        Assert.Contains(bundle.Problems, problem => !problem.IsBlocking && problem.Message.Contains("#boxhide"));
        Assert.DoesNotContain("#boxhide", bundle.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 무대_폴드는_줄_태그를_건너뛴다()
    {
        string fixtures = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));
        StageReducerTuning tuning = RuntimeTuningLibrary.Load(fixtures, (1920, 1080)).Tuning!;
        var tag = new PresentationResultCommand(
            Identifier.PresentationCommand(), "line_tag.box_hide", new Dictionary<string, string>());

        CoreStageFoldResult result = CoreStageFold.Fold(
            PresentationCommandCatalog.Default, [], [new MiniStageFoldLine("ln_1", false, [tag])], tuning);

        Assert.Empty(result.CoreState!.Unhandled);
        Assert.Empty(result.State.Unhandled);
    }

    [Fact]
    public void 텍스트_입력은_태그_글자를_받는다()
    {
        CommandTextParseResult parsed = CommandText.Parse("#boxhide", PresentationCommandCatalog.Default);

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal("line_tag.box_hide", parsed.Definition!.Id);
    }
}
