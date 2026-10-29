using Avalonia.Controls;
using Avalonia.Interactivity;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>간선 패널의 자동 저장</b> — 2026-08-17 소유자: "간선을 편집할 때 굳이 적용을 누르지 않아도
/// 바로 변화가 반영되도록".
///
/// ⛔ 이 파일의 앞 절(엑셀 잠금 — 2026-08-17 "엑셀이 열려있으면 … 잠그고", 2026-10-29 C6 "잠금은 편집을
/// 막지 않는다")은 2026-10-29에 엑셀 잠금 감시 · 배너와 함께 걷혔다(docs/design/two-graphs.md §7 D2).
/// </summary>
public sealed class ChapterEditGateTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 간선 패널은 고치는 순간 저장된다 ───────────────────────────────────

    [Fact]
    public void 조건을_고르면_적용_없이_엑셀에_써진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";

        Assert.Equal("지쳐있음", ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A").VisibleConditionLabel);
    });

    [Fact]
    public void 문구를_바꾼_직후에_고른_조건도_써진다() => HeadlessUi.Run(() =>
    {
        // 문구는 간선의 신원이다 — 한 번 고치면 손에 든 모델이 낡는다. 그 상태로 다음
        // 저장을 부르면 간선을 못 찾아 조건이 조용히 안 써졌다(이 테스트가 잡았다).
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem = "혼자 문을 연다";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";

        ChapterEdge reread = ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A");

        Assert.Equal("혼자 문을 연다", reread.OptionLabel);
        Assert.Equal("지쳐있음", reread.VisibleConditionLabel);
    });

    [Fact]
    public void 잠금_안내문은_초점을_잃을_때_써진다() => HeadlessUi.Run(() =>
    {
        // 자판 하나마다 워크북을 열면 엑셀 파일을 쉼 없이 두드리고, 그 사이 파일 사건이
        // 칸을 다시 채워 쓰던 글을 끊는다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        string? before = Edge(project).LockedMessage;

        var box = view.FindControl<TextBox>("EdgeLockedMsgBox")!;
        box.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        box.Text = "아직 이르다";

        Assert.Equal(before, Edge(project).LockedMessage);   // 치는 동안은 안 나간다

        // 초점을 진짜로 옮긴다 — LostFocus는 FocusChangedEventArgs를 요구한다.
        view.FindControl<ComboBox>("EdgeVisibleCombo")!.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("아직 이르다", Edge(project).LockedMessage);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterEdge Edge(TempProject project) =>
        ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A");

    private static (ChapterGraphView View, AuthoringSession Session) Show(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (view, session);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        /// <param name="secondChapter">
        /// 같은 내용의 ch06을 하나 더 둔다 — 챕터를 옮길 때 잠금이 따라오는지 보는 용도다.
        /// </param>
        public TempProject(bool secondChapter = false)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-edit-gate", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(SamplePath, ChapterPath);

            if (secondChapter)
            {
                File.Copy(
                    SamplePath,
                    Path.Combine(_directory, ChapterLibrary.FolderName, "ch06.xlsx"));
            }

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            var project = new StoryProject { Title = "편집 스위치 검증" };
            project.Files.Add(new StoryFile("sf_main", "본편", "story/main.vnstory.json"));
            ProjectStore.Save(ManifestPath, project);
        }

        public string ManifestPath { get; }

        public string ChapterPath =>
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
