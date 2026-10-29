using Vn.Authoring.Chapters;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Graph;

/// <summary>
/// <b>새 카드가 연출 그래프에 설 자리</b> — 위에서 아래로 (D3 · 2026-10-29, <c>docs/design/two-graphs.md</c> §7 D3).
///
/// 소유자: *"챕터 그래프처럼 위에서 아래로 진행시키도록 합니다 … 분기는 기존처럼 오른쪽으로 빠지도록."*
///
/// <list type="bullet">
/// <item><b>선택지로 가는 다음 에피소드</b>는 부모 카드의 <b>아래</b>에 선다. 그 자리가 차 있으면 오른쪽으로 비켜 선다.</item>
/// <item><b>분기로 다녀갈 카드</b>는 원본의 <b>오른쪽</b>에 선다(<see cref="BesideOf"/>).</item>
/// <item>부모가 없으면(시작 에피소드 · 들어오는 길이 없는 카드) 판의 맨 아래 카드 밑에, 판이 비었으면 원점에 선다.</item>
/// </list>
///
/// ⛔ 2026-10-29까지는 왼→오였다 — 장면마다 한 줄을 받고 장면 안의 진행은 그 줄의 오른쪽으로 갔다(R7 P-4).
/// 그리고 에피소드를 만드는 길이 자리를 <b>에피소드를 넣기 전에</b> 물어서, 챕터 그래프에서 만든 새 카드는
/// 언제나 판 오른쪽 끝에 섰다(§6-1). 이제 부르는 쪽이 부모를 함께 준다.
///
/// ⚠ <b>이미 선 카드는 안 옮긴다.</b> 자리는 사람이 끌어 정하는 것이다 — 여기서 정하는 것은 새로 서는
/// 카드의 첫 자리뿐이고, 판 전체를 다시 세우는 일은 [자동 정렬](<see cref="Arrange"/>)이 사람이 누를 때만 한다.
/// </summary>
public static class NodePlacement
{
    /// <summary>옆 카드까지의 거리 — 카드 너비(210)에, 칩 오른쪽으로 내려가는 레인의 자리를 더한 값.</summary>
    public const double Column = 320;

    /// <summary>
    /// 아래 카드까지의 거리 — 카드(글 칸을 품어 대개 150~220, D4) + 그 아래 선택지 칩 셋(약 140) + 도착 접점과 여백.
    /// </summary>
    public const double Row = 400;

    /// <summary>
    /// 그 에피소드의 카드가 설 자리.
    /// </summary>
    /// <param name="parentEpisodeId">
    /// 이 에피소드로 이어 줄 부모. 주지 않으면 챕터의 간선에서 찾는다(들어오는 길의 출발 중 카드가 선 가장 아래 것).
    /// ⚠ 에피소드와 간선을 <b>한 변경으로</b> 만드는 길(<c>AddNextEpisode</c>)은 자리를 물을 때 간선이 아직
    /// 없다 — 그래서 부모를 직접 준다.
    /// </param>
    public static (double X, double Y) For(
        StoryProject project, string chapterId, string episodeId, string? parentEpisodeId = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (EpisodeNaming.BoardOf(project, chapterId) is not { } board)
        {
            return (0, 0);
        }

        if (ParentCard(project, chapterId, episodeId, parentEpisodeId) is { } parent)
        {
            return FirstFree(board, parent.Layout.X, parent.Layout.Y + Row);
        }

        List<DialogueNode> cards = board.Nodes.OfType<DialogueNode>().ToList();

        return cards.Count == 0
            ? (0, 0)
            : FirstFree(board, cards.Min(card => card.Layout.X), cards.Max(card => card.Layout.Y) + Row);
    }

    /// <summary>원본의 오른쪽 — 분기로 다녀갈 카드의 자리. 차 있으면 더 오른쪽으로 비켜 선다.</summary>
    public static (double X, double Y) BesideOf(StoryFile board, StoryNode source)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(source);

        return FirstFree(board, source.Layout.X + Column, source.Layout.Y);
    }

    /// <summary>
    /// [자동 정렬] — 챕터 판 하나를 <b>위에서 아래로 다시 세운</b> 자리 (D3-d · 2026-10-29). 노드 Id → 자리.
    /// 부르는 쪽이 <c>ProjectEditor.MoveNodes</c> 한 번으로 적는다(되돌리기 한 칸).
    ///
    /// <list type="bullet">
    /// <item><b>에피소드 카드</b>: 줄 = 챕터 그래프의 깊이(<see cref="ChapterBranchPlanner.Depths"/>), 같은 줄은 에피소드를
    ///       적은 순서대로 옆으로. 깊이가 없는(시작에서 닿지 않는) 에피소드는 한 줄 띄운 맨 아래 줄 — 챕터 그래프의
    ///       [자동 정렬](<see cref="ChapterBranchPlanner.Layout"/>)과 같은 모양이다.</item>
    /// <item><b>그 카드의 실행 출구(분기 · IF 갈래 · 기본)로 다녀가는 카드</b>: 그 카드의 <b>오른쪽</b>에 한 줄로.
    ///       그 칸을 비워 두고 같은 줄의 다음 에피소드가 선다.</item>
    /// <item><b>어디에도 안 매인 카드</b>(설정 노드 · 들어오는 길이 없는 자유 씬 · 챕터에 없는 에피소드의 카드): 맨 아래 한 줄.</item>
    /// <item>숨은 배관(<see cref="GraphProjectionBuilder.IsHiddenPlumbing"/>)은 그리지 않으므로 건드리지 않는다.</item>
    /// </list>
    ///
    /// 판의 왼쪽 위 구석(지금 카드들의 가장 작은 X · Y)은 그대로 둔다 — 한 화면에 챕터 판 여럿이 펼쳐져 있으므로
    /// 원점으로 끌어오면 다른 판과 포개진다. 챕터 판이 아니면 빈 결과다.
    /// </summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> Arrange(StoryProject project, string chapterId)
    {
        ArgumentNullException.ThrowIfNull(project);

        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        if (EpisodeNaming.BoardOf(project, chapterId) is not { } board ||
            project.Chapters.FirstOrDefault(item =>
                string.Equals(item.ChapterId, chapterId, StringComparison.Ordinal)) is not { } chapter)
        {
            return positions;
        }

        List<StoryNode> drawn = board.Nodes
            .Where(node => !GraphProjectionBuilder.IsHiddenPlumbing(node, board))
            .ToList();

        if (drawn.Count == 0)
        {
            return positions;
        }

        (double originX, double originY) = (drawn.Min(node => node.Layout.X), drawn.Min(node => node.Layout.Y));
        var drawnIds = drawn.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        ChapterGraphModel model = chapter.ToGraphModel(chapterId);
        IReadOnlyDictionary<string, int> depths = ChapterBranchPlanner.Depths(model);

        List<(int Depth, DialogueNode Card)> episodes = model.Episodes
            .Select(episode => (Episode: episode, Card: EpisodeNaming.CardFor(project, chapterId, episode.EpisodeId)))
            .Where(pair => pair.Card is not null && drawnIds.Contains(pair.Card.Id))
            .Select(pair => (Depth: depths.TryGetValue(pair.Episode.EpisodeId, out int depth) ? depth : -1, Card: pair.Card!))
            .ToList();

        // 척추 = 진행이 닿는(깊이가 있는) 에피소드. 「분기 추가」의 카드도 챕터 판에서는 에피소드지만
        // 진행이 착지하지 않는 곁가지라 깊이가 없다 — 그 카드는 다녀가는 카드의 오른쪽에 선다.
        List<(int Depth, DialogueNode Card)> spine = episodes.Where(pair => pair.Depth >= 0).ToList();
        var spineIds = spine.Select(pair => pair.Card.Id).ToHashSet(StringComparer.Ordinal);
        int deepest = spine.Count == 0 ? 0 : spine.Max(pair => pair.Depth);

        foreach (IGrouping<int, (int Depth, DialogueNode Card)> row in spine
                     .GroupBy(pair => pair.Depth)
                     .OrderBy(row => row.Key))
        {
            PlaceRow(row.Select(pair => pair.Card), originY + (row.Key * Row));
        }

        // 아무 데서도 다녀가지 않는 깊이 없는 에피소드 — 한 줄 띄운 맨 아래 줄(챕터 그래프의 Layout과 같은 모양).
        List<DialogueNode> unreached = episodes
            .Where(pair => pair.Depth < 0 && !positions.ContainsKey(pair.Card.Id))
            .Select(pair => pair.Card)
            .ToList();

        if (unreached.Count > 0)
        {
            PlaceRow(unreached, originY + ((deepest + 2) * Row));
        }

        // 어디에도 안 매인 카드 — 맨 아래 한 줄.
        double restY = positions.Count == 0 ? originY : positions.Values.Max(spot => spot.Y) + Row;
        double restX = originX;

        foreach (StoryNode node in drawn.Where(node => !positions.ContainsKey(node.Id)))
        {
            positions[node.Id] = (restX, restY);
            restX += Column;
        }

        return positions;

        // 한 줄 — 카드마다 제 자리, 그 오른쪽에 그 카드로 다녀가는 카드들, 그 다음 카드는 그만큼 비켜 선다.
        void PlaceRow(IEnumerable<DialogueNode> cards, double y)
        {
            double x = originX;

            foreach (DialogueNode card in cards)   // 에피소드를 적은 순서다
            {
                if (positions.ContainsKey(card.Id))
                {
                    continue;   // 같은 줄의 앞 카드가 다녀가는 카드로 이미 데려갔다
                }

                positions[card.Id] = (x, y);
                x += Column;

                foreach (StoryNode side in SideChainOf(project, card, spineIds, drawnIds, positions))
                {
                    positions[side.Id] = (x, y);
                    x += Column;
                }
            }
        }
    }

    /// <summary>
    /// 이 카드의 실행 출구로 다녀가는 카드들 — 출구를 따라 너비 우선으로, 에피소드 카드(제 줄에 선다)와 이미 자리를
    /// 받은 카드에서 멈춘다. 같은 카드로 이어지는 출구가 여럿이어도 한 번이다.
    /// </summary>
    private static List<StoryNode> SideChainOf(
        StoryProject project,
        DialogueNode card,
        IReadOnlySet<string> spineIds,
        IReadOnlySet<string> drawnIds,
        IReadOnlyDictionary<string, (double X, double Y)> placed)
    {
        var chain = new List<StoryNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { card.Id };
        var queue = new Queue<StoryNode>([card]);

        while (queue.Count > 0)
        {
            foreach (ExitPort port in NodeConnections.PortsOf(queue.Dequeue(), project))
            {
                if (port.TargetNodeId is not { } targetId ||
                    !seen.Add(targetId) ||
                    spineIds.Contains(targetId) ||
                    !drawnIds.Contains(targetId) ||
                    placed.ContainsKey(targetId) ||
                    project.FindNode(targetId) is not { } target)
                {
                    continue;
                }

                chain.Add(target);
                queue.Enqueue(target);
            }
        }

        return chain;
    }

    /// <summary>
    /// 부모 카드 — 준 것이 먼저고, 없으면 들어오는 간선의 출발 중 카드가 선 것 가운데 <b>가장 아래</b> 것이다.
    /// 합류하는 에피소드는 가장 깊은 부모의 밑에 서야 그 부모의 레인이 위로 거슬러 오르지 않는다
    /// (챕터 그래프의 깊이 규칙과 같다 — <see cref="ChapterBranchPlanner.Depths"/>).
    /// </summary>
    private static DialogueNode? ParentCard(
        StoryProject project, string chapterId, string episodeId, string? parentEpisodeId)
    {
        if (parentEpisodeId is not null)
        {
            return EpisodeNaming.CardFor(project, chapterId, parentEpisodeId);
        }

        ChapterDocument? chapter = project.Chapters.FirstOrDefault(item =>
            string.Equals(item.ChapterId, chapterId, StringComparison.Ordinal));

        return chapter?.Edges
            .Where(edge => string.Equals(edge.ToEpisodeId, episodeId, StringComparison.Ordinal) &&
                           !string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal))
            .Select(edge => EpisodeNaming.CardFor(project, chapterId, edge.FromEpisodeId))
            .Where(card => card is not null)
            .MaxBy(card => card!.Layout.Y);
    }

    /// <summary>그 자리에 카드가 있으면 한 칸씩 오른쪽으로 — 겹치지만 않으면 된다.</summary>
    private static (double X, double Y) FirstFree(StoryFile board, double x, double y)
    {
        List<DialogueNode> cards = board.Nodes.OfType<DialogueNode>().ToList();

        while (cards.Any(card =>
                   Math.Abs(card.Layout.X - x) < Column / 2 &&
                   Math.Abs(card.Layout.Y - y) < Row / 2))
        {
            x += Column;
        }

        return (x, y);
    }
}
