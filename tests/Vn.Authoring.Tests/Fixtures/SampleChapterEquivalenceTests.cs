using System.Text.Json;
using System.Text.Json.Nodes;
using Vn.Authoring.Chapters;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Fixtures;

/// <summary>
/// <b>JSON 견본 = 엑셀 견본을 들여온 결과</b> — 엑셀을 걷기 전의 마지막 다리(2026-10-29).
///
/// 견본을 엑셀에서 프로젝트 형식으로 옮기면서, 옮긴 것이 리더가 읽던 것과 한 글자도 다르지 않은지를
/// 여기서 붙든다. 엑셀 연동과 견본 워크북이 걷히는 커밋에서 이 테스트도 함께 걷힌다 — 그 뒤로는 JSON이
/// 견본의 원본이다.
/// </summary>
public sealed class SampleChapterEquivalenceTests
{
    [Theory]
    [InlineData(SampleChapters.Corridor, "chapter-graph-sample.xlsx")]
    [InlineData(SampleChapters.Contract, "ch01.contract-sample.xlsx")]
    public void JSON_견본이_엑셀_견본을_들여온_것과_같다(string sample, string workbook)
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", workbook));

        ChapterDocument imported = ChapterDocument.From(ChapterWorkbookReader.Read(path));
        imported.ChapterId = sample;

        JsonNode expected = ChapterDocumentJson.Write([imported])![0]!;
        JsonNode actual = ChapterDocumentJson.Write([SampleChapters.Load(sample)])![0]!;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 다르면 들여온 쪽을 통째로 보인다 — 견본을 고칠 때 그대로 옮겨 적으면 된다.
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            "견본 JSON이 엑셀에서 들여온 것과 다릅니다. 들여온 것:\n" + expected.ToJsonString(options));
    }
}
