namespace Vn.App;

/// <summary>
/// 이 앱의 이름 — 창 제목 · 설정과 로그 폴더 · 시작 실패 문구가 같은 글자를 쓴다
/// (2026-10-29, <c>docs/plans/compositor-split.md</c> C3).
///
/// ⚠ 설정 폴더가 ked-story-director(옛 VnTool)와 <b>갈린다</b>. 두 툴이 한 <c>settings.json</c>을 쓰면
/// 한쪽이 연 프로젝트가 다른 쪽의 "최근 프로젝트"를 덮는다.
/// </summary>
internal static class AppIdentity
{
    public const string Name = "ked-story-compositor";
}
