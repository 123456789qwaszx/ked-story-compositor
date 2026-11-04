using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Results;

/// <summary>
/// <b>열 때 저절로 다시 묶는다</b> (2026-11-04). director에서 대사를 고친 원고를 다시 열면, 연출이 읽은 발행본과
/// 지금 대사가 다른 카드의 연출 채널을 갱신하고(<see cref="ProjectEditor.EnsurePresentationChannel"/>) 사람에게는
/// <b>결과만</b> 말한다 — "대사가 바뀐 카드 3 · 따라온 명령 12 · 고아 2 · 연출 없는 새 줄 5".
///
/// "발행"은 버전 짝을 추적하는 툴 내부 장치다. 사용자가 알아야 하는 것은 <i>연출이 따라왔나</i>뿐이다 — 전에는 경고
/// 문구("연출 '…'이 읽은 발행본과 현재 대사 내용이 다릅니다. 대사를 다시 발행하고 연출 입력을 갱신하면")가 사람에게
/// 발행 절차를 시켰다(`analysis-2026-11-04-tool-workflow.md` W6).
///
/// ⚠ 바인딩은 건드리지 않는다 — 사라진 줄의 명령은 <b>고아로 남겨 보인다</b>(자동 삭제 금지, 2026-08-21 규칙 그대로).
/// </summary>
public static class CompositionRefresh
{
    /// <param name="DialogueChanged">대사가 바뀌어 다시 묶은 카드 수.</param>
    /// <param name="CommandsCarried">다시 묶은 카드에서 지금도 있는 줄에 걸린 명령 수.</param>
    /// <param name="Orphans">다시 묶은 카드에서 사라진 줄에 걸린 바인딩 수(명령은 남아 있다).</param>
    /// <param name="NewLines">다시 묶은 카드에서 연출이 하나도 없는 대사 줄 수.</param>
    /// <param name="WithoutChannel">연출 채널 자체가 없는 카드 수 — 아직 연출을 시작하지 않은 카드.</param>
    /// <param name="Failures">다시 묶지 못한 카드와 사유(대사를 고정할 수 없는 상태).</param>
    public sealed record Report(
        int DialogueChanged,
        int CommandsCarried,
        int Orphans,
        int NewLines,
        int WithoutChannel,
        IReadOnlyList<string> Failures)
    {
        public bool ChangedAnything => DialogueChanged > 0;

        /// <summary>상태줄 한 마디. 바뀐 것이 없으면 null — 조용한 열기가 시끄러워지지 않는다.</summary>
        public string? Summary
        {
            get
            {
                if (DialogueChanged == 0 && Failures.Count == 0)
                {
                    return null;
                }

                var parts = new List<string>();

                if (DialogueChanged > 0)
                {
                    parts.Add($"대사가 바뀐 카드 {DialogueChanged} — 연출을 다시 묶었습니다 (따라온 명령 {CommandsCarried}" +
                        (Orphans > 0 ? $" · 사라진 줄의 명령 {Orphans} ⚠" : string.Empty) +
                        (NewLines > 0 ? $" · 연출 없는 줄 {NewLines}" : string.Empty) + ")");
                }

                if (Failures.Count > 0)
                {
                    parts.Add("다시 묶지 못함: " + string.Join(" / ", Failures));
                }

                return string.Join(" · ", parts);
            }
        }
    }

    public static Report Apply(ProjectEditor editor, GameDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(editor);

        int changed = 0, carried = 0, orphans = 0, newLines = 0, withoutChannel = 0;
        var failures = new List<string>();

        foreach (DialogueNode node in editor.Project.EnumerateNodes().OfType<DialogueNode>().ToList())
        {
            PresentationNode? presentation =
                NodeExportResolver.SupplyLinkOf(editor.Project, node.Id) is { } supply
                    ? editor.Project.FindPresentation(supply.SourceNodeId)
                    : null;

            if (presentation is null)
            {
                withoutChannel++;
                continue;
            }

            if (!DialogueChangedSince(editor.Project, node, presentation, definition))
            {
                continue;
            }

            PresentationChannelOutcome outcome = editor.EnsurePresentationChannel(node.Id, definition);

            if (outcome.Presentation is null)
            {
                failures.Add($"{node.Name}: {outcome.Problem}");
                continue;
            }

            changed++;

            // 되돌리기가 스냅샷을 다시 풀 수 있으니 Id로 다시 찾는다.
            DialogueNode current = editor.Project.FindDialogue(node.Id)!;
            PresentationNode channel = editor.Project.FindPresentation(outcome.Presentation.Id)!;
            HashSet<string> lines = current.Script.ActiveLines.Select(line => line.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> bound = new(StringComparer.Ordinal);

            foreach (PresentationLineBinding binding in channel.Bindings)
            {
                if (lines.Contains(binding.LineId))
                {
                    carried += binding.Commands.Count;
                    if (binding.Commands.Count > 0)
                    {
                        bound.Add(binding.LineId);
                    }
                }
                else if (binding.Commands.Count > 0)
                {
                    orphans++;
                }
            }

            newLines += lines.Count(line => !bound.Contains(line));
        }

        return new Report(changed, carried, orphans, newLines, withoutChannel, failures);
    }

    /// <summary>연출이 읽은 발행본의 내용 해시와 지금 대사의 해시가 다른가. 고정할 수 없는 대사(막는 문제)는 "바뀜"으로 본다 — 채널이 사유를 말한다.</summary>
    private static bool DialogueChangedSince(
        StoryProject project, DialogueNode node, PresentationNode presentation, GameDefinition? definition)
    {
        string? pinned = presentation.Source is { } source
            ? project.Results.FindDialogue(source.ResultId, source.Version)?.Identity.ContentHash
            : null;

        if (pinned is null)
        {
            return true;
        }

        DialogueDraft draft = DialoguePublisher.Draft(project, node.Id, definition);

        if (!draft.CanPublish)
        {
            return true;
        }

        return !string.Equals(pinned, ResultHash.Compute(DialogueResultJson.WriteBody(draft)), StringComparison.Ordinal);
    }
}
