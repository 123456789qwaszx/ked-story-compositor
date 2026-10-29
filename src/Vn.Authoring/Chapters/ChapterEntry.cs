namespace Vn.Authoring.Chapters;

/// <summary>
/// 화면이 쓰는 챕터 한 줄 — 프로젝트의 챕터를 그래프 모델로 편 것.
///
/// ⛔ 2026-10-29(D2)까지는 여기에 <c>Path</c>(워크북 자리)와 <c>OpenFailure</c>(못 연 사유)가 있었다.
/// 챕터가 엑셀 파일이던 시절의 것이고, 엑셀 연동이 걷히며 함께 사라졌다 — 챕터는 프로젝트에 살고
/// 열지 못할 파일이 없다.
/// </summary>
public sealed record ChapterEntry(string ChapterId, ChapterGraphModel Model)
{
    /// <summary>모델 진단에 오류가 있는가. 왼쪽 목록이 붉게 표시한다.</summary>
    public bool HasErrors => Model.HasErrors;
}
