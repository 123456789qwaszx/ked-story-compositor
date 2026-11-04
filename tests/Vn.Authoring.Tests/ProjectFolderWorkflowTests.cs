using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>두 툴은 같은 폴더를 연다</b> (2026-11-04 소유자). director가 쓴 폴더를 이 툴이 열면 연출 파일이 그 옆에 선다.
/// 연출이 없는 원고에는 다른 폴더의 연출을 <b>가져오고</b>(복사 — 원고는 읽기만), 열 때 대사가 바뀐 카드는
/// <b>저절로 다시 묶어</b> 결과만 말한다. 게임 폴더는 폴더 안 <c>runtime.target.json</c> 하나로 director와 나눈다.
/// </summary>
public sealed class ProjectFolderWorkflowTests
{
    private static string Temp()
    {
        string path = Path.Combine(Path.GetTempPath(), "vn-compose-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>director가 써 둔 모양의 원고 — 카드 하나, 줄 둘.</summary>
    private static (string Manifest, string NodeId, string FirstLine, string SecondLine) Manuscript(string folder)
    {
        var sample = new Sample();
        string first = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Dialogue.Id, first, "하린", "첫 줄");
        string second = sample.Line("둘째 줄");
        sample.Editor.SetScriptLineText(sample.Dialogue.Id, second, "하린", "둘째 줄");

        string manifest = ProjectFolder.NewManifestPathIn(folder);
        ProjectStore.Save(manifest, sample.Project);

        return (manifest, sample.Dialogue.Id, first, second);
    }

    [Fact]
    public void 폴더의_manifest를_찾아_열면_연출_파일은_그_옆에_선다()
    {
        string folder = Temp();

        try
        {
            (string manifest, string nodeId, string first, _) = Manuscript(folder);

            ProjectFolder.Found found = ProjectFolder.Find(folder);
            Assert.Equal(manifest, found.ManifestPath);

            CompositionLoadResult loaded = CompositionStore.Load(found.ManifestPath!);
            Assert.Equal(CompositionSource.None, loaded.Source);
            Assert.Equal(Path.Combine(folder, "project.vncompose.json"), loaded.ComposePath);

            var editor = new ProjectEditor(loaded.Project);
            PresentationNode channel = editor.EnsurePresentationChannel(nodeId).Presentation!;
            editor.AddPresentationCommand(channel.Id, first, "char_rig_presentation.fade_in",
                new Dictionary<string, string> { ["slot"] = "s1", ["duration"] = "14fr" });
            CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

            Assert.True(File.Exists(Path.Combine(folder, "project.vncompose.json")));
            Assert.True(File.Exists(Path.Combine(folder, "results.vnresults.json")));
            // 원고는 그대로다 (K3).
            Assert.Equal(CompositionSource.ComposeFile, CompositionStore.Load(found.ManifestPath!).Source);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void 연출이_없는_원고에_다른_폴더의_연출을_가져온다()
    {
        string a = Temp();
        string b = Temp();

        try
        {
            (string manifestA, string nodeId, string first, _) = Manuscript(a);

            // A에서 연출을 붙여 저장한다.
            CompositionLoadResult loadedA = CompositionStore.Load(manifestA);
            var editorA = new ProjectEditor(loadedA.Project);
            PresentationNode channel = editorA.EnsurePresentationChannel(nodeId).Presentation!;
            editorA.AddPresentationCommand(channel.Id, first, "char_rig_presentation.fade_in",
                new Dictionary<string, string> { ["slot"] = "s1", ["duration"] = "14fr" });
            CompositionStore.Save(loadedA.ComposePath, manifestA, editorA.Project);

            // B는 같은 원고의 사본(탐색기 복사)인데 연출이 없다 — 이름도 다르다.
            string manifestB = Path.Combine(b, "festival.vnproject.json");
            File.Copy(manifestA, manifestB);
            foreach (string story in Directory.GetFiles(Path.Combine(a, "story")))
            {
                Directory.CreateDirectory(Path.Combine(b, "story"));
                File.Copy(story, Path.Combine(b, "story", Path.GetFileName(story)));
            }

            Assert.Equal(CompositionSource.None, CompositionStore.Load(manifestB).Source);

            int dropped = CompositionStore.ImportLayerFrom(loadedA.ComposePath, manifestB);

            Assert.Equal(0, dropped);
            Assert.True(File.Exists(Path.Combine(b, "festival.vncompose.json")));
            CompositionLoadResult loadedB = CompositionStore.Load(manifestB);
            Assert.Equal(CompositionSource.ComposeFile, loadedB.Source);
            Assert.Single(loadedB.Project.FindPresentation(channel.Id)!.Bindings);

            // 같은 폴더의 것을 다시 가져오라는 요청은 뜻이 없다.
            Assert.Throws<InvalidOperationException>(() => CompositionStore.ImportLayerFrom(loadedB.ComposePath, manifestB));
        }
        finally
        {
            Directory.Delete(a, recursive: true);
            Directory.Delete(b, recursive: true);
        }
    }

    [Fact]
    public void 대사가_바뀐_카드는_열_때_다시_묶이고_결과만_말한다()
    {
        string folder = Temp();

        try
        {
            (string manifest, string nodeId, string first, string second) = Manuscript(folder);

            CompositionLoadResult loaded = CompositionStore.Load(manifest);
            var editor = new ProjectEditor(loaded.Project);
            PresentationNode channel = editor.EnsurePresentationChannel(nodeId).Presentation!;
            editor.AddPresentationCommand(channel.Id, first, "char_rig_presentation.fade_in",
                new Dictionary<string, string> { ["slot"] = "s1", ["duration"] = "14fr" });
            editor.AddPresentationCommand(channel.Id, second, "char_rig_cast.face",
                new Dictionary<string, string> { ["slot"] = "s1", ["emotionKey"] = "2" });
            CompositionStore.Save(loaded.ComposePath, manifest, editor.Project);

            // 바뀐 것이 없으면 조용하다.
            CompositionRefresh.Report quiet = CompositionRefresh.Apply(new ProjectEditor(CompositionStore.Load(manifest).Project), null);
            Assert.False(quiet.ChangedAnything);
            Assert.Null(quiet.Summary);

            // director가 둘째 줄을 지우고 셋째 줄을 더했다 — 원고만 다시 쓴다.
            StoryProject manuscript = ProjectStore.Load(manifest).Project;
            var writer = new ProjectEditor(manuscript);
            DialogueNode card = manuscript.FindDialogue(nodeId)!;
            // 이 툴에는 작가 조작이 없다(K7) — director가 한 일을 모델에 그대로 적는다.
            card.Script.Lines.Single(line => line.Id == second).IsRetired = true;
            string third = writer.InsertScriptLine(card.Id).Id;
            writer.SetScriptLineText(card.Id, third, "하린", "셋째 줄");
            ProjectStore.Save(manifest, manuscript);

            var reopened = new ProjectEditor(CompositionStore.Load(manifest).Project);
            CompositionRefresh.Report report = CompositionRefresh.Apply(reopened, null);

            Assert.Equal(1, report.DialogueChanged);
            Assert.Equal(1, report.CommandsCarried);   // 첫 줄의 fade_in
            Assert.Equal(1, report.Orphans);           // 사라진 둘째 줄의 face — 지우지 않는다
            Assert.Equal(1, report.NewLines);          // 셋째 줄
            Assert.Empty(report.Failures);
            Assert.Contains("따라온 명령 1", report.Summary);
            Assert.Contains("사라진 줄의 명령 1", report.Summary);
            Assert.Contains("연출 없는 줄 1", report.Summary);

            // 다시 묶였으니 내보내기 경고가 없다.
            LiveComposition composition = LiveNodeComposer.Compose(reopened.Project, nodeId, GameDefinition.Empty, DateTimeOffset.UnixEpoch);
            Assert.True(composition.CanWrite);
            Assert.DoesNotContain(composition.Warnings, warning => warning.Contains("발행본"));
            Assert.Contains("셋째 줄", composition.Bundle!.StoryText);
            Assert.True(reopened.Project.FindDialogue(nodeId)!.Script.ActiveLines.Any(line => line.Id == third));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void 게임_폴더_설정은_director와_같은_파일이다()
    {
        string folder = Temp();

        try
        {
            (string manifest, _, _, _) = Manuscript(folder);
            string game = Path.Combine(folder, "game");
            Directory.CreateDirectory(game);

            Assert.Null(RuntimeTarget.LoadBeside(manifest));
            RuntimeTarget.SaveBeside(manifest, RuntimeTarget.FromAbsolute(manifest, game));

            RuntimeTarget target = RuntimeTarget.LoadBeside(manifest)!;
            Assert.Equal("game", target.GameFolder);
            Assert.Equal(Path.GetFullPath(game), target.ResolveExisting(manifest));

            // director가 쓴 형식 그대로 읽힌다 — 같은 클래스의 사본이다.
            Assert.Contains("\"gameFolder\": \"game\"", File.ReadAllText(RuntimeTarget.PathFor(manifest)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
