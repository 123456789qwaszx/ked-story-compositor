using System.Text.Json.Nodes;
using Vn.Authoring.Chapters;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Fixtures;

/// <summary>
/// 견본 챕터 — <c>tests/fixtures/chapter-samples/{이름}.chapter.json</c>.
///
/// 프로젝트 매니페스트의 <c>chapters</c> 항목 하나와 같은 모양이다(<see cref="ChapterDocumentJson"/>).
/// 2026-10-29까지 견본은 엑셀 워크북(<c>docs/chapter-graph-sample.xlsx</c> ·
/// <c>docs/ch01.contract-sample.xlsx</c>)이었고 테스트가 그것을 리더로 읽었다. 엑셀 연동을 걷으면서
/// 같은 내용을 프로젝트 형식으로 옮겼다(docs/design/two-graphs.md §7 D2).
/// </summary>
internal static class SampleChapters
{
    /// <summary>복도와 라루의 제안 — 에피소드 여섯 · 간선 다섯 · 조건 넷 · 스탯 넷(깃발 하나) · 픽스처 셋.</summary>
    public const string Corridor = "ch05";

    /// <summary>런타임 계약 표본 — 장면 · 자동 길 · 표시/해금 조건 · 스탯 변화 · 이벤트키.</summary>
    public const string Contract = "ch01";

    public static string PathOf(string sample) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "tests", "fixtures", "chapter-samples", sample + ".chapter.json"));

    /// <summary>견본을 읽는다. <paramref name="chapterId"/>를 주면 그 이름으로 세운다.</summary>
    public static ChapterDocument Load(string sample, string? chapterId = null)
    {
        JsonNode chapter = JsonNode.Parse(File.ReadAllText(PathOf(sample)))
            ?? throw new InvalidDataException($"견본 '{sample}'이 비어 있습니다.");

        ChapterDocument document = ChapterDocumentJson.Read(new JsonArray(chapter)).Single();

        if (chapterId is not null)
        {
            document.ChapterId = chapterId;
        }

        return document;
    }

    /// <summary>견본의 그래프 모델 — 검증·증명·내보내기가 읽는 모양.</summary>
    public static ChapterGraphModel Model(string sample, string? chapterId = null) =>
        Load(sample, chapterId).ToGraphModel(PathOf(sample));
}
