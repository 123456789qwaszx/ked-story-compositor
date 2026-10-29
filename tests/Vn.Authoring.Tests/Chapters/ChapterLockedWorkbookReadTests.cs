using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 엑셀이 잡고 있는 워크북을 어떻게 읽는가 — 열어 둔 것은 읽히고, 배타적으로 잠긴 것은 사유를 남긴다.
///
/// (2026-10-29: 이 파일은 `chapters/` 감시자의 테스트와 한 클래스였다. 감시자는 엑셀 잠금 감시와 함께
/// 걷혔고 — 그 테스트가 Windows 러너에서 거듭 흔들렸다 — 읽기 둘만 남았다. 리더도 엑셀 연동과 함께
/// 곧 걷힌다.)
/// </summary>
public sealed class ChapterLockedWorkbookReadTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-chapter-watch", Guid.NewGuid().ToString("N"), "chapters");

    public ChapterLockedWorkbookReadTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_folder)!;

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 엑셀이_열어_둔_워크북도_읽힌다()
    {
        string path = WriteChapter("ch05.xlsx", episodeTitle: "열린 채로");

        // 엑셀이 파일을 열어 두는 방식(공유 읽기·쓰기)에서도 읽혀야 한다.
        // 기획자가 엑셀을 켜 둔 채 그래프를 보는 것이 이 레이어의 일상이다.
        using var open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        ChapterEntry entry = ChapterLibrary.Read(path);

        Assert.True(entry.IsReadable);
        Assert.Equal("열린 채로", entry.Model!.Episodes[0].Title);
    }

    [Fact]
    public void 배타적으로_잠긴_워크북은_재시도_후_사유를_남긴다()
    {
        string path = WriteChapter("ch05.xlsx", episodeTitle: "잠김");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ChapterEntry locked = ChapterLibrary.Read(path);

            // 조용히 빈 챕터가 되지 않는다 — 왜 못 읽었는지가 남는다(규칙 14).
            Assert.False(locked.IsReadable);
            Assert.NotNull(locked.OpenFailure);
            Assert.Equal("ch05", locked.ChapterId);
        }

        // 잠금이 풀리면 다음 읽기는 성공한다. 한 번 실패가 영구 실패가 아니다.
        Assert.True(ChapterLibrary.Read(path).IsReadable);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>오류 없이 읽히는 최소 챕터 워크북. 제목만 바꿔 가며 저장을 흉내낸다.</summary>
    private string WriteChapter(string fileName, string episodeTitle)
    {
        string path = Path.Combine(_folder, fileName);

        using var workbook = new XLWorkbook();

        Fill(workbook.AddWorksheet("에피소드"),
        [
            // v14 열 순서 (2026-08-26) — 이 시험이 보는 것은 감시자이지 이행이 아니므로
            // 지금 규격 그대로 적는다.
            ["EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y", "메모"],
            ["ep1", "Story_ep1", episodeTitle, null, "0", "0", null],
            ["ep2", "Story_ep2", "둘째 화", null, "200", "0", null]
        ]);

        Fill(workbook.AddWorksheet("간선"),
        [
            ["출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문"],
            ["ep1", "ep2", null, null, "FALSE", null]
        ]);

        Fill(workbook.AddWorksheet("조건"),
        [
            ["라벨", "조건식", "설명"],
            ["신뢰높음", "trust >= 3", "라루를 신뢰"]
        ]);

        Fill(workbook.AddWorksheet("스탯"),
        [
            ["타입", "스탯키", "표시명", "초기값", "최소", "최대"],
            [null, "trust", "신뢰", "0", "0", "10"],
            [null, "anger", "분노", "0", "0", "10"]
        ]);

        Fill(workbook.AddWorksheet("픽스처"),
        [
            ["픽스처명", "활성", "trust", "anger", "고정 선택 (에피소드ID→도착ID)"],
            ["기본", "TRUE", "0", "0", "ep1→ep2"]
        ]);

        workbook.SaveAs(path);
        return path;
    }

    private static void Fill(IXLWorksheet sheet, string?[][] rows)
    {
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 1, column + 1).SetValue(value);
                }
            }
        }
    }
}
