using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Editing;

/// <summary>
/// 대본을 바꾸는 편집 명령.
///
/// <b>화자와 대사를 바꾸는 코드는 이 파일 안에만 있다.</b> DialogueNode를 지나는 경로는
/// 없다. 그래야 "한 줄을 고쳤을 때 권위 있는 데이터가 하나만 바뀐다"가 코드 구조로
/// 보장된다.
/// </summary>
public sealed partial class ProjectEditor
{
    // ⛔ `AddScript` · `EnsureDialogueScript` · `SetDialogueScript`는 2026-10-29에 걷혔다 — 카드가 제 대본을 든다
    //    (director `docs/plans/script-merge.md`). 대본을 고르거나 잇는 길이 없다. 줄 명령은 카드 Id로 부른다.

    /// <summary>카드의 대본에 빈 줄 하나를 끼워 넣는다. 새 LineId를 발급한다.</summary>
    public ScriptLine InsertScriptLine(string dialogueNodeId, int? index = null, string? locale = null)
    {
        ScriptDocument script = RequireDialogue(dialogueNodeId).Script;
        var line = new ScriptLine(_newLineId());
        string targetLocale = locale ?? script.PrimaryLocale;

        Mutate(() =>
        {
            int at = index ?? script.Lines.Count;
            script.Lines.Insert(Math.Clamp(at, 0, script.Lines.Count), line);
            script.RequireLocale(targetLocale).Entries[line.Id] = LocalizedLine.Empty;
        });

        return line;
    }

    /// <summary>
    /// 화자와 대사를 바꾼다. <b>도구 안에서 대사 본문을 바꾸는 유일한 명령이다.</b>
    /// 내용만 바뀌므로 편집 컨트롤을 다시 만들지 않는다.
    /// </summary>
    public void SetScriptLineText(
        string dialogueNodeId,
        string lineId,
        string speaker,
        string text,
        string? locale = null)
    {
        ScriptDocument script = RequireDialogue(dialogueNodeId).Script;

        if (script.FindLine(lineId) is not { } line)
        {
            return;
        }

        string targetLocale = locale ?? script.PrimaryLocale;
        LocalizedLine current = script.Text(lineId, targetLocale);
        var next = new LocalizedLine(speaker, text);

        if (current == next)
        {
            return;
        }

        Mutate(ProjectChangeKind.DialogueContent, () =>
        {
            script.RequireLocale(targetLocale).Entries[lineId] = next;

            // 기준 locale의 문구가 바뀌면 번역·녹음이 다시 봐야 한다는 뜻이다.
            if (string.Equals(targetLocale, script.PrimaryLocale, StringComparison.Ordinal))
            {
                line.Revision++;
            }
        });
    }
}
