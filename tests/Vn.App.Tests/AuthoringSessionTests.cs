using System.Text;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

public class AuthoringSessionTests
{
    [Fact]
    public void 원고를_열기_전에는_쓸_자리가_없고_원고를_열라고_말한다()
    {
        // 이 툴은 원고를 만들지 않는다(director의 일) — 연출 층 파일은 원고 옆에 산다.
        var session = new AuthoringSession();

        Assert.Null(session.ManuscriptPath);
        Assert.Null(session.ProjectPath);
        Assert.Equal(AuthoringSession.NoManuscriptMessage, session.StatusMessage);
        Assert.Throws<InvalidOperationException>(() => session.Save());
    }

    [Fact]
    public void 프로젝트를_열면_시작_노드가_속한_파일이_현재_파일이_된다()
    {
        var project = new StoryProject { Title = "테스트" };
        var first = new StoryFile("sf_a", "A", "story/a.vnstory.json");
        var second = new StoryFile("sf_b", "B", "story/b.vnstory.json");
        project.Files.Add(first);
        project.Files.Add(second);
        first.Nodes.Add(new DialogueNode("nd_a", "A"));
        second.Nodes.Add(new DialogueNode("nd_b", "B"));
        project.StartNodeId = "nd_b";

        string directory = TempDirectory();
        string path = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);

        try
        {
            ProjectStore.Save(path, project);
            var session = new AuthoringSession();

            session.Open(path);

            Assert.Equal(second.Id, session.ActiveFileId);
            Assert.Equal("nd_b", session.SelectedNodeId);
            Assert.Equal(Path.GetFullPath(path), session.ManuscriptPath);
            Assert.Equal(CompositionStore.PathFor(path), session.ProjectPath);   // 이 툴의 파일은 원고 옆에
            Assert.False(session.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void dirty_비교는_디스크가_아니라_ProjectSnapshotCodec을_사용한다()
    {
        var session = new AuthoringSession();
        StoryFile file = session.ActiveFile!;

        Assert.False(session.IsDirty);
        DialogueNode node = session.Editor.AddDialogueNode(file.Id, name: "새 장면");
        Assert.True(session.IsDirty);

        session.Editor.Undo();
        Assert.False(session.IsDirty);

        session.Editor.Redo();
        Assert.True(session.IsDirty);
        Assert.NotNull(session.Project.FindNode(node.Id));
    }

    /// <summary>
    /// 이전 형식은 자동 마이그레이션하지 않고 거부한다. 이 테스트는 그 마이그레이션을
    /// 검증하던 테스트를 대체한다. 열지 못한 뒤에도 세션은 원래 상태로 계속 쓸 수 있어야 한다.
    /// </summary>
    [Fact]
    public void 이전_형식을_열면_거부하고_세션은_그대로_남는다()
    {
        string directory = TempDirectory();
        string legacyPath = Path.Combine(directory, "legacy.vnproject.json");
        File.WriteAllText(legacyPath, """
            {
              "formatVersion": 2,
              "title": "이전",
              "files": []
            }
            """, new UTF8Encoding(false));

        try
        {
            var session = new AuthoringSession();
            string? pathBefore = session.ProjectPath;
            int nodesBefore = session.Project.EnumerateNodes().Count();

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => session.Open(legacyPath));

            Assert.Contains("더 이상 열 수 없습니다", error.Message, StringComparison.Ordinal);
            Assert.Equal(pathBefore, session.ProjectPath);
            Assert.Equal(nodesBefore, session.Project.EnumerateNodes().Count());
            Assert.False(session.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void 새_노드는_ActiveFile의_마지막에_추가된다()
    {
        var session = new AuthoringSession();
        StoryFile first = session.ActiveFile!;
        StoryFile second = session.Editor.AddStoryFile("두 번째");
        SetNode existing = session.Editor.AddSetNode(second.Id, name: "기존");

        session.SelectFile(second.Id);
        DialogueNode added = session.Editor.AddDialogueNode(session.ActiveFileId!, name: "추가");

        Assert.Empty(first.Nodes);
        Assert.Same(added, second.Nodes[^1]);
        Assert.Equal(new[] { existing.Id, added.Id }, second.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void 파일_선택은_현재_파일_변경으로만_알린다()
    {
        var session = new AuthoringSession();
        StoryFile second = session.Editor.AddStoryFile("두 번째");
        var changes = new List<FileGraphStateChangedEventArgs>();
        session.FileGraphStateChanged += (_, e) => changes.Add(e);

        session.SelectFile(second.Id);

        FileGraphStateChangedEventArgs active = Assert.Single(changes);
        Assert.True(active.ActiveFileChanged);
        Assert.False(active.FileListChanged);
    }

    [Fact]
    public void 파일_추가와_노드_개수_변경은_파일_목록_갱신으로_알린다()
    {
        var session = new AuthoringSession();
        var changes = new List<FileGraphStateChangedEventArgs>();
        session.FileGraphStateChanged += (_, e) => changes.Add(e);

        StoryFile added = session.Editor.AddStoryFile("추가");
        session.Editor.AddDialogueNode(added.Id, name: "장면");

        Assert.Equal(2, changes.Count);
        Assert.All(changes, change => Assert.True(change.FileListChanged));
    }

    // ── 튜닝 관리 (W46) ───────────────────────────────────────────────────

    [Fact]
    public void 기본_튜닝_생성은_규약_폴더에_내장_파일을_쓰고_바로_읽힌다()
    {
        string directory = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(directory);

            session.CreateDefaultTuning();

            string tuningRoot = Path.Combine(directory, "ExportedTuning");
            Assert.True(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "rig-schemas.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "presets", "depth.json")));
            Assert.True(session.TuningLibrary.IsLoaded);
            Assert.True(session.TuningLibrary.RigCount > 0);

            // ⛔ <b>기본 튜닝은 로더가 찾는 것을 하나도 빠뜨리지 않는다</b> (2026-08-26).
            //    파일 이름을 몇 개 세는 것으로는 이걸 못 지킨다 — 로더에 축이 하나 늘면
            //    (W64의 `presets/role-anchor.json`이 그랬다) 여기 목록은 조용히 통과하고
            //    새 프로젝트는 <b>열 때마다 경고</b>를 맞는다. 실제로 그렇게 났다.
            //    불평 목록이 비어 있는지를 걸면 다음에 축이 늘 때 이 시험이 먼저 운다.
            Assert.Empty(session.TuningLibrary.Problems);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기본_튜닝_생성은_저장_전에는_안내만_하고_기존_폴더를_덮지_않는다()
    {
        var unsaved = new AuthoringSession();
        unsaved.CreateDefaultTuning(); // 저장 전 — 예외 없이 안내만
        Assert.Contains("저장", unsaved.StatusMessage);

        string directory = TempDirectory();

        try
        {
            // 이미 튜닝이 있는 폴더에 저장한다 — 첫 저장의 자동 준비(W48)도 불가침을 지킨다.
            string tuningRoot = Path.Combine(directory, "ExportedTuning");
            Directory.CreateDirectory(tuningRoot);
            File.WriteAllText(Path.Combine(tuningRoot, "custom.json"), "{}");

            AuthoringSession session = OpenFresh(directory);

            Assert.False(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));

            session.CreateDefaultTuning(); // 내용이 있는 폴더 — 불가침

            Assert.False(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "custom.json")));
            Assert.Contains("덮어쓰지 않습니다", session.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 튜닝_폴더_연결은_규약_자리로_복사하고_엉뚱한_폴더는_거른다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(directory);

            session.ConnectTuningFolder(source); // 튜닝 파일이 없는 폴더
            Assert.Contains("찾지 못했습니다", session.StatusMessage);

            File.WriteAllText(
                Path.Combine(source, "base-resolution.json"),
                """{"referenceResolution":{"x":1920,"y":1080}}""");
            Directory.CreateDirectory(Path.Combine(source, "presets"));
            File.WriteAllText(Path.Combine(source, "presets", "depth.json"), "{}");

            session.ConnectTuningFolder(source);

            string tuningRoot = Path.Combine(directory, "ExportedTuning");
            Assert.True(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "presets", "depth.json")));
            Assert.True(session.TuningLibrary.IsLoaded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    // ── 새 프로젝트 자동 준비 + PNG 가져오기 (W48) ────────────────────────

    [Fact]
    public void 첫_저장은_에셋_폴더와_기본_튜닝을_준비한다()
    {
        string directory = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(directory);

            Assert.Equal("assets/backgrounds", session.Project.AssetRoots.BackgroundsPath);
            Assert.Equal("assets/portraits", session.Project.AssetRoots.PortraitsPath);
            Assert.Equal("assets/bgm", session.Project.AssetRoots.BgmPath);   // W59
            Assert.Equal("assets/sfx", session.Project.AssetRoots.SfxPath);
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "backgrounds")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "portraits")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "bgm")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "sfx")));
            Assert.True(session.TuningLibrary.IsLoaded); // 기본 튜닝이 자동으로 깔려 연결된다
            Assert.False(session.IsDirty); // 준비 과정이 미저장 변경을 남기지 않는다

            // 두 번째 저장은 준비를 반복하지 않는다 — 상태 메시지가 담백하다.
            session.Save();
            Assert.DoesNotContain("기본 튜닝", session.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 배경_가져오기는_복제하고_같은_이름은_건너뛴다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(directory);

            string png = Path.Combine(source, "room_day.png");
            File.WriteAllBytes(png, [1, 2, 3]);

            Assert.Equal(1, session.ImportBackgrounds([png]));
            Assert.True(File.Exists(Path.Combine(directory, "assets", "backgrounds", "room_day.png")));

            Assert.Equal(0, session.ImportBackgrounds([png])); // 같은 이름 — 덮지 않는다
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void 저장은_없는_폴더를_복구한다()
    {
        // W60: 실수로 지운 폴더는 다음 저장이 되살린다. 내용이 있는 폴더는 불가침이다.
        // (⛔ [다른 이름으로…]의 새 자리 살림은 2026-10-29에 그 단추와 함께 걷혔다 — 이 툴의 파일은 원고 옆 한 자리다.)
        string first = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(first);

            Directory.Delete(Path.Combine(first, "assets", "bgm"), recursive: true);
            session.Save(); // 같은 자리 재저장 — 지워진 폴더가 되살아난다
            Assert.True(Directory.Exists(Path.Combine(first, "assets", "bgm")));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
        }
    }

    [Fact]
    public void 오디오_가져오기는_복제하고_저장_후_다시_열어도_루트가_남는다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(directory);
            string projectPath = session.ManuscriptPath!;

            string clip = Path.Combine(source, "main_theme.mp3");
            File.WriteAllBytes(clip, [1, 2]);
            string ignored = Path.Combine(source, "노트.txt");
            File.WriteAllText(ignored, "오디오 아님");

            Assert.Equal(1, session.ImportAudio(bgm: true, [clip, ignored])); // 규약 외 확장자는 걸러진다
            Assert.True(File.Exists(Path.Combine(directory, "assets", "bgm", "main_theme.mp3")));
            Assert.Equal(["main_theme"], session.AudioClipKeys(session.BgmRoot));

            // 오디오 루트는 저장본에 남는다 (W59 직렬화).
            session.Save();
            var reopened = new AuthoringSession();
            reopened.Open(projectPath);
            Assert.Equal("assets/bgm", reopened.Project.AssetRoots.BgmPath);
            Assert.Equal("assets/sfx", reopened.Project.AssetRoots.SfxPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void 초상화_가져오기는_규약_자리에_표정_번호를_이어_붙인다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            AuthoringSession session = OpenFresh(directory);

            string first = Path.Combine(source, "웃음.png");
            string second = Path.Combine(source, "화남.png");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [2]);

            Assert.Equal(2, session.ImportPortraits("willow", 'a', [first, second]));

            string folder = Path.Combine(directory, "assets", "portraits", "willow", "a");
            Assert.True(File.Exists(Path.Combine(folder, "01.png")));
            Assert.True(File.Exists(Path.Combine(folder, "02.png")));

            Assert.Equal(1, session.ImportPortraits("willow", 'a', [first]));
            Assert.True(File.Exists(Path.Combine(folder, "03.png"))); // 빈 번호를 이어 쓴다
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>
    /// director가 쓴 것 같은 빈 원고 하나를 깔고 연 뒤 한 번 저장한다(연출 층 파일 · 에셋 · 튜닝이 준비된다).
    /// 이 툴은 원고를 만들지 않으므로 원고는 <see cref="ProjectStore"/>로 직접 쓴다.
    /// </summary>
    private static AuthoringSession OpenFresh(string directory)
    {
        string manuscript = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        var project = new StoryProject { Title = "원고" };
        project.Files.Add(new StoryFile(name: "기본 파일"));
        ProjectStore.Save(manuscript, project);

        var session = new AuthoringSession();
        session.Open(manuscript);
        session.Save();
        return session;
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Session.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
