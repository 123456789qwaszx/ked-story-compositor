using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Vn.App.Views;

namespace Vn.App.Tests;

/// <summary>
/// 창의 모양 — <b>창은 무대 하나다</b> (2026-10-29). 이야기의 뼈대와 대사를 쓰는 두 그래프(챕터 그래프 ·
/// 연출 그래프)와 대사 편집기는 ked-story-director에 남았고, 이 툴은 무대에서 연출을 얹는다.
/// 어느 씬을 연출할지는 무대의 챕터 · 씬 선택기가 고른다(docs/plans/compositor-split.md C1).
/// </summary>
public sealed class MainChromeTests
{
    [Fact]
    public void 창은_무대_하나다_그래프_탭이_없다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(window.FindControl<TabControl>("MainTabs"));
        Assert.Single(Live<MiniStagePreview>(window));
        Assert.Null(window.FindControl<Control>("ChapterGraph"));
        Assert.Null(window.FindControl<Control>("Graph"));

        window.Close();
    });

    [Fact]
    public void 상태줄은_곁눈으로_읽는_크기다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — "저장했습니다, 로드했습니다 이런 문구의 크기를 줄여줘."
        // 기본 글자크기(14)로 두면 창 아래를 늘 크게 차지하는데, 여기 뜨는 말은 대부분
        // "됐다"이고 그것은 크게 볼 것이 아니다. 이 저장소가 곁정보에 쓰는 11로 맞춘다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(11, window.FindControl<TextBlock>("StatusText")!.FontSize);

        window.Close();
    });

    /// <summary>지금 화면에 <b>실제로 선</b> 컨트롤들.</summary>
    private static T[] Live<T>(MainWindow window) where T : Control =>
        window.GetVisualDescendants().OfType<T>().ToArray();

    [Fact]
    public void 위_띠에는_Yarn_내보내기가_있고_진행_JSON_폴더는_없다() => HeadlessUi.Run(() =>
    {
        // 게임용 Yarn은 이 툴만 낸다(K4). 진행 JSON은 ked-story-director가 내므로 그 폴더를 여는 단추는
        // 2026-10-29에 걷혔다. CSV · 양식은 임시로 접어 둔 그대로다(2026-08-25 소유자).
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<Button>("ExportButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("CsvExportButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("ExportFormatsButton")!.IsVisible);
        Assert.Null(window.FindControl<Button>("OpenExportFolderButton"));

        window.Close();
    });

    [Fact]
    public void 편집_자료는_사라지고_에셋은_무대_프리뷰_안에_남는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자 — "이제 편집자료는 굳이 표시할 필요가 없는 것 같아".
        // 대본·발행 결과·연출 공급을 나열하던 현황판인데, 발행·배선이 자동이 된 뒤로는
        // 볼 이유가 없다. 에셋 탐색기는 남되, 2026-08-26에 무대 프리뷰로 이사했다 —
        // 에셋을 보며 고르는 화면이 무대라서다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(window.FindControl<ScrollViewer>("ResourceScroll"));
        Assert.Null(window.FindControl<ToggleButton>("ResourceCollapseToggle"));

        var stage = window.FindControl<MiniStagePreview>("StagePreview")!;
        Assert.Contains(stage, Assert.Single(Live<AssetExplorerView>(window)).GetVisualAncestors());

        window.Close();
    });
}
