using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>원고는 읽기만, 연출은 이 툴의 파일에</b> (2026-10-29, <c>docs/plans/compositor-split.md</c> K3 · C2).
///
/// 원고(챕터 · 판 · 대사 · 대본)의 주인은 ked-story-director다. 이 툴은 원고 옆 <c>.vncompose.json</c>에
/// 연출 층만 쓰고, 열 때 둘을 합친다. 옛 프로젝트(연출이 원고 안에 있던 판)에서는 연출을 가져온다.
/// </summary>
public sealed class CompositionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vn-compose-" + Guid.NewGuid().ToString("N"));

    public CompositionStoreTests() => Directory.CreateDirectory(_root);

    private string Manuscript => Path.Combine(_root, "예제" + ProjectManifestJson.FileExtension);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void 저장은_원고를_건드리지_않고_연출_층만_원고_옆에_쓴다()
    {
        (string dialogueId, _) = WriteManuscript();
        Dictionary<string, byte[]> before = ManuscriptBytes();

        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);
        Assert.Equal(CompositionSource.None, loaded.Source);
        Assert.Equal(Path.Combine(_root, "예제" + CompositionStore.FileExtension), loaded.ComposePath);

        var editor = new ProjectEditor(loaded.Project);
        PresentationNode presentation = EnterChannel(editor, dialogueId);

        CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

        // 원고는 한 바이트도 안 바뀌었다 — 그 파일의 주인은 director다.
        Assert.Equal(before, ManuscriptBytes());
        Assert.True(File.Exists(loaded.ComposePath));
        Assert.True(File.Exists(Path.Combine(_root, ResultStoreJson.DefaultFileName)));

        // 다시 열면 둘이 합쳐진다.
        CompositionLoadResult reopened = CompositionStore.Load(Manuscript);
        Assert.Equal(CompositionSource.ComposeFile, reopened.Source);
        Assert.NotNull(reopened.Project.FindPresentation(presentation.Id));
        Assert.Contains(reopened.Project.Links, link =>
            link.Kind == NodeLinkKind.PresentationSupply &&
            link.SourceNodeId == presentation.Id && link.TargetNodeId == dialogueId);
        Assert.NotEmpty(reopened.Project.Results.DialogueResults);
    }

    [Fact]
    public void 이_툴의_파일로도_열린다()
    {
        (string dialogueId, _) = WriteManuscript();
        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);
        var editor = new ProjectEditor(loaded.Project);
        EnterChannel(editor, dialogueId);
        CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

        CompositionLoadResult viaCompose = CompositionStore.Load(loaded.ComposePath);

        Assert.Equal(Path.GetFullPath(Manuscript), viaCompose.ManuscriptPath);
        Assert.Equal(CompositionSource.ComposeFile, viaCompose.Source);
    }

    [Fact]
    public void director가_판을_옮겨도_연출은_대사_노드의_판에_다시_선다()
    {
        (string dialogueId, string boardId) = WriteManuscript();
        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);
        var editor = new ProjectEditor(loaded.Project);
        PresentationNode presentation = EnterChannel(editor, dialogueId);
        CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

        // director 쪽에서 판을 새로 세우고 대사 노드를 그리로 옮겼다(옛 판은 사라졌다).
        StoryProject manuscript = ProjectStore.Load(Manuscript).Project;
        StoryFile old = manuscript.FindFile(boardId)!;
        var moved = new StoryFile("sf_new", "새 판");
        manuscript.Files.Remove(old);
        moved.Nodes.AddRange(old.Nodes);
        manuscript.Files.Add(moved);
        ProjectStore.Save(Manuscript, manuscript);

        CompositionLoadResult reopened = CompositionStore.Load(Manuscript);

        Assert.Equal("sf_new", reopened.Project.FindFileContainingNode(presentation.Id)!.Id);
        Assert.Equal(0, reopened.DroppedLinks);
    }

    [Fact]
    public void 원고에서_사라진_대사를_가리키던_배선은_버리고_연출_노드는_남긴다()
    {
        (string dialogueId, _) = WriteManuscript();
        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);
        var editor = new ProjectEditor(loaded.Project);
        PresentationNode presentation = EnterChannel(editor, dialogueId);
        CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

        // director 쪽에서 그 대사 노드를 지웠다.
        StoryProject manuscript = ProjectStore.Load(Manuscript).Project;
        foreach (StoryFile file in manuscript.Files)
        {
            file.Nodes.RemoveAll(node => node.Id == dialogueId);
        }

        manuscript.StartNodeId = null;
        ProjectStore.Save(Manuscript, manuscript);

        CompositionLoadResult reopened = CompositionStore.Load(Manuscript);

        Assert.Equal(1, reopened.DroppedLinks);
        Assert.NotNull(reopened.Project.FindPresentation(presentation.Id));
    }

    [Fact]
    public void 원고_안의_옛_연출을_가져오고_저장은_원고를_그대로_둔다()
    {
        // 연출이 아직 원고 파일 안에 있던 옛 프로젝트(VnTool 시절 · director R3 전).
        WriteManuscript(withPresentation: true);
        Dictionary<string, byte[]> before = ManuscriptBytes();

        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);

        Assert.Equal(CompositionSource.EmbeddedInManuscript, loaded.Source);
        Assert.Contains(loaded.Project.EnumerateNodes(), node => node is PresentationNode);

        CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, loaded.Project);

        Assert.Equal(before, ManuscriptBytes());
        Assert.Equal(CompositionSource.ComposeFile, CompositionStore.Load(Manuscript).Source);
    }

    [Fact]
    public void 이_툴의_파일이_있으면_원고_안의_옛_연출은_읽지_않는다()
    {
        WriteManuscript(withPresentation: true);
        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);

        // 이 툴에서 옛 연출을 지우고 저장했다 — 원고 안의 옛 연출이 되살아나면 안 된다.
        var editor = new ProjectEditor(loaded.Project);
        foreach (PresentationNode node in editor.Project.EnumerateNodes().OfType<PresentationNode>().ToList())
        {
            editor.RemoveNode(node.Id);
        }

        CompositionStore.Save(loaded.ComposePath, loaded.ManuscriptPath, editor.Project);

        CompositionLoadResult reopened = CompositionStore.Load(Manuscript);

        Assert.Equal(CompositionSource.ComposeFile, reopened.Source);
        Assert.DoesNotContain(reopened.Project.EnumerateNodes(), node => node is PresentationNode);
    }

    [Fact]
    public void director가_남긴_옛_원본에서_연출을_가져온다()
    {
        // director가 옛 프로젝트를 열어 원본을 presentation-original/에 남기고, 연출 없이 다시 저장한 뒤다.
        (string dialogueId, _) = WriteManuscript(withPresentation: true);
        string backup = Path.Combine(_root, CompositionStore.PresentationBackupFolder);
        CopyDirectory(_root, backup);

        StoryProject manuscriptOnly = ProjectStore.Load(Manuscript).Project;
        StoryProject stripped = PresentationLayer.Merge(manuscriptOnly, PresentationLayer.Empty(), out _);
        File.Delete(Path.Combine(_root, ResultStoreJson.DefaultFileName));
        ProjectStore.Save(Manuscript, stripped);

        CompositionLoadResult loaded = CompositionStore.Load(Manuscript);

        Assert.Equal(CompositionSource.PresentationOriginal, loaded.Source);
        Assert.Contains(loaded.Project.Links, link =>
            link.Kind == NodeLinkKind.PresentationSupply && link.TargetNodeId == dialogueId);
    }

    /// <summary>판 하나 · 대사 노드 하나(대본 한 줄)인 원고를 쓴다. 연출을 넣으면 옛 프로젝트 모양이다.</summary>
    private (string DialogueId, string BoardId) WriteManuscript(bool withPresentation = false)
    {
        var editor = new ProjectEditor(new StoryProject { Title = "원고" });
        StoryFile board = editor.AddStoryFile("ch01");
        ScriptDocument script = editor.AddScript("본문");
        DialogueNode dialogue = editor.AddDialogueNode(board.Id, name: "ep01", scriptId: script.Id);
        ScriptLine line = editor.InsertScriptLine(script.Id);
        editor.SetScriptLineText(script.Id, line.Id, "라루", "첫 줄");

        if (withPresentation)
        {
            EnterChannel(editor, dialogue.Id);
        }

        ProjectStore.Save(Manuscript, editor.Project);
        return (dialogue.Id, board.Id);
    }

    private static PresentationNode EnterChannel(ProjectEditor editor, string dialogueId)
    {
        PresentationChannelOutcome outcome = editor.EnsurePresentationChannel(dialogueId, Sample.Definition);
        Assert.True(outcome.Ready, outcome.Problem);
        return outcome.Presentation!;
    }

    /// <summary>원고에 속한 파일들(manifest · 대본 · StoryFile)의 바이트 — 이 툴의 파일과 결과 파일은 뺀다.</summary>
    private Dictionary<string, byte[]> ManuscriptBytes() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(path =>
                !path.EndsWith(CompositionStore.FileExtension, StringComparison.Ordinal) &&
                !path.EndsWith(ResultStoreJson.FileExtension, StringComparison.Ordinal) &&
                !path.Contains(CompositionStore.PresentationBackupFolder, StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(_root, path), File.ReadAllBytes);

    private static void CopyDirectory(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
                     .Where(path => !path.StartsWith(to, StringComparison.Ordinal)))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
