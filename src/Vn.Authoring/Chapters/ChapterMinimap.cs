using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>지도의 점 하나가 어떤 모양으로 서는가. 우선순위대로 하나만 — 분기 카드 › 장면 루트 › 챕터 끝 › 갈림 › 보통.</summary>
public enum ChapterMinimapShape
{
    /// <summary>보통 에피소드.</summary>
    Episode,

    /// <summary>선택지로 갈라지는 자리 — 나가는 선택지 간선이 둘 이상.</summary>
    Fork,

    /// <summary>나가는 간선이 없다 — 챕터 끝(끝 카드).</summary>
    End,

    /// <summary>장면의 루트 — 밖에서 들어오는 자리. 여기서 무대를 세운다.</summary>
    SceneRoot,

    /// <summary>분기 카드 — 들어오는 간선 없이 <c>&lt;&lt;detour&gt;&gt;</c>로만 들어간다. 제 첫머리 조건으로 돌지 말지 정한다.</summary>
    Branch
}

public enum ChapterMinimapEdgeKind
{
    /// <summary>선택지 간선 — 플레이어가 고른다.</summary>
    Choice,

    /// <summary>자동 간선 — 묻지 않고 넘어간다.</summary>
    Auto,

    /// <summary>분기 다녀오기 — 줄 앞에서 분기 카드로 갔다 돌아온다.</summary>
    Detour
}

/// <param name="X">director 챕터 그래프의 자리 그대로(2026-11-04 소유자: "지도의 자리는 director의 것을 그대로 쓰고 compositor에서는 편집하지 않는다").</param>
/// <param name="DialogueNodeId">이 에피소드의 카드. 카드가 아직 없으면 null — 점은 서되 누를 수 없다.</param>
public sealed record ChapterMinimapNode(
    string EpisodeId,
    string Title,
    string SceneId,
    ChapterMinimapShape Shape,
    double X,
    double Y,
    string? DialogueNodeId,
    bool IsSceneRoot,
    bool IsEnd,
    bool IsFork);

public sealed record ChapterMinimapEdge(
    string FromEpisodeId,
    string ToEpisodeId,
    ChapterMinimapEdgeKind Kind,
    string? Label);

/// <summary>장면 띠 — 같은 장면의 점들을 한 배경으로 묶는다. 장면이 바뀌면 무대가 비워진다는 것이 띠 경계로 읽힌다.</summary>
public sealed record ChapterMinimapScene(string SceneId, string DisplayName, IReadOnlyList<string> EpisodeIds);

/// <summary>
/// <b>챕터 지도</b> (2026-11-04 소유자: *"슬레이 더 스파이어의 미니맵 같은 노드 기반 연결 + 마야의 애님스쿨 피커 같은 느낌"*).
///
/// 무대의 씬 선택기(드롭다운)는 "어디서 왔고 어디로 가는가"를 숨겼다 — 연출은 바로 그 흐름 위에서 하는 일인데.
/// 이 모델은 챕터 하나를 <b>점(에피소드) · 선(간선) · 띠(장면)</b>로 편다. 그리기는 화면의 일이고 여기는 순수하다.
///
/// 자리의 주인은 director다 — 챕터 그래프의 (x, y)를 그대로 쓴다. 여기서 옮기지 않는다: 자리를 둘이 가지면
/// 한쪽이 낡는다(같은 날 "사본이 갈리는" 문제의 작은 판). 보기 싫으면 director의 자동 정렬을 고친다.
/// </summary>
public sealed class ChapterMinimap
{
    private ChapterMinimap(
        string chapterId,
        IReadOnlyList<ChapterMinimapNode> nodes,
        IReadOnlyList<ChapterMinimapEdge> edges,
        IReadOnlyList<ChapterMinimapScene> scenes)
    {
        ChapterId = chapterId;
        Nodes = nodes;
        Edges = edges;
        Scenes = scenes;
    }

    public string ChapterId { get; }

    public IReadOnlyList<ChapterMinimapNode> Nodes { get; }

    public IReadOnlyList<ChapterMinimapEdge> Edges { get; }

    public IReadOnlyList<ChapterMinimapScene> Scenes { get; }

    public ChapterMinimapNode? Find(string episodeId) =>
        Nodes.FirstOrDefault(node => string.Equals(node.EpisodeId, episodeId, StringComparison.Ordinal));

    /// <summary>카드 Id로 점을 찾는다 — 무대가 지금 서 있는 카드를 지도에 비출 때.</summary>
    public ChapterMinimapNode? FindByDialogue(string? dialogueNodeId) =>
        dialogueNodeId is null
            ? null
            : Nodes.FirstOrDefault(node => string.Equals(node.DialogueNodeId, dialogueNodeId, StringComparison.Ordinal));

    /// <summary>점들의 경계(x · y 최소/최대). 점이 없으면 0.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds =>
        Nodes.Count == 0
            ? (0, 0, 0, 0)
            : (Nodes.Min(node => node.X), Nodes.Min(node => node.Y), Nodes.Max(node => node.X), Nodes.Max(node => node.Y));

    /// <summary>
    /// 챕터 모델과 판에서 지도를 짓는다. 판(<paramref name="project"/>)은 카드 Id와 분기 표식(<c>&lt;&lt;detour&gt;&gt;</c>)의 원천이다 —
    /// 분기는 챕터 간선이 아니라 카드 줄의 표식이라 모델만으로는 모른다.
    /// </summary>
    public static ChapterMinimap Build(ChapterGraphModel chapter, StoryProject? project)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        HashSet<string> sceneRoots = ChapterSceneGrouping.Of(chapter)
            .Select(scene => scene.RootEpisodeId)
            .ToHashSet(StringComparer.Ordinal);

        var edges = new List<ChapterMinimapEdge>();

        foreach (ChapterEdge edge in chapter.Edges)
        {
            edges.Add(new ChapterMinimapEdge(
                edge.FromEpisodeId,
                edge.ToEpisodeId,
                edge.Auto ? ChapterMinimapEdgeKind.Auto : ChapterMinimapEdgeKind.Choice,
                string.IsNullOrWhiteSpace(edge.OptionLabel) ? null : edge.OptionLabel));
        }

        // 분기 표식 — 카드의 줄 앞에서 다녀오는 카드. 그 카드가 이 챕터의 에피소드일 때만 선을 긋는다.
        var cards = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);

        if (project is not null)
        {
            foreach (ChapterEpisode episode in chapter.Episodes)
            {
                if (EpisodeNaming.CardFor(project, chapter.ChapterId, episode.EpisodeId) is { } card)
                {
                    cards[episode.EpisodeId] = card;
                }
            }

            Dictionary<string, string> episodeByNodeId = cards.ToDictionary(
                pair => pair.Value.Id, pair => pair.Key, StringComparer.Ordinal);

            foreach ((string episodeId, DialogueNode card) in cards)
            {
                foreach (DialogueLineExtension extension in card.LineExtensions)
                {
                    foreach (string targetNodeId in extension.DetourTargetNodeIds)
                    {
                        if (episodeByNodeId.TryGetValue(targetNodeId, out string? targetEpisode))
                        {
                            edges.Add(new ChapterMinimapEdge(episodeId, targetEpisode, ChapterMinimapEdgeKind.Detour, null));
                        }
                    }
                }
            }
        }

        HashSet<string> detourTargets = edges
            .Where(edge => edge.Kind == ChapterMinimapEdgeKind.Detour)
            .Select(edge => edge.ToEpisodeId)
            .ToHashSet(StringComparer.Ordinal);

        var nodes = new List<ChapterMinimapNode>();

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            bool hasIncoming = chapter.Edges.Any(edge =>
                string.Equals(edge.ToEpisodeId, episode.EpisodeId, StringComparison.Ordinal));
            int outgoingChoices = chapter.Edges.Count(edge =>
                string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal) && !edge.Auto);
            bool hasOutgoing = chapter.Edges.Any(edge =>
                string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal));

            bool isBranch = !hasIncoming && detourTargets.Contains(episode.EpisodeId);
            bool isSceneRoot = sceneRoots.Contains(episode.EpisodeId) && !isBranch;
            bool isEnd = !hasOutgoing && !isBranch;
            bool isFork = outgoingChoices >= 2;

            ChapterMinimapShape shape =
                isBranch ? ChapterMinimapShape.Branch :
                isSceneRoot ? ChapterMinimapShape.SceneRoot :
                isEnd ? ChapterMinimapShape.End :
                isFork ? ChapterMinimapShape.Fork :
                ChapterMinimapShape.Episode;

            nodes.Add(new ChapterMinimapNode(
                episode.EpisodeId,
                string.IsNullOrWhiteSpace(episode.Title) ? episode.EpisodeId : episode.Title,
                episode.EffectiveSceneId,
                shape,
                episode.X,
                episode.Y,
                cards.TryGetValue(episode.EpisodeId, out DialogueNode? card) ? card.Id : null,
                isSceneRoot,
                isEnd,
                isFork));
        }

        List<ChapterMinimapScene> scenes = ChapterSceneGrouping.Of(chapter)
            .Select(scene => new ChapterMinimapScene(
                scene.SceneId,
                scene.DisplayName,
                scene.Episodes.Select(episode => episode.EpisodeId).ToList()))
            .ToList();

        return new ChapterMinimap(chapter.ChapterId, nodes, edges, scenes);
    }
}
