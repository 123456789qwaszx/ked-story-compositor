using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 프레임 대기 <c>&lt;&lt;Nfr&gt;&gt;</c> (2026-11-04). 카탈로그는 런타임의 <c>1fr</c>~<c>48fr</c>을 정의 하나
/// (<c>control.frame_wait</c>, 출력 이름 <c>&lt;N&gt;fr</c>)로 묶는다. 이름 안의 자리표를 채우는 규칙이 없어서
/// <c>&lt;&lt;&lt;N&gt;fr 24&gt;&gt;</c>가 그대로 나갔고 — 런타임이 모르는 커맨드다 — 입력도 <c>24fr</c>를 몰랐다.
/// 이름을 짓는 자리(Yarn · 무대 폴드)와 읽는 자리(텍스트 입력)가 <see cref="CommandText"/>의 한 규칙을 쓴다.
/// </summary>
public sealed class FrameWaitCommandTests
{
    private const string FrameWait = "control.frame_wait";

    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static Dictionary<string, string> N(string value) => new(StringComparer.Ordinal) { ["n"] = value };

    [Fact]
    public void 자리표_이름은_첫_인자로_채우고_인자에서_뺀다()
    {
        PresentationCommandDefinition definition = Catalog.Find(FrameWait)!;

        Assert.True(CommandText.IsNameTemplate(definition));
        Assert.Equal("<<24fr>>", CommandText.Format(definition, FrameWait, N("24")));

        (string name, IReadOnlyList<OrderedCommandArgument> arguments) =
            CommandText.ResolveInvocation(definition, FrameWait, N("12"));

        Assert.Equal("12fr", name);
        Assert.Empty(arguments);
    }

    [Fact]
    public void 자리표가_없는_정의는_그대로다()
    {
        PresentationCommandDefinition pause = Catalog.FindByOutputCommand("pause")!;

        Assert.False(CommandText.IsNameTemplate(pause));
        Assert.Equal("<<pause 24fr>>", CommandText.Format(pause, "pause", new Dictionary<string, string> { ["duration"] = "24fr" }));
    }

    [Theory]
    [InlineData("<<24fr>>", "24")]
    [InlineData("12fr", "12")]
    [InlineData("1fr", "1")]
    [InlineData("48fr", "48")]
    public void 입력은_이름_안의_값을_첫_인자로_돌린다(string input, string expected)
    {
        CommandTextParseResult parsed = CommandText.Parse(input, Catalog);

        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(FrameWait, parsed.Definition!.Id);
        Assert.Equal(expected, parsed.Arguments!["n"]);
    }

    [Theory]
    [InlineData("49fr")]
    [InlineData("0fr")]
    [InlineData("xfr")]
    public void 런타임에_없는_이름은_입력에서_막는다(string input)
    {
        CommandTextParseResult parsed = CommandText.Parse(input, Catalog);

        Assert.False(parsed.Success);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void 범위_밖이면_pause를_안내한다()
    {
        string? error = CommandText.ValidateNameTemplate(Catalog.Find(FrameWait), N("60"));

        Assert.NotNull(error);
        Assert.Contains("pause", error);
        Assert.Null(CommandText.ValidateNameTemplate(Catalog.Find(FrameWait), N("24")));
    }

    [Fact]
    public void 무대_폴드는_런타임과_같은_이름을_코어에_넘긴다()
    {
        string fixtures = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));
        StageReducerTuning tuning = RuntimeTuningLibrary.Load(fixtures, (1920, 1080)).Tuning!;

        var wait = new PresentationResultCommand(Identifier.PresentationCommand(), FrameWait, N("24"));
        CoreStageFoldResult result = CoreStageFold.Fold(Catalog, [], [new MiniStageFoldLine("ln_1", false, [wait])], tuning);

        // 코어는 Nfr을 "접을 것이 없는 성공"으로 받는다 — 모르는 이름(<N>fr)이면 Unhandled가 남았다.
        Assert.Empty(result.CoreState!.Unhandled);
        Assert.DoesNotContain(result.State.Unhandled, item => item.CommandName.Contains('<'));
    }

    [Fact]
    public void Yarn에는_Nfr로_나간다()
    {
        string definitionPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "game.definition.json"));
        GameDefinition definition = GameDefinition.Parse(File.ReadAllText(definitionPath))!;

        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Dialogue.Id, first, "라루", "첫 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, first, FrameWait, N("24"));
        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(dialogue, presentation, sample.Project, definition, bundleName: "wait_ep");

        Assert.Contains("<<24fr>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("<N>", bundle.StoryText, StringComparison.Ordinal);
        Assert.False(bundle.HasBlockingProblems);
    }
}
