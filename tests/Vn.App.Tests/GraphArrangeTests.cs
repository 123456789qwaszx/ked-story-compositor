using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// <b>연출 그래프의 [자동 정렬]</b> (D3-d · 2026-10-29, <c>docs/design/two-graphs.md</c> §7 D3).
///
/// 새 카드는 처음부터 위→아래로 서지만 이미 선 카드는 저절로 옮기지 않는다(§5-14) — 판 전체를 다시 세우는
/// 일은 사람이 누르는 이 단추이고 되돌리기 한 칸이다. 자리 계산 자체는 <c>NodePlacementTests</c>가 본다.
/// </summary>
public sealed class GraphArrangeTests
{
    private const int GraphTab = 1;

    [Fact]
    public void 자동_정렬은_고른_챕터의_판을_위에서_아래로_세우고_되돌리기_한_칸이다() => HeadlessUi.Run(() =>
    {
        (MainWindow window, AuthoringSession session) = Open();
        session.Editor.EnsureChapter("ch01");
        session.Editor.AddEpisode("ch01", "root", "복도", 0, 0);
        session.Editor.AddNextEpisode("ch01", "root", "next", "옥상", 0, 0, optionLabel: "간다");
        session.SelectFile(EpisodeNaming.BoardOf(session.Project, "ch01")!.Id);

        // 사람이 흐트러뜨린 판 — 다음 에피소드가 시작보다 위에 있다.
        string root = EpisodeNaming.CardFor(session.Project, "ch01", "root")!.Id;
        string next = EpisodeNaming.CardFor(session.Project, "ch01", "next")!.Id;
        session.Editor.MoveNode(root, 0, 900);
        session.Editor.MoveNode(next, 700, 0);
        Dispatcher.UIThread.RunJobs();

        int changes = 0;
        session.Changed += (_, _) => changes++;

        Click(window);

        Assert.True(Below(session, next, root), "정렬 뒤에는 다음 에피소드가 시작 아래에 서야 합니다");
        Assert.Equal(1, changes);

        session.Editor.Undo();

        Assert.Equal((0, 900), Spot(session, root));
        Assert.Equal((700, 0), Spot(session, next));

        window.Close();
    });

    [Fact]
    public void 챕터_판이_아니면_말로_돌려보내고_아무것도_옮기지_않는다() => HeadlessUi.Run(() =>
    {
        (MainWindow window, AuthoringSession session) = Open();
        DialogueNode loose = session.Editor.AddDialogueNode(session.ActiveFile!.Id, 500, 500, "낙서");
        long revision = session.Editor.Revision;

        Click(window);

        Assert.Equal(revision, session.Editor.Revision);
        Assert.Equal((500, 500), Spot(session, loose.Id));
        Assert.Contains("챕터 판", session.StatusMessage, StringComparison.Ordinal);

        window.Close();
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (double X, double Y) Spot(AuthoringSession session, string nodeId)
    {
        StoryNode node = session.Project.FindNode(nodeId)!;
        return (node.Layout.X, node.Layout.Y);
    }

    /// <summary>위의 카드와 같은 기둥이고 한 줄(<see cref="NodePlacement.Row"/>) 아래다.</summary>
    private static bool Below(AuthoringSession session, string lowerId, string upperId)
    {
        (double X, double Y) lower = Spot(session, lowerId);
        (double X, double Y) upper = Spot(session, upperId);

        return lower.X == upper.X && lower.Y == upper.Y + NodePlacement.Row;
    }

    private static void Click(MainWindow window)
    {
        window.FindControl<GraphEditorView>("Graph")!
            .FindControl<Button>("ArrangeBoardButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, AuthoringSession Session) Open()
    {
        var window = new MainWindow();
        window.Show();
        window.FindControl<TabControl>("MainTabs")!.SelectedIndex = GraphTab;
        Dispatcher.UIThread.RunJobs();

        return (window, window.SessionProbe);
    }
}
