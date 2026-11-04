namespace Vn.Authoring.Model;

/// <summary>갈림 하나에서 고를 선택지 — 도착이 아니라 <b>문구</b>로 적는다(같은 도착으로 가는 간선이 문구만 다를 수 있다).</summary>
public sealed record PreviewRouteChoice(string FromEpisodeId, string OptionLabel);

/// <summary>
/// <b>미리 정한 루트</b> (2026-11-04 소유자: *"그 선택지가 있는 곳에서 무슨 선택지를 고를지 일종의 픽스처 느낌으로 미리
/// 루트를 설정"*) — 챕터 하나의 전체 재생이 갈림마다 어느 선택지를 고를지, 그리고 어떤 스탯으로 시작할지.
///
/// director의 챕터 픽스처(`ChapterFixture` — 2026-08-24에 화면만 걷혔다)와 같은 생각이지만 <b>자리</b>가 다르다: 거기서는
/// 검증이라 쓸 일이 없었고, 여기서는 "이 길로 재생해 보기"라 매번 쓰는 일이다. 그래서 이 툴의 파일(<c>.vncompose.json</c>)에
/// 산다 — 재생 설정이지 원고가 아니다(K3). 챕터마다 여럿, 활성은 하나.
///
/// 시작 스탯이 있는 이유: 분기 카드의 "막힘/통과"가 이 지도의 가장 큰 값이고, 그것은 스탯 없이는 못 본다.
/// </summary>
public sealed class PreviewRoute
{
    public PreviewRoute(string? id = null, string? name = null)
    {
        Id = id ?? Identifier.PreviewRoute();
        Name = name ?? "루트";
    }

    public string Id { get; }

    public string Name { get; set; }

    /// <summary>어느 챕터의 루트인가(= 판 이름).</summary>
    public string ChapterId { get; set; } = string.Empty;

    /// <summary>챕터 안에서 활성인 루트 하나 — 전체 재생이 이것을 따른다. 편집기가 하나만 켜지게 지킨다.</summary>
    public bool IsActive { get; set; }

    /// <summary>시작 스탯 덮어쓰기 — 없는 키는 `스탯` 시트의 초기값.</summary>
    public Dictionary<string, int> Stats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>갈림마다 하나 — 같은 에피소드에 둘이 있으면 편집기가 거부한다.</summary>
    public List<PreviewRouteChoice> Choices { get; init; } = new();

    public PreviewRouteChoice? ChoiceAt(string episodeId) =>
        Choices.FirstOrDefault(choice => string.Equals(choice.FromEpisodeId, episodeId, StringComparison.Ordinal));

    public PreviewRoute Clone() => new(Id, Name)
    {
        ChapterId = ChapterId,
        IsActive = IsActive,
        Stats = new Dictionary<string, int>(Stats, StringComparer.Ordinal),
        Choices = Choices.ToList()
    };
}
