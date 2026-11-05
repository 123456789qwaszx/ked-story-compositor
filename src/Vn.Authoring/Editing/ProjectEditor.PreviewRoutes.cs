using Vn.Authoring.Model;

namespace Vn.Authoring.Editing;

/// <summary>
/// 미리 정한 루트 —전체 재생이 갈림마다 고를 선택지와 시작 스탯. 되돌릴 수 있고 이 툴의 파일에 저장된다.
/// 챕터마다 여럿, <b>활성은 하나</b> — 지도와 재생이 "지금 루트"를 하나로 본다.
/// </summary>
public sealed partial class ProjectEditor
{
    public IReadOnlyList<PreviewRoute> PreviewRoutesOf(string chapterId) =>
        Project.PreviewRoutes
            .Where(route => string.Equals(route.ChapterId, chapterId, StringComparison.Ordinal))
            .ToList();

    public PreviewRoute? ActivePreviewRoute(string chapterId) =>
        Project.PreviewRoutes.FirstOrDefault(route =>
            route.IsActive && string.Equals(route.ChapterId, chapterId, StringComparison.Ordinal));

    public PreviewRoute? FindPreviewRoute(string routeId) =>
        Project.PreviewRoutes.FirstOrDefault(route => string.Equals(route.Id, routeId, StringComparison.Ordinal));

    /// <summary>새 루트 — 만들면서 활성이 된다(만든 사람이 곧 쓰려는 것이다).</summary>
    public PreviewRoute AddPreviewRoute(string chapterId, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);

        int ordinal = PreviewRoutesOf(chapterId).Count + 1;
        var route = new PreviewRoute(name: string.IsNullOrWhiteSpace(name) ? $"루트 {ordinal}" : name.Trim())
        {
            ChapterId = chapterId
        };

        Mutate(ProjectChangeKind.Content, () =>
        {
            foreach (PreviewRoute other in Project.PreviewRoutes.Where(other =>
                         string.Equals(other.ChapterId, chapterId, StringComparison.Ordinal)))
            {
                other.IsActive = false;
            }

            route.IsActive = true;
            Project.PreviewRoutes.Add(route);
        });

        return route;
    }

    public void RemovePreviewRoute(string routeId)
    {
        PreviewRoute route = RequirePreviewRoute(routeId);
        Mutate(ProjectChangeKind.Content, () => Project.PreviewRoutes.Remove(route));
    }

    public void RenamePreviewRoute(string routeId, string name)
    {
        PreviewRoute route = RequirePreviewRoute(routeId);
        string next = (name ?? string.Empty).Trim();

        if (next.Length == 0 || string.Equals(route.Name, next, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(ProjectChangeKind.Content, () => route.Name = next);
    }

    /// <summary>활성 루트를 바꾼다. null이면 그 챕터에 활성 루트 없음(갈림마다 묻는다).</summary>
    public void SetActivePreviewRoute(string chapterId, string? routeId)
    {
        PreviewRoute? target = routeId is null ? null : RequirePreviewRoute(routeId);

        if (target is not null && !string.Equals(target.ChapterId, chapterId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("다른 챕터의 루트입니다.");
        }

        if ((ActivePreviewRoute(chapterId)?.Id) == target?.Id)
        {
            return;
        }

        Mutate(ProjectChangeKind.Content, () =>
        {
            foreach (PreviewRoute route in Project.PreviewRoutes.Where(route =>
                         string.Equals(route.ChapterId, chapterId, StringComparison.Ordinal)))
            {
                route.IsActive = ReferenceEquals(route, target);
            }
        });
    }

    /// <summary>
    /// 갈림 하나의 선택을 적는다 — 같은 에피소드의 이전 선택은 바뀐다. <paramref name="optionLabel"/>이 null이면 뺀다.
    /// 같은 값이면 아무것도 적지 않는다.
    /// </summary>
    public void SetPreviewRouteChoice(string routeId, string fromEpisodeId, string? optionLabel)
    {
        PreviewRoute route = RequirePreviewRoute(routeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromEpisodeId);

        PreviewRouteChoice? current = route.ChoiceAt(fromEpisodeId);

        if (optionLabel is null ? current is null : string.Equals(current?.OptionLabel, optionLabel, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(ProjectChangeKind.Content, () =>
        {
            route.Choices.RemoveAll(choice => string.Equals(choice.FromEpisodeId, fromEpisodeId, StringComparison.Ordinal));

            if (optionLabel is not null)
            {
                route.Choices.Add(new PreviewRouteChoice(fromEpisodeId, optionLabel));
            }
        });
    }

    /// <summary>시작 스탯 하나 — null이면 덮어쓰기를 걷는다(시트 초기값으로).</summary>
    public void SetPreviewRouteStat(string routeId, string statKey, int? value)
    {
        PreviewRoute route = RequirePreviewRoute(routeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(statKey);

        bool has = route.Stats.TryGetValue(statKey, out int existing);

        if (value is null ? !has : has && existing == value.Value)
        {
            return;
        }

        Mutate(ProjectChangeKind.Content, () =>
        {
            if (value is null)
            {
                route.Stats.Remove(statKey);
            }
            else
            {
                route.Stats[statKey] = value.Value;
            }
        });
    }

    private PreviewRoute RequirePreviewRoute(string routeId) =>
        FindPreviewRoute(routeId) ?? throw new InvalidOperationException($"루트 '{routeId}'가 없습니다.");
}
