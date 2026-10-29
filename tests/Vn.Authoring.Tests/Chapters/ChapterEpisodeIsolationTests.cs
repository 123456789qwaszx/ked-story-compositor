using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터별 원고 격리 (2026-08-16 소유자 보고) — "다른 챕터의 에피소드를 눌렀는데 기존 이름이
/// 같은 대본 엑셀이 열린다."
///
/// EpisodeId는 <b>챕터 안에서만</b> 유일하다. 원고는 카드의 대본에 살고, 카드는 <b>그 챕터의
/// 판에서만</b> 찾는다(<see cref="EpisodeNaming.CardFor"/>) — 같은 이름의 에피소드가 두 챕터에
/// 있어도 카드·대본이 따로다. 조회 자체의 규칙은 <c>EpisodeNamingLookupTests</c>가 진다.
///
/// (2026-10-29 · D2: 대본 파일 경로(<c>episodes/{챕터}/</c>)·평면 대본 입양 검사는 엑셀 연동과 함께 걷혔다.)
/// </summary>
public sealed class ChapterEpisodeIsolationTests
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" } ] }
        """)!;

    [Fact]
    public void 같은_이름의_에피소드가_챕터마다_다른_카드와_대본을_갖는다()
    {
        var editor = new ProjectEditor(new StoryProject());
        editor.EnsureChapter("ch00");
        editor.EnsureChapter("ch01");

        editor.AddEpisode("ch00", "new01", "new01", 0, 0);
        editor.AddEpisode("ch01", "new01", "new01", 0, 0); // 두 번째도 정말 따로 선다

        DialogueNode cardA = EpisodeNaming.CardFor(editor.Project, "ch00", "new01")!;
        DialogueNode cardB = EpisodeNaming.CardFor(editor.Project, "ch01", "new01")!;

        Assert.NotEqual(cardA.Id, cardB.Id);
        Assert.NotSame(cardA.Script, cardB.Script);

        // 한쪽 원고를 고쳐도 다른 쪽은 그대로다 — 이것이 보고된 버그의 핵심이다.
        ScriptLine line = editor.InsertScriptLine(cardA.Id);
        editor.SetScriptLineText(cardA.Id, line.Id, "라루", "ch00의 대사");

        Assert.Contains("ch00의 대사", TextOf(editor.Project, cardA));
        Assert.DoesNotContain("ch00의 대사", TextOf(editor.Project, cardB));
    }

    /// <summary>그 카드가 들고 있는 글 전부 — 어느 원고가 들어갔는지의 근거.</summary>
    private static string TextOf(StoryProject project, DialogueNode node) => string.Join(
        " | ",
        node.Script.Locales
            .SelectMany(locale => locale.Entries.Values)
            .Select(line => line.Text));
}
