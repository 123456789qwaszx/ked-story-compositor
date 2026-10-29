using Vn.Authoring.Model;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;

namespace Vn.Authoring.Editing;

/// <summary>
/// <b>챕터를 바꾸는 코드는 이 파일 안에만 있다</b> (R-F · 2026-09-16, 지시서 §1.1·§4.1).
///
/// <c>ProjectEditor.Scripts.cs</c>가 대사에 대해 하는 일을 챕터 구조에 대해 한다 — 그래야
/// <i>"한 값을 고쳤을 때 권위 있는 데이터가 하나만 바뀐다"</i>가 코드 구조로 보장된다.
///
/// ⛔ <b>여기는 파일을 모른다.</b> 옛 <c>ChapterWorkbookWriter</c>는 셀 하나를 고치는
/// 외과수술이었고 그래서 열 번호·시트 이름·행 삭제가 부르는 쪽까지 스며 있었다. 이제 고치는
/// 것은 <b>모델</b>뿐이다(엑셀 출력은 2026-10-29 · D2에 걷혔다).
///
/// ⚠ <b>규칙은 옛 창구에서 그대로 옮겨 왔다.</b> 간선의 신원은 (출발, 도착, 문구)이고(v9),
/// 개명은 간선·픽스처를 함께 끌고 가며, 삭제는 끝점 간선을 함께 걷고, <b>선택지 사전은
/// 건드리지 않는다</b>(어휘집이지 배선이 아니다). 바뀐 것은 <b>어디에 쓰는가</b>뿐이다.
///
/// ⚠ 실패는 <see cref="InvalidOperationException"/>이다 — 옛 창구와 같다. 부르는 쪽이
/// <c>UiGuard</c>로 받아 상태줄에 말한다.
/// </summary>
public sealed partial class ProjectEditor
{
    /// <summary>그 Id의 챕터. 없으면 null — 부르는 쪽이 "아직 안 들여왔다"를 구분한다.</summary>
    public ChapterDocument? FindChapter(string chapterId) =>
        Project.Chapters.FirstOrDefault(chapter =>
            string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal));

    private ChapterDocument RequireChapter(string chapterId) =>
        FindChapter(chapterId)
        ?? throw new InvalidOperationException($"챕터 '{chapterId}'가 프로젝트에 없습니다.");

    /// <summary>빈 챕터 하나를 세운다. 같은 Id가 이미 있으면 그것을 돌려준다(멱등).</summary>
    public ChapterDocument EnsureChapter(string chapterId)
    {
        if (FindChapter(chapterId) is { } existing)
        {
            return existing;
        }

        var chapter = new ChapterDocument { ChapterId = chapterId };
        Mutate(() => Project.Chapters.Add(chapter));

        return chapter;
    }

    // ── 에피소드 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 에피소드 하나를 세운다.
    ///
    /// ⚠ <b>대사엔트리 = EpisodeId</b>가 기본이다(v3 규약) — 옛 창구가 B열에 같은 값을 적던
    /// 그 규칙이고, 임포터의 노드 이름 규칙도 같은 값을 본다. 어긋나면 같은
    /// 에피소드에 대사노드가 둘이 된다.
    /// </summary>
    /// <param name="sceneId">
    /// 어느 장면에 놓을지. ⚠ <b>만들면서 함께 정한다</b> — 만든 뒤에 옮기면 되돌리기가 두 걸음이
    /// 되고, 그 사이 상태(장면 없는 에피소드)가 잠깐 진짜가 된다.
    /// </param>
    public ChapterEpisode AddEpisode(
        string chapterId, string episodeId, string title, double x, double y, string? sceneId = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindEpisode(chapter, episodeId) is not null)
        {
            throw new InvalidOperationException($"EpisodeId '{episodeId}'가 이미 있습니다.");
        }

        (_, ChapterEpisode? episode, Action attach) = NewEpisodeCore(
            chapterId, episodeId, title, x, y, sceneId);

        Mutate(attach);
        return episode!;
    }

    /// <summary>
    /// <b>에피소드 하나를 짓기만 한다</b> — 카드·대본까지 함께, <see cref="Mutate"/> 밖에서.
    ///
    /// ⭐ <b>2026-09-18에 창구가 하나로 합쳐졌다.</b> 그 전에는 「에피소드에는 대사노드가
    /// 있다」가 <b>편집기 명령이 아니라 화면에</b> 살았다: 챕터 그래프는 <c>AddEpisode</c> 뒤에
    /// 노드 만들기를 한 줄 더 불렀고, 대본 탭은 <b>그 한 줄이 없었다</b>. 그래서 작가의 탭에서
    /// 만든 에피소드만 쓸 자리가 없었다.
    ///
    /// ⚠ <b>창구를 늘릴 때 잊을 수 있는 자리를 없앤 것</b>이 요점이다. 이제 에피소드를 만드는
    /// 길은 전부 여기를 지나므로, 어디서 만들었느냐가 결과를 바꾸지 않는다.
    ///
    /// ⚠ <see cref="EnsureChapterBoard"/>가 판을 <b>먼저</b> 세운다(없을 때만). 판이 없는
    /// 챕터에 에피소드를 넣는 것은 카드를 놓을 자리가 없다는 뜻이다.
    /// </summary>
    private (DialogueNode Node, ChapterEpisode? Episode, Action Attach) NewEpisodeCore(
        string chapterId, string episodeId, string title, double x, double y, string? sceneId,
        string? parentEpisodeId = null)
    {
        string fileId = EnsureChapterBoard(chapterId);

        // ⚠ <b>자리가 둘이다.</b> 주어진 (x, y)는 <b>챕터 그래프</b>의 자리이고, 연출 판의
        //    카드는 제 판의 규칙(<see cref="Graph.NodePlacement"/>)으로 선다 — 부모 카드의 아래에.
        //    한쪽 좌표를 다른 판에 그대로 넣으면 사람이 맞춰 둔 배치가 흐트러진다.
        //
        // ⚠ 부모를 <b>직접</b> 준다 (D3 · 2026-10-29). 이 자리를 묻는 순간에는 에피소드도 간선도 아직
        //    챕터에 없다 — 예전에는 그래서 언제나 "챕터에 없는 이름"으로 빠져 판 오른쪽 끝에 섰다(§6-1).
        (double cardX, double cardY) = Graph.NodePlacement.For(Project, chapterId, episodeId, parentEpisodeId);

        return NewDialogueNodeCore(
            fileId,
            cardX,
            cardY,
            name: episodeId,
            sceneId: sceneId,
            title: title,
            episodePosition: (x, y));
    }

    /// <summary>
    /// 다음 에피소드 — 분기 저작의 핵심 동작. 에피소드와 간선이 <b>한 번의 변경</b>이다
    /// (둘 중 하나만 선 채로 남지 않는다).
    /// </summary>
    public ChapterEpisode AddNextEpisode(
        string chapterId,
        string parentEpisodeId,
        string newEpisodeId,
        string title,
        double x,
        double y,
        string? optionLabel = null,
        string? sceneId = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindEpisode(chapter, newEpisodeId) is not null)
        {
            throw new InvalidOperationException($"EpisodeId '{newEpisodeId}'가 이미 있습니다.");
        }

        if (FindEpisode(chapter, parentEpisodeId) is null)
        {
            throw new InvalidOperationException($"부모 에피소드 '{parentEpisodeId}'가 없습니다.");
        }

        (_, ChapterEpisode? episode, Action attach) = NewEpisodeCore(
            chapterId, newEpisodeId, title, x, y, sceneId, parentEpisodeId);

        ChapterEdge edge = NewEdge(chapter, parentEpisodeId, newEpisodeId, optionLabel);

        // 에피소드·카드·대본·간선이 <b>한 변경</b>이다 — 되돌리기 한 번에 넷이 함께 사라진다.
        Mutate(() =>
        {
            attach();
            chapter.Edges.Add(edge);
            LearnChoiceLabel(chapter, optionLabel);
        });

        return episode!;
    }

    /// <summary>그 챕터의 판 — 규칙은 <see cref="EpisodeNaming.BoardOf"/> 하나다.</summary>
    private StoryFile? BoardOf(string chapterId) => EpisodeNaming.BoardOf(Project, chapterId);

    // ── 간선 ────────────────────────────────────────────────────────────────
    // v9 — 길 하나가 곧 선택지 하나이고, 신원은 (출발, 도착, 문구)다.

    public ChapterEdge AddEdge(
        string chapterId,
        string fromEpisodeId,
        string toEpisodeId,
        string? conditionLabel = null,
        string? optionLabel = null,
        string? statChanges = null,
        bool auto = false)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        ChapterEdge edge = NewEdge(chapter, fromEpisodeId, toEpisodeId, optionLabel, conditionLabel, statChanges, auto);

        Mutate(() =>
        {
            chapter.Edges.Add(edge);
            LearnChoiceLabel(chapter, optionLabel);
        });

        return edge;
    }

    // ── 선택지 사전 · 조건 ──────────────────────────────────────────────────

    /// <summary>
    /// 챕터 조건 하나 — 간선의 표시조건·해금조건이 라벨로 가리킨다.
    ///
    /// ⚠ <b>이름에 "Chapter"가 붙은 이유</b>: <c>AddCondition</c>은 이미 <b>설정노드</b>의
    /// 창구다. 둘은 다른 것이다 — 저쪽은 대사 안의 [3] 조건이 태어나는 자리이고, 이쪽은
    /// 챕터 간선이 보는 [2] 진행 스탯이다(지시서 §3.2). 같은 이름으로 두면 인자 개수만
    /// 다른 호출이 <b>조용히 저쪽으로 간다</b> — 실제로 이 테스트를 쓰다 한 번 물렸다.
    /// </summary>
    public void AddChapterCondition(string chapterId, string label, string expression, string? description = null)
    {
        ChapterDocument chapter = RequireChapter(chapterId);

        if (FindCondition(chapter, label) is not null)
        {
            throw new InvalidOperationException($"조건 라벨 '{label}'이 이미 있습니다.");
        }

        // 조건과 그 조건의 공급이 <b>한 변경</b>이다 (2026-10-29 · D2) — 되돌리기 한 번에 함께 간다.
        Batch(() =>
        {
            Mutate(() => chapter.Conditions.Add(
                new ChapterCondition(label, expression, description, Parsed: [], IsValid: false, SourceRow: 0)));

            SupplyChapterConditions(chapterId);
        });
    }

    // ── 판에 공급하는 챕터 조건 (2026-10-29 · D2) ───────────────────────────
    //
    // ⛔ 예전에는 이 공급이 <b>엑셀 [대본 가져오기]</b> 안에 숨어 있었다. 그 단추를 누를 때마다
    //    챕터 조건이 판의 공급 노드(`챕터 {Id} 조건`)로 건너갔고, 그래서 툴에서 조건을 고친 뒤
    //    가져오기를 안 누르면 대사 노드의 조건 목록이 옛것이었다. 엑셀 연동이 걷힌 지금은
    //    <b>조건이 생기는 그 명령</b>(<see cref="AddChapterCondition"/>)과
    //    <b>판이 서는 순간</b>(<see cref="EnsureChapterBoard"/>)이 공급을 부른다.

    /// <summary>판이 있으면 챕터 조건을 그 판의 공급 노드로 보낸다. 멱등이다 — 바뀔 것이 없으면 안 건드린다.</summary>
    private void SupplyChapterConditions(string chapterId)
    {
        if (FindChapter(chapterId) is { } chapter &&
            EpisodeNaming.BoardOf(Project, chapterId) is { } board)
        {
            ChapterBoardSupply.SupplyChapterConditionsToBoard(
                this, GameDefinition.Empty, board.Id, chapter.ToGraphModel(string.Empty));
        }
    }


    // ── 스탯 정의 (2026-09-17 — 저작 자리가 엑셀에서 챕터 그래프로) ──────────

    /// <summary>
    /// 스탯 하나를 세운다.
    ///
    /// ⚠ <b>경계(최소·최대·초기)는 여기서 안 막는다.</b> <see cref="ChapterDocument"/>가 이미
    /// 진단으로 짚고(<c>최소 &gt; 최대</c>, 초기값이 범위 밖), <b>읽기는 오류가 있어도 모델을
    /// 만든다</b>(규칙 14) — 여기서만 막으면 툴로 만든 것과 파일에서 읽은 것(옛 프로젝트 · 손으로
    /// 고친 파일)이 서로 다른
    /// 규칙을 살게 된다. 값을 채워 가는 중에 막히지도 않는다.
    ///
    /// ⛔ 막는 것은 <b>키 중복</b>뿐이다. 같은 키가 둘이면 어느 행이 이기는지 말할 수 없어
    /// 진단으로 넘길 수 있는 상태가 아니다 — 정규화 뒤에 겹치는 것도 같다(화면에서는 달라
    /// 보이는데 <b>게임에서 하나가 된다</b>).
    /// </summary>
    public ChapterStat AddChapterStat(
        string chapterId,
        string key,
        string? displayName = null,
        int initial = 0,
        int minimum = 0,
        int maximum = 100,
        ChapterStatType type = ChapterStatType.Int)
    {
        ChapterDocument chapter = RequireChapter(chapterId);
        string target = (key ?? string.Empty).Trim();

        if (target.Length == 0)
        {
            throw new InvalidOperationException("스탯 키가 비어 있습니다.");
        }

        RequireStatKeyFree(chapter, target, replacing: null);

        var stat = BoolBounded(new ChapterStat(
            target,
            string.IsNullOrWhiteSpace(displayName) ? target : displayName.Trim(),
            initial,
            minimum,
            maximum,
            SourceRow: 0,
            type));

        Mutate(() => chapter.Stats.Add(stat));

        return stat;
    }

    /// <summary>
    /// bool 스탯은 값 공간이 0/1 하나다 — 범위를 0~1로 고정하고 처음값은 0이 아니면 1이다.
    ///
    /// ⚠ 옛 워크북 리더가 하던 일이다(최소·최대 칸을 읽지 않고 고정했다). 리더가 걷힌 뒤 편집기가
    /// 경계를 받는 그대로 두면 깃발에 0~100 같은 범위가 붙고, 도달성 증명이 없는 값까지 훑는다.
    /// </summary>
    private static ChapterStat BoolBounded(ChapterStat stat) =>
        stat.Type == ChapterStatType.Bool
            ? stat with { Minimum = 0, Maximum = 1, Initial = stat.Initial == 0 ? 0 : 1 }
            : stat;

    /// <summary>
    /// 이 키를 써도 되는가 — 겹치면 던진다.
    /// <paramref name="replacing"/>은 자기 자신이라 안 센다.
    /// </summary>
    private static void RequireStatKeyFree(ChapterDocument chapter, string key, string? replacing)
    {
        bool Other(ChapterStat stat) =>
            replacing is null || !string.Equals(stat.Key, replacing, StringComparison.Ordinal);

        if (chapter.Stats.Any(stat => Other(stat) &&
                string.Equals(stat.Key, key, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"스탯 '{key}'가 이미 있습니다.");
        }

        // ⛔ ~~정규화 뒤 겹침도 막는다~~ — <b>걷혔다</b> (2026-09-17, 런타임 회신 §6.1).
        //    `stat("키")`의 인자가 문자열 리터럴이라 정규화가 없어졌고, 근거도 함께 사라졌다.
    }

    // ── 잔손 ────────────────────────────────────────────────────────────────

    private static ChapterEpisode? FindEpisode(ChapterDocument chapter, string episodeId) =>
        chapter.Episodes.FirstOrDefault(episode =>
            string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal));

    private ChapterEdge NewEdge(
        ChapterDocument chapter,
        string fromEpisodeId,
        string toEpisodeId,
        string? optionLabel,
        string? conditionLabel = null,
        string? statChanges = null,
        bool auto = false)
    {
        if (HasEdge(chapter, fromEpisodeId, toEpisodeId, optionLabel))
        {
            throw new InvalidOperationException(
                $"간선 {fromEpisodeId}→{toEpisodeId}이 같은 선택지 문구로 이미 있습니다.");
        }

        return new ChapterEdge(fromEpisodeId, toEpisodeId, optionLabel, conditionLabel, LockedMessage: null, SourceRow: 0)
        {
            Auto = auto,
            StatChanges = statChanges is null ? [] : ParseDeltas(chapter, statChanges)
        };
    }

    /// <summary>간선의 신원 (출발, 도착, 문구)가 이미 있는가(v9). <paramref name="exceptIndex"/>의 간선은 빼고 본다.</summary>
    private static bool HasEdge(
        ChapterDocument chapter, string fromEpisodeId, string toEpisodeId, string? optionLabel, int exceptIndex = -1) =>
        chapter.Edges
            .Where((_, index) => index != exceptIndex)
            .Any(edge =>
                string.Equals(edge.FromEpisodeId, fromEpisodeId, StringComparison.Ordinal) &&
                string.Equals(edge.ToEpisodeId, toEpisodeId, StringComparison.Ordinal) &&
                string.Equals(
                    edge.OptionLabel?.Trim() ?? string.Empty,
                    optionLabel?.Trim() ?? string.Empty,
                    StringComparison.Ordinal));

    /// <summary>
    /// 사전에 없는 낱말이면 올려 둔다 — 다음부터 드롭다운에서 고른다(옛 <c>EnsureChoiceLabel</c>).
    ///
    /// ⚠ <c>Mutate</c> 안에서만 부른다 — 이것만으로 되돌리기 항목을 따로 만들지 않는다.
    /// </summary>
    private static void LearnChoiceLabel(ChapterDocument chapter, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || FindChoice(chapter, text) is not null)
        {
            return;
        }

        // 인덱스는 사전 안의 순서일 뿐이다 — 10 단위로 세어 사람이 사이에 끼울 자리를 남긴다.
        string index = ((chapter.ChoiceOptions.Count + 1) * 10).ToString(
            System.Globalization.CultureInfo.InvariantCulture);

        chapter.ChoiceOptions.Add(new ChapterChoiceOption(index, text.Trim(), Memo: null, SourceRow: 0));
    }

    private static ChapterChoiceOption? FindChoice(ChapterDocument chapter, string text) =>
        chapter.ChoiceOptions.FirstOrDefault(option =>
            string.Equals(option.Text.Trim(), text.Trim(), StringComparison.Ordinal));

    private static ChapterCondition? FindCondition(ChapterDocument chapter, string label) =>
        chapter.Conditions.FirstOrDefault(condition =>
            string.Equals(condition.Label, label, StringComparison.Ordinal));

    /// <summary>
    /// `스탯변화` 문법(`trust +1; anger -1`)을 푼다 — 문법을 읽는 자리는
    /// <see cref="StatDeltaParser"/> 하나다.
    ///
    /// ⚠ <b>못 읽은 것은 조용히 버리지 않는다.</b> 옛 경로에서는 셀에 원문이 남아 리더가
    /// 다음 읽기에 짚어 줬는데, 이제 다시 읽는 일이 없어 여기서 안 막으면 영영 사라진다.
    /// </summary>
    private static IReadOnlyList<StatDelta> ParseDeltas(ChapterDocument chapter, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        StatDeltaParseResult parsed = StatDeltaParser.Parse(
            text, chapter.Stats.Select(stat => stat.Key).ToList());

        return parsed.IsValid
            ? parsed.Deltas
            : throw new InvalidOperationException(
                $"스탯변화 '{text}': {parsed.Problems[0].Message}");
    }
}
