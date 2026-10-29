using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>원고 형식 3 · 4를 둘 다 읽는다</b> (2026-10-29 · director <c>docs/plans/script-merge.md</c> S-D7 · S-D8).
///
/// director가 대본을 카드 안으로 들였다 — 형식 4는 대본이 판 파일 안 카드의 <c>script</c> 칸에 들고, 형식 3은
/// <c>script/*.vnscript.json</c>에 따로 서서 카드가 Id로 가리켰다. 이 툴은 원고를 읽기만 하므로 옮기지 않고 둘 다 읽는다.
///
/// 고정 자료 <c>tests/fixtures/director-format4</c>는 director가 견본(형식 3인 <c>samples/Authoring</c>과 같은 원고)을
/// 형식 4로 옮겨 쓴 그대로다. 두 형식을 읽은 카드는 같아야 한다.
/// </summary>
public sealed class ManuscriptFormatTests
{
    private static readonly string FormatThreeManifest =
        Path.GetFullPath("../../../../../samples/Authoring/project.vnproject.json");

    private static readonly string FormatFourManifest =
        Path.GetFullPath("../../../../../tests/fixtures/director-format4/project.vnproject.json");

    [Fact]
    public void 형식_4_원고는_카드가_제_대본을_든다()
    {
        StoryProject project = ProjectStore.Load(FormatFourManifest).Project;

        ScriptDocument scene = project.FindDialogue("nd_scene")!.Script;
        Assert.Equal(8, scene.ActiveLines.Count());
        Assert.Equal(new LocalizedLine("라루", "3분의 1이요?"), scene.Text("ln_a2"));
        Assert.Equal(
            new[] { "ln_b1", "ln_c1", "ln_d1" },
            project.FindDialogue("nd_scene")!.LineExtensions.Select(extension => extension.LineId));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(FormatFourManifest)!, "script")));
    }

    [Fact]
    public void 형식_3과_4는_같은_카드를_낸다()
    {
        StoryProject three = ProjectStore.Load(FormatThreeManifest).Project;
        StoryProject four = ProjectStore.Load(FormatFourManifest).Project;

        DialogueNode[] cards = four.EnumerateNodes().OfType<DialogueNode>().ToArray();
        Assert.Equal(4, cards.Length);

        foreach (DialogueNode card in cards)
        {
            DialogueNode twin = three.FindDialogue(card.Id)!;

            // 줄 신원 · 개정 번호 · locale별 본문이 바이트까지 같다 — 연출은 LineId에 붙고 결과는 개정 번호를 싣는다.
            Assert.Equal(
                ScriptDocumentJson.WriteBody(twin.Script).ToJsonString(),
                ScriptDocumentJson.WriteBody(card.Script).ToJsonString());
            Assert.Equal(
                twin.LineExtensions.Select(extension => extension.LineId),
                card.LineExtensions.Select(extension => extension.LineId));
        }
    }

    [Fact]
    public void 형식_4_원고에도_연출을_얹는다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vn-format4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "story"));

        try
        {
            string source = Path.GetDirectoryName(FormatFourManifest)!;
            string manifest = Path.Combine(directory, "project.vnproject.json");
            File.Copy(FormatFourManifest, manifest);
            File.Copy(
                Path.Combine(source, "story", "chapter01.vnstory.json"),
                Path.Combine(directory, "story", "chapter01.vnstory.json"));
            byte[] manuscriptBefore = File.ReadAllBytes(Path.Combine(directory, "story", "chapter01.vnstory.json"));

            CompositionLoadResult loaded = CompositionStore.Load(manifest);
            var editor = new ProjectEditor(loaded.Project);
            editor.AddPresentationNode(loaded.Project.Files[0].Id, name: "의뢰 장면 연출");
            CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

            CompositionLoadResult reopened = CompositionStore.Load(manifest);

            Assert.Equal(CompositionSource.ComposeFile, reopened.Source);
            Assert.Single(reopened.Project.EnumerateNodes().OfType<PresentationNode>());
            Assert.Equal(8, reopened.Project.FindDialogue("nd_scene")!.Script.ActiveLines.Count());

            // 원고는 읽기만 한다 — 연출을 얹어 저장해도 원고 파일은 그대로다.
            Assert.Equal(manuscriptBefore, File.ReadAllBytes(Path.Combine(directory, "story", "chapter01.vnstory.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 옛_결과의_대본_Id는_그대로_남고_새_발행은_싣지_않는다()
    {
        // 결과는 불변이다 — 2026-10-29 전에 발행된 결과는 대본 Id(`sourceScript`)를 든 채 읽히고 써지며, 제 해시와
        // 여전히 맞는다. 그 위에 같은 내용을 다시 발행하면 본문에서 대본 Id가 빠지므로 새 버전이 <b>한 번</b> 생긴다(S-D8).
        var sample = new Sample();
        sample.Line("첫 대사");
        DialogueDraft draft = DialoguePublisher.Draft(sample.Project, sample.Dialogue.Id, Sample.Definition);
        DialogueResult legacy = LegacyResult(draft, "sc_old");

        var results = new ResultRepository();
        results.Add(legacy);
        DialogueResult reread = ResultStoreJson.Read(ResultStoreJson.Write(results)).DialogueResults.Single();

        Assert.Equal("sc_old", reread.SourceScriptId);
        Assert.True(ResultHash.IsIntact(reread));

        DialogueResult next = DialoguePublisher.Publish(
            results, draft, legacy.Identity.ResultId, DateTimeOffset.UnixEpoch, out bool created);

        Assert.True(created);
        Assert.Equal(2, next.Identity.Version);
        Assert.Null(next.SourceScriptId);

        DialoguePublisher.Publish(results, draft, legacy.Identity.ResultId, DateTimeOffset.UnixEpoch, out bool again);
        Assert.False(again);   // 그 뒤로는 같은 내용이면 같은 결과다
    }

    /// <summary>초안과 같은 내용에 대본 Id를 실어 발행했던 옛 결과(v1).</summary>
    private static DialogueResult LegacyResult(DialogueDraft draft, string scriptId)
    {
        DialogueResult Build(string hash) => new(
            new ResultIdentity("rs_legacy", 1, DialogueResult.CurrentSchemaVersion, hash),
            draft.SourceNodeId,
            draft.SourceNodeName,
            scriptId,
            draft.SourceScriptRevision,
            draft.Locale,
            draft.Lines,
            draft.Assignments,
            draft.DefaultExitTargetNodeId,
            DateTimeOffset.UnixEpoch,
            draft.TrailingTransitions);

        return Build(ResultHash.Of(Build("임시")));
    }
}
