using Vn.Authoring.Editing;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 개명의 <b>유일한 자리</b> (2026-08-24 소유자 보고: "챕터의 이름을 바꾸니까,
/// 에피소드들의 대화가 없다고 나오고, 엑셀도 새로운게 열리네. 또 연출그래프에서도
/// 조건노드가 이전의 챕터를 받고 있어").
///
/// 개명은 <see cref="ProjectEditor.RenameChapter"/> 한 번이다: <c>Project.Chapters</c>의 Id · 판 이름 ·
/// 조건 공급 노드 이름이 한 변경으로 바뀌고 되돌리기 한 번에 돌아온다.
///
/// (2026-10-29 · D2: 그 뒤에 따라 하던 <c>chapters/{Id}.xlsx</c>·<c>episodes/{Id}/</c> 옮기기는 엑셀
/// 연동과 함께 걷혔다. 남은 파일은 진행 JSON 하나이고, 그것은 옮기지 않고 이름만 말한다.)
/// </summary>
public static class ChapterRenamer
{
    /// <param name="SupplyRenamed">조건 공급 노드의 이름이 따라갔으면 참.</param>
    /// <param name="StaleExport">
    /// 옛 이름으로 내보낸 진행 JSON이 남아 있으면 그 파일 이름. <b>지우지 않는다</b> —
    /// 산출물이라도 남의 폴더(개발자가 가져가는 자리)에 있는 파일이다. 대신 <b>이름을 말해</b>
    /// 사람이 지우게 한다: 그냥 두면 런타임이 없는 챕터를 계속 싣는다.
    /// <see cref="ChapterDeleter"/>도 같은 규칙을 쓴다.
    /// </param>
    public sealed record Result(
        bool Renamed,
        string? Failure,
        bool SupplyRenamed = false,
        string? StaleExport = null)
    {
        public static Result Fail(string reason) => new(false, reason);
    }

    /// <param name="projectManifestPath">
    /// 프로젝트 파일 경로 — 옛 이름의 진행 JSON이 남았는지 보는 데만 쓴다. 아직 저장 안 한
    /// 프로젝트(null)면 볼 것이 없다.
    /// </param>
    public static Result Rename(
        ProjectEditor editor, string? projectManifestPath, string oldId, string newId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        oldId = (oldId ?? string.Empty).Trim();
        newId = (newId ?? string.Empty).Trim();

        if (oldId.Length == 0 || newId.Length == 0)
        {
            return Result.Fail("챕터 이름이 비어 있습니다.");
        }

        if (string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            return Result.Fail("이름이 같습니다.");
        }

        if (editor.FindChapter(oldId) is null)
        {
            return Result.Fail($"챕터 '{oldId}'가 프로젝트에 없습니다.");
        }

        if (editor.FindChapter(newId) is not null)
        {
            return Result.Fail($"챕터 '{newId}'가 이미 있습니다.");
        }

        // 챕터 Id · 판 · 배관이 한 변경으로.
        ChapterRenameEffect effect = editor.RenameChapter(oldId, newId);

        string? stale = projectManifestPath is null
            ? null
            : ChapterExportService.ExportPathFor(projectManifestPath, oldId);

        return new Result(
            true, null,
            effect.SupplyRenamed,
            stale is not null && File.Exists(stale) ? Path.GetFileName(stale) : null);
    }
}
