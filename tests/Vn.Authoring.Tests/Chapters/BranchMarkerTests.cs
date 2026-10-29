using Vn.Authoring.Editing;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>「조건 분기」 표식</b> — 분기 하나가 곧 포트 하나 (R7 P-5 · `docs/plans/R7.md`). 한 줄에 여럿일 수 있다(2026-10-29).
///
/// ⛔ <b>조건이 아니다.</b> 연출 그래프는 "어디서 갈라지는가"만 짚는다 — 성립하든 말든
/// 다녀오고, 다녀온 자유 씬이 제 첫머리에서 보고 아니면 곧바로 돌아온다.
///
/// ⚠ 갈래(<c>BeginIf</c>…)와 달리 <b>구역이 아니다</b>. 줄 사이에 끼는 카드 한 장이라
/// 여는 전환도 닫는 전환도 없다 — 그래서 전환이 아니라 <b>줄에 붙는 표식</b>이다.
/// </summary>
public sealed class BranchMarkerTests
{
    [Fact]
    public void 표식_하나가_포트_하나를_뚫는다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        Assert.DoesNotContain(Ports(editor, node), port => port.Kind == ExitPortKind.Detour);

        node.RequireExtension(lineId).DetourTargetNodeIds.Add(Target(editor).Id);

        ExitPort port = Assert.Single(Ports(editor, node), item => item.Kind == ExitPortKind.Detour);

        Assert.Equal(lineId, port.BranchOpenLineId);
        Assert.Equal(Target(editor).Id, port.TargetNodeId);
        Assert.True(port.IsConnected);
    }

    [Fact]
    public void 조건이_없어도_포트가_선다()
    {
        // 갈래는 `ConditionId`가 있어야 열리지만 이 표식은 조건을 아예 안 진다 —
        // 갈지 말지는 다녀온 곳이 정한다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        node.RequireExtension(lineId).DetourTargetNodeIds.Add(Target(editor).Id);

        Assert.DoesNotContain(
            node.LineExtensions.SelectMany(extension => extension.Transitions),
            transition => transition.OpensBranch);

        Assert.Single(Ports(editor, node), item => item.Kind == ExitPortKind.Detour);
    }

    [Fact]
    public void 목표는_그_줄이_지고_갈래_출구_장부에_안_적힌다()
    {
        // ⛔ 갈래 출구 장부(`BranchExits`)에 적으면 <b>여는 전환이 없는 고아</b>가 되고,
        //    그 칸은 저장할 때 전환 안에 실리므로 통째로 사라진다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, Target(editor).Id);

        Assert.Equal([Target(editor).Id], node.FindExtension(lineId)!.DetourTargetNodeIds);
        Assert.Empty(node.BranchExits);
    }

    [Fact]
    public void 끊으면_표식도_지워진다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, Target(editor).Id);

        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, null);

        Assert.Empty(node.FindExtension(lineId)?.DetourTargetNodeIds ?? []);
        Assert.DoesNotContain(Ports(editor, node), port => port.Kind == ExitPortKind.Detour);
    }

    [Fact]
    public void 저장하고_다시_열어도_남는다()
    {
        // 저장이 안 되면 다음에 열었을 때 포트가 통째로 사라진다 — 뚫어 둔 자리가 없어진다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        node.RequireExtension(lineId).DetourTargetNodeIds.Add(Target(editor).Id);

        StoryProject reopened = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(editor.Project));

        DialogueNode again = reopened.EnumerateNodes().OfType<DialogueNode>()
            .First(item => string.Equals(item.Id, node.Id, StringComparison.Ordinal));

        Assert.Equal([Target(editor).Id], again.FindExtension(lineId)!.DetourTargetNodeIds);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ExitPort> Ports(ProjectEditor editor, DialogueNode node) =>
        NodeConnections.PortsOf(node, editor.Project);

    private static DialogueNode Target(ProjectEditor editor) =>
        editor.Project.EnumerateNodes().OfType<DialogueNode>()
            .First(node => node.Name == "곁가지");

    /// <summary>대사 한 줄짜리 에피소드 노드와, 다녀올 자유 씬 하나.</summary>
    private static (ProjectEditor Editor, DialogueNode Node, string LineId) World()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");
        editor.AddEpisode("ch01", "root", title: "root", 0, 0);

        DialogueNode node = editor.AddDialogueNode(fileId, 0, 0, "root");
        node.MarkedEpisodeId = "root";

        editor.AddDialogueNode(fileId, 400, 0, "곁가지");

        string lineId = node.Script.ActiveLines.First().Id;

        return (editor, node, lineId);
    }

    // ── 대본에 나가는 모양 (R7 P-5, 이미터) ────────────────────────────────

    [Fact]
    public void 표식은_조건_없이_detour_한_줄로_나간다()
    {
        // ⛔ `<<if>>`로 감싸지 않는다 — 연출 그래프는 "어디서 갈라지는가"만 짚고, 갈지
        //    말지는 다녀온 곳이 제 첫머리에서 정한다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        node.RequireExtension(lineId).DetourTargetNodeIds.Add(Target(editor).Id);

        RenderedDocument document = Rendered(editor, node);

        // ⚠ 조각은 노드 <b>Id</b>를 든다 — 번들 이미터가 제 이름 규칙으로 옮긴다.
        RenderedSegment detour = Assert.Single(
            document.Segments, segment => segment.Kind == RenderedSegmentKind.BranchDetour);
        Assert.Equal(Target(editor).Id, detour.TargetNodeId);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.ConditionBegin);
    }

    [Fact]
    public void 표식이_없으면_아무것도_안_나간다()
    {
        (ProjectEditor editor, DialogueNode node, _) = World();

        Assert.DoesNotContain(
            Rendered(editor, node).Segments, segment => segment.Kind == RenderedSegmentKind.BranchDetour);
    }

    /// <summary>발행본을 문서 조각으로 편 것 — 게임용 Yarn이 이 조각들을 그대로 옮긴다.</summary>
    private static RenderedDocument Rendered(ProjectEditor editor, DialogueNode node) =>
        ResultDocumentComposer.Compose(editor.PublishDialogue(node.Id).Result, project: editor.Project);

    // ── 「분기 추가」 (R7 P-5, 편집 명령) ───────────────────────────────────

    [Fact]
    public void 한_번에_표식과_에피소드가_함께_선다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.Equal([made.Id], node.FindExtension(lineId)!.DetourTargetNodeIds);
        Assert.Single(Ports(editor, node), port => port.Kind == ExitPortKind.Detour);

        // ⚠ 뒤집힌 자리다 (R7 P-6 · 결정 ⑤ · 2026-09-17). 전에는 자유 씬이라 표식이 없었다 —
        //    이제 자유 씬이라는 종류가 없으므로 다녀오는 곳도 <b>그냥 에피소드</b>다.
        Assert.Equal(made.Name, made.MarkedEpisodeId);
        Assert.Contains(
            editor.Project.Chapters.Single().Episodes,
            episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal));

        // 같은 판에 선다 — 다녀오는 길이 판을 넘지 않는다.
        Assert.Equal(
            editor.Project.FindFileContainingNode(node.Id)!.Id,
            editor.Project.FindFileContainingNode(made.Id)!.Id);
    }

    [Fact]
    public void 다녀오는_에피소드는_출발과_같은_장면에_선다()
    {
        // 곁가지는 제 본줄 옆에 있어야 판에서 읽힌다 — 장면을 안 주면 `장면 밖`으로 밀려
        // 챕터 프레임 바깥에 혼자 선다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        ManuscriptFixture.Episode(editor.Project, "ch01", "root", episode => episode with { SceneId = "sc_아침" });

        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.Equal(
            "sc_아침",
            editor.Project.Chapters.Single().Episodes
                .Single(episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal))
                .SceneId);
    }

    [Fact]
    public void 다녀오는_에피소드는_도달_불가로_울지_않는다()
    {
        // ⛔ 들어오는 간선이 없는 것이 맞다 — 챕터 진행이 아니라 대본의 `<<detour>>`로
        //    들어간다. 그래서 도달성 증명의 판정은 <b>옳고, 고치지 않는다</b>: 그 증명기는
        //    저쪽 런타임의 오라클이고 코퍼스로 고정돼 있어 여기서 답을 바꾸면 둘이 조용히
        //    갈린다. 이미 있는 `도달불가 허용`이 정확히 이 자리를 위한 칸이다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        Assert.True(editor.Project.Chapters.Single().Episodes
            .Single(episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal))
            .AllowUnreachable);
    }

    [Fact]
    public void 손으로_세운_카드는_도달_불가로_운다()
    {
        // ⚠ 「분기 추가」와 가르는 자리다. 작가가 판에 그냥 세운 카드는 <b>아직 안 이은 것</b>
        //    이므로 짚어 줘야 한다 — 허용을 기본값으로 두면 고아 에피소드가 조용히 쌓인다.
        (ProjectEditor editor, _, _) = World();

        DialogueNode made = editor.AddDialogueNode(
            editor.Project.Files[0].Id, name: "혼자선카드");

        Assert.False(editor.Project.Chapters.Single().Episodes
            .Single(episode => string.Equals(episode.EpisodeId, made.Name, StringComparison.Ordinal))
            .AllowUnreachable);
    }

    [Fact]
    public void 되돌리기_한_번에_셋이_함께_돌아간다()
    {
        // ⛔ 갈라 두면 표식만 사라지고 <b>아무도 안 부르는 씬</b>이 판에 남는다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();

        int before = editor.Project.Files[0].Nodes.Count;
        editor.AddBranchMarker(node.Id, lineId);

        editor.Undo();

        Assert.Equal(before, editor.Project.Files[0].Nodes.Count);
        Assert.Empty(editor.Project.EnumerateNodes().OfType<DialogueNode>()
            .First(item => item.Id == node.Id)
            .FindExtension(lineId)?.DetourTargetNodeIds ?? []);
    }

    [Fact]
    public void 한_자리에_여럿을_두면_차례로_다녀온다()
    {
        // ⛔ 2026-10-29까지는 줄 하나에 하나였다(칸이 하나뿐이었다). 소유자: *"하나의 줄에 여러개의 분기가 있을 수
        //    있게"* — 둘째는 첫째 <b>뒤에</b> 서고, 대본에도 그 차례로 `<<detour>>`가 나간다. 각자 제 첫머리 조건으로
        //    들어갈지 정한다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        DialogueNode first = editor.AddBranchMarker(node.Id, lineId);
        DialogueNode second = editor.AddBranchMarker(node.Id, lineId);

        Assert.Equal([first.Id, second.Id], node.FindExtension(lineId)!.DetourTargetNodeIds);
        Assert.Equal(
            [first.Id, second.Id],
            Ports(editor, node).Where(port => port.Kind == ExitPortKind.Detour).Select(port => port.TargetNodeId));
        Assert.Equal(
            [first.Id, second.Id],
            Rendered(editor, node).Segments
                .Where(segment => segment.Kind == RenderedSegmentKind.BranchDetour)
                .Select(segment => segment.TargetNodeId));

        // 두 카드는 서로 다른 빈자리에 선다.
        Assert.NotEqual((first.Layout.X, first.Layout.Y), (second.Layout.X, second.Layout.Y));
    }

    [Fact]
    public void 포트로_바꾸면_그_분기_하나만_바뀐다()
    {
        // 분기의 신원은 줄과 다녀갈 노드다 — 줄만으로는 어느 분기인지 모른다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        DialogueNode first = editor.AddBranchMarker(node.Id, lineId);
        DialogueNode second = editor.AddBranchMarker(node.Id, lineId);
        ExitPort firstPort = Ports(editor, node).Single(port => port.TargetNodeId == first.Id);

        Assert.NotEqual(
            firstPort.ExitKey,
            Ports(editor, node).Single(port => port.TargetNodeId == second.Id).ExitKey);

        // 다른 곳으로 이으면 제자리에서 바뀐다 — 다녀오는 차례가 그대로다.
        editor.SetExitTarget(firstPort, Target(editor).Id);
        Assert.Equal([Target(editor).Id, second.Id], node.FindExtension(lineId)!.DetourTargetNodeIds);

        // 끊으면 그 하나만 걷힌다.
        editor.SetExitTarget(Ports(editor, node).Single(port => port.TargetNodeId == Target(editor).Id), null);
        Assert.Equal([second.Id], node.FindExtension(lineId)!.DetourTargetNodeIds);

        // 줄만 주고 끊으면 그 줄의 분기를 모두 끊는다.
        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, null);
        Assert.Empty(node.FindExtension(lineId)?.DetourTargetNodeIds ?? []);
    }

    [Fact]
    public void 하나면_문자열로_여럿이면_배열로_적고_결과와_대본까지_간다()
    {
        // 하나뿐이면 2026-10-29 전과 같은 바이트다 — 분기가 하나인 원고 · 결과는 다시 써도 그대로이고 결과 해시도
        // 그대로다(새 버전이 안 생긴다). 여럿이면 배열이다.
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        DialogueNode first = editor.AddBranchMarker(node.Id, lineId);

        string single = StoryFileJson.Write(editor.Project.FindFileContainingNode(node.Id)!);
        Assert.Contains($"\"detour\": \"{first.Id}\"", single, StringComparison.Ordinal);

        DialogueNode second = editor.AddBranchMarker(node.Id, lineId);
        StoryFile reread = StoryFileJson.Read(StoryFileJson.Write(editor.Project.FindFileContainingNode(node.Id)!));
        Assert.Equal(
            [first.Id, second.Id],
            reread.Nodes.OfType<DialogueNode>().Single(item => item.Id == node.Id)
                .FindExtension(lineId)!.DetourTargetNodeIds);

        // 발행 결과도 둘 다 든다 — 파일로 갔다 와도.
        DialogueResult published = editor.PublishDialogue(node.Id).Result;
        var results = new Vn.Authoring.Results.ResultRepository();
        results.Add(published);
        DialogueResult back = ResultStoreJson.Read(ResultStoreJson.Write(results)).DialogueResults.Single();
        Assert.Equal([first.Id, second.Id], back.Lines.Single(line => line.LineId == lineId).DetourTargetNodeIds);
        Assert.True(ResultHash.IsIntact(back));

        // 게임용 Yarn에는 그 차례로 두 줄 — 그다음이 그 줄이다.
        string story = YarnBundleEmitter.Emit(published, project: editor.Project).StoryText;
        int firstAt = story.IndexOf("<<detour ", StringComparison.Ordinal);
        int secondAt = story.IndexOf("<<detour ", firstAt + 1, StringComparison.Ordinal);
        Assert.True(firstAt >= 0 && secondAt > firstAt, story);
    }

    [Fact]
    public void 같은_줄에_같은_곳은_두_번_들지_않는다()
    {
        (ProjectEditor editor, DialogueNode node, string lineId) = World();
        DialogueNode made = editor.AddBranchMarker(node.Id, lineId);

        editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, made.Id);

        Assert.Equal([made.Id], node.FindExtension(lineId)!.DetourTargetNodeIds);
    }

    [Fact]
    public void 그_노드의_줄이_아니면_거절한다()
    {
        (ProjectEditor editor, DialogueNode node, _) = World();

        Assert.Throws<InvalidOperationException>(
            () => editor.AddBranchMarker(node.Id, "ln_아무거나"));
    }
}
