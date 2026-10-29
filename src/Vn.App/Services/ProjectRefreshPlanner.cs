using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.App.Services;

/// <summary>
/// 도메인 변경 한 번이 화면에 어떤 갱신을 요구하는지 결정한다.
///
/// 이 판단을 MainWindow의 switch 안에 흩어 놓으면 변경 종류를 추가할 때마다 편집 컨트롤을
/// 불필요하게 다시 만드는 회귀가 생긴다 — 다시 만드는 순간 사람이 누르던 칸이 사라진다.
///
/// ⛔ 그래프 · 대사 편집기 · 카드 글 칸의 갱신은 2026-10-29에 걷혔다 — 두 그래프와 대사 편집이
/// ked-story-director의 것이 되면서 이 창은 무대 하나다(docs/plans/compositor-split.md C1).
/// </summary>
internal static class ProjectRefreshPlanner
{
    public static ProjectRefreshPlan Plan(ProjectChangeKind kind, StoryNode? selectedNode)
    {
        return kind switch
        {
            ProjectChangeKind.Structure => new ProjectRefreshPlan(
                RefreshChapters: true,
                RebuildInspector: true),

            // 연출 편집기는 대사 줄을 읽기 전용으로 비춘다 — 대사가 바뀌면 그 행을 다시 만든다.
            ProjectChangeKind.DialogueContent => new ProjectRefreshPlan(
                RefreshChapters: false,
                RebuildInspector: selectedNode is PresentationNode),

            // 연출 편집기의 커맨드 행은 유지하되 무대 프리뷰는 새 커맨드를 접어야 한다.
            ProjectChangeKind.PresentationContent => new ProjectRefreshPlan(
                RefreshChapters: false,
                RebuildInspector: false,
                RefreshStagePreview: selectedNode is PresentationNode),

            // 연출의 입력 결과(공급 연결)가 바뀌면 읽기 전용 미러와 orphan 판정이 달라진다.
            ProjectChangeKind.Connections => new ProjectRefreshPlan(
                RefreshChapters: false,
                RebuildInspector: selectedNode is PresentationNode),

            // 발행은 편집 행을 바꾸지 않는다 — 연출이 읽는 결과만 새로 읽는다.
            ProjectChangeKind.Results => new ProjectRefreshPlan(
                RefreshChapters: false,
                RebuildInspector: selectedNode is PresentationNode),

            // 노드 이름은 씬 선택기가 읽는다.
            ProjectChangeKind.NodeMetadata => new ProjectRefreshPlan(
                RefreshChapters: true,
                RebuildInspector: false),

            _ => default
        };
    }
}

/// <param name="RefreshChapters">무대의 챕터 · 씬 선택기가 읽는 목록을 다시 공급한다.</param>
/// <param name="RebuildInspector">고른 연출 노드의 편집기를 다시 만든다.</param>
/// <param name="RefreshStagePreview">무대가 지금 프레임을 다시 접는다.</param>
internal readonly record struct ProjectRefreshPlan(
    bool RefreshChapters,
    bool RebuildInspector,
    bool RefreshStagePreview = false);
