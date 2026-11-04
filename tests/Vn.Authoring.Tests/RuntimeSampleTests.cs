using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// <c>samples/RuntimeSamples</c> — ked-presentation-runtime의 예제 두 챕터(축제 전날 · 연출 데모)를 <b>두 툴로</b> 낸 것 (2026-11-04).
/// 소유자: <i>"목표는 런타임 예제를 손 대본 대신 두 툴로 내는 것입니다."</i>
///
/// 원고(<c>*.vnproject.json</c> · <c>story/</c> · <c>game.definition.json</c>)는 ked-story-director의
/// <c>samples/RuntimeSamples</c> 사본이고(K3 — 이 툴은 읽기만 한다), 이 툴의 파일은 그 옆의
/// <c>*.vncompose.json</c> · <c>results.vnresults.json</c>, 출력은 <c>yarn/</c>이다. 진행 JSON은 director가 낸다.
///
/// 이 테스트는 연출 파일을 다시 열어 내보낸 Yarn이 <c>yarn/</c>에 실린 것과 <b>같다</b>는 것을 잠근다 —
/// 연출이나 이미터를 고치고 예제를 다시 내지 않으면 운다.
/// </summary>
public sealed class RuntimeSampleTests
{
    private static string FolderOf(string folder) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "RuntimeSamples", folder));

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static Dictionary<string, string> Export(string folder, string chapterId)
    {
        string manifest = Path.Combine(FolderOf(folder), chapterId + ".vnproject.json");
        CompositionLoadResult loaded = CompositionStore.Load(manifest);
        GameDefinition definition = GameDefinition.LoadBeside(manifest);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (DialogueNode node in loaded.Project.EnumerateNodes().OfType<DialogueNode>())
        {
            LiveComposition composition = LiveNodeComposer.Compose(
                loaded.Project, node.Id, definition, DateTimeOffset.UnixEpoch);

            Assert.True(composition.CanWrite, $"{node.Name}: {string.Join(" / ", composition.BlockingProblems)}");
            Assert.True(composition.Warnings.Count == 0, $"{node.Name}: {string.Join(" / ", composition.Warnings)}");
            Assert.True(composition.Bundle!.Problems.Count == 0,
                $"{node.Name}: {string.Join(" / ", composition.Bundle.Problems.Select(problem => problem.Message))}");

            files.Add(composition.Bundle.StoryFileName, composition.Bundle.StoryText);
        }

        return files;
    }

    [Theory]
    [InlineData("FestivalEve", "festival_eve")]
    [InlineData("PresentationDemo", "presentation_demo")]
    public void 다시_낸_Yarn이_실린_것과_같다(string folder, string chapterId)
    {
        Dictionary<string, string> exported = Export(folder, chapterId);
        string yarnFolder = Path.Combine(FolderOf(folder), "yarn");

        Assert.Equal(
            Directory.GetFiles(yarnFolder, "*.yarn").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToList(),
            exported.Keys.Order(StringComparer.Ordinal).ToList());

        foreach ((string fileName, string text) in exported)
        {
            Assert.Equal(Lf(File.ReadAllText(Path.Combine(yarnFolder, fileName))), Lf(text));
        }
    }

    [Fact]
    public void 축제_전날의_조건은_다녀가는_카드가_스스로_본다()
    {
        // 손 대본의 <<if>>/<<elseif>>/<<else>>는 툴의 모양으로 다시 짰다(2026-11-04 소유자: "툴에서 else를 사용할 수
        // 있는 방법 자체가 없다") — 부르는 쪽은 조건 없는 <<detour>> 줄, 조건은 다녀가는 카드의 첫머리(else 쪽은 여집합).
        Dictionary<string, string> exported = Export("FestivalEve", "festival_eve");

        string wrapup = exported["festival_eve_Festival_Wrapup.yarn"];
        Assert.Contains("<<detour Festival_Wrapup_Remade>>\n<<detour Festival_Wrapup_Sticker>>\n시구레: 이제 교무실에", Lf(wrapup));
        Assert.DoesNotContain("<<if", wrapup);

        Assert.Contains("<<if stat(\"성실\") >= 2>>", exported["festival_eve_Festival_Wrapup_Remade.yarn"]);
        Assert.Contains("<<if stat(\"성실\") < 2>>", exported["festival_eve_Festival_Wrapup_Sticker.yarn"]);
        Assert.Contains("<<if stat(\"요령\") >= 3>>", exported["festival_eve_Festival_Staffroom_Kasmi.yarn"]);
        Assert.Contains("<<if stat(\"성실\") >= 3 && stat(\"요령\") < 3>>", exported["festival_eve_Festival_Staffroom_Praise.yarn"]);
        Assert.Contains("<<if stat(\"성실\") < 3 && stat(\"요령\") < 3>>", exported["festival_eve_Festival_Staffroom_Pass.yarn"]);

        // 명령만 든 조건 블록(카스미 퇴장)은 걸 줄이 없어 그 갈래 안으로 옮겼다(2026-11-04 소유자 Q4).
        Assert.Contains("<<fade_out s3 12fr>>", exported["festival_eve_Festival_Staffroom_Kasmi.yarn"]);
        Assert.DoesNotContain("s3", exported["festival_eve_Festival_Staffroom.yarn"]);
        Assert.Contains("<<24fr>>\n캐릭터 퇴장 #boxhide #auto #line:", Lf(exported["festival_eve_Festival_Staffroom.yarn"]));
    }
}
