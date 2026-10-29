using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
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
    public ScriptDocument AddScript(string? name = null)
    {
        var script = new ScriptDocument(name: name ?? NextScriptName());
        script.RequireLocale(script.PrimaryLocale);

        Mutate(() => Project.Scripts.Add(script));
        return script;
    }

    /// <summary>
    /// 대본 없는 대사 노드(가져오기 시절의 옛 프로젝트)에 전용 대본을 만들어 잇는다 (X4).
    /// 이미 대본이 있으면 그대로 돌려준다.
    /// </summary>
    public ScriptDocument EnsureDialogueScript(string dialogueNodeId)
    {
        DialogueNode node = RequireDialogue(dialogueNodeId);

        if (Project.FindScript(node.ScriptId) is { } existing)
        {
            return existing;
        }

        var script = new ScriptDocument(name: $"{node.Name} 대본");
        script.RequireLocale(script.PrimaryLocale);

        Mutate(() =>
        {
            Project.Scripts.Add(script);
            node.ScriptId = script.Id;
        });

        return script;
    }

    /// <summary>대본에 빈 줄 하나를 끼워 넣는다. 새 LineId를 발급한다.</summary>
    public ScriptLine InsertScriptLine(string scriptId, int? index = null, string? locale = null)
    {
        ScriptDocument script = RequireScript(scriptId);
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
        string scriptId,
        string lineId,
        string speaker,
        string text,
        string? locale = null)
    {
        ScriptDocument script = RequireScript(scriptId);

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

    /// <summary>이 대사 노드가 읽을 대본을 정한다. 조건 구조는 LineId 기준으로 그대로 남는다.</summary>
    public void SetDialogueScript(string dialogueNodeId, string? scriptId)
    {
        DialogueNode node = RequireDialogue(dialogueNodeId);

        if (scriptId is not null && Project.FindScript(scriptId) is null)
        {
            throw new InvalidOperationException($"대본 '{scriptId}'를 찾을 수 없습니다.");
        }

        if (string.Equals(node.ScriptId, scriptId, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(() => node.ScriptId = scriptId);
    }

    private ScriptDocument RequireScript(string scriptId)
    {
        return Project.FindScript(scriptId)
            ?? throw new InvalidOperationException($"대본 '{scriptId}'를 찾을 수 없습니다.");
    }

    private string NextScriptName() => $"대본 {Project.Scripts.Count + 1}";
}
