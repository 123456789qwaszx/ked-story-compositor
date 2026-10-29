using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 무대 프리뷰의 씬 선택기 (2026-08-21 소유자 — "미리 다 해둔 다음에 무대 프리뷰측에서
/// 뭘 할지 고르도록"). 그래프에서 발행·배선하던 길이 사라지고 이 콤보가 유일한 입구다:
/// 고르면 연출 채널이 자동으로 선다(EnsurePresentationChannel — Vn.Authoring 테스트가 진다).
/// </summary>
public sealed class StageScenePickerTests
{
    [Fact]
    public void 선택기는_에피소드를_앞에_커스텀_씬을_뒤에_이름_순으로_나열한다() => HeadlessUi.Run(() =>
    {
        var (preview, session) = ShowPreview();
        string fileId = session.Editor.EnsureChapterBoard("ch05");
        // 목록은 활성 판의 것만 담는다 (2026-08-26) — 앱에서는 챕터를 고르는 순간 판이
        // 활성이 되므로, 그 흐름 그대로 활성화한다.
        session.SelectFile(fileId);

        // 일부러 이름 역순으로 만든다 — 파일 순서가 아니라 이름 순임을 재기 위해서다
        // (2026-08-26 소유자: "이름 순으로 정렬 해달라").
        session.Editor.AddDialogueNode(fileId, name: "커스텀B");
        session.Editor.AddDialogueNode(fileId, name: "커스텀A");
        DialogueNode late = session.Editor.AddDialogueNode(fileId, name: "EP10");
        late.MarkedEpisodeId = "EP10";
        DialogueNode episode = session.Editor.AddDialogueNode(fileId, name: "EP00");
        episode.MarkedEpisodeId = "EP00";

        var combo = preview.FindControl<ComboBox>("SceneCombo")!;
        combo.IsDropDownOpen = true; // 열 때마다 목록을 다시 짓는다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            ["📄 EP00", "📄 EP10", "✎ 커스텀A", "✎ 커스텀B"],
            ((IEnumerable<string>)combo.ItemsSource!).ToList());
    });

    [Fact]
    public void 선택기는_고른_챕터의_씬만_담는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-26 소유자 — "선택한 챕터의 것만, 무대프리뷰 왼쪽의 노드드롭다운에
        // 표시되면 좋겠습니다." 예전에는 프로젝트 전체라 챕터가 늘수록 목록이 섞였다.
        var (preview, session) = ShowPreview();
        string ch05 = session.Editor.EnsureChapterBoard("ch05");
        string ch06 = session.Editor.EnsureChapterBoard("ch06");

        session.Editor.AddDialogueNode(ch05, name: "이쪽씬");
        session.Editor.AddDialogueNode(ch06, name: "저쪽씬");

        session.SelectFile(ch05);

        var combo = preview.FindControl<ComboBox>("SceneCombo")!;
        combo.IsDropDownOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var labels = ((IEnumerable<string>)combo.ItemsSource!).ToList();
        Assert.Contains("✎ 이쪽씬", labels);
        Assert.DoesNotContain("✎ 저쪽씬", labels);
    });

    [Fact]
    public void 씬을_고르면_대사_노드_Id로_알리고_채널이_한_번에_선다() => HeadlessUi.Run(() =>
    {
        var (preview, session) = ShowPreview();
        string fileId = session.Editor.EnsureChapterBoard("ch05");
        session.SelectFile(fileId);   // 목록은 활성 판의 것만 담는다 (2026-08-26)

        DialogueNode scene = session.Editor.AddDialogueNode(fileId, name: "커스텀A");
        ScriptLine line = session.Editor.InsertScriptLine(scene.ScriptId!);
        session.Editor.SetScriptLineText(scene.ScriptId!, line.Id, "라루", "한 줄");

        string? chosen = null;
        preview.SceneChosen += id => chosen = id;

        var combo = preview.FindControl<ComboBox>("SceneCombo")!;
        combo.IsDropDownOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var scenes = (List<DialogueNode>)combo.Tag!;
        combo.SelectedIndex = scenes.FindIndex(node => node.Id == scene.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(scene.Id, chosen);

        // MainWindow가 하는 그 호출 — 채널이 서고 연출 노드가 열릴 준비가 된다.
        PresentationChannelOutcome outcome = session.Editor.EnsurePresentationChannel(chosen!);
        Assert.True(outcome.Ready, outcome.Problem);

        // 바깥 선택이 바뀌면 콤보가 따라온다 — 이벤트는 다시 안 쏜다.
        chosen = null;
        preview.SetCurrentScene(scene.Id);
        Assert.Null(chosen);
    });

    [Fact]
    public void 챕터_선택기는_판이_있는_챕터만_담고_고르면_그_판을_찾기만_한다() => HeadlessUi.Run(() =>
    {
        // 2026-10-29 (C2b) — 원고는 ked-story-director의 것이다. 예전에는 챕터를 고르면 판이 없을 때
        // 판을 세웠는데, 이 툴은 원고를 저장하지 않으므로 그 판은 화면에만 서고 사라진다.
        var (preview, session) = ShowPreview();
        session.Editor.EnsureChapter("ch05");
        string ch05 = session.Editor.EnsureChapterBoard("ch05");
        session.Editor.EnsureChapter("ch07");   // 챕터는 있는데 판(씬)이 아직 없다
        int boards = session.Project.Files.Count;

        preview.SupplyChapters(session.ChapterEntries());
        var combo = preview.FindControl<ComboBox>("ChapterCombo")!;

        Assert.Equal(["ch05"], ((IEnumerable<string>)combo.ItemsSource!).ToList());

        combo.SelectedItem = "ch05";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(ch05, session.ActiveFileId);
        Assert.Equal(boards, session.Project.Files.Count);
    });

    private static (MiniStagePreview Preview, AuthoringSession Session) ShowPreview()
    {
        var session = new AuthoringSession();
        var preview = new MiniStagePreview();
        var window = new Window { Width = 1200, Height = 800, Content = preview };
        window.Show();
        preview.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (preview, session);
    }
}
