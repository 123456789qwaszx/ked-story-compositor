using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 코어 재반입(2026-11-04)이 소비자에게 가져온 두 축 — <b>배경은 슬롯</b>과 <b>모션 폴드</b>.
///
/// 런타임은 2026-10-29에 배경 리그를 걷었다: <c>bg_*</c>는 캐스팅하지 않은 캐릭터 슬롯이다.
/// 코어 상태에는 그 슬롯이 서고, 프리뷰는 배경을 지금처럼 배경 층으로 그린다 — 인물 칸이 아니다.
/// 모션은 모션 툴이 낸 <c>motions.json</c>을 튜닝에 실어 코어 리듀서가 접는다 — 재생과 같은 클립이다.
/// </summary>
public sealed class CoreResyncTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));

    private static RuntimeTuningLibrary LoadLibrary() =>
        RuntimeTuningLibrary.Load(FixtureDirectory, (1920, 1080));

    private static PresentationResultCommand Command(string definitionId, params (string Key, string Value)[] args) =>
        new(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static readonly PresentationResultCommand[] CastHarin =
    [
        Command("char_rig_cast.slot", ("slotKey", "c1")),
        Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "harin"), ("emotionKey", "1")),
    ];

    [Fact]
    public void 배경은_코어에_슬롯으로_서지만_프리뷰에선_배경_층이다()
    {
        StageReducerTuning tuning = LoadLibrary().Tuning!;

        CoreStageFoldResult result = CoreStageFold.Fold(
            Catalog,
            [Command("background.bg_spawn", ("rigKey", "bg0"), ("spriteKey", "class_day")), .. CastHarin],
            [],
            tuning);

        Assert.True(result.CoreState!.HasSlot("bg0"));
        Assert.Equal("class_day", result.State.BackgroundKey);
        Assert.False(result.State.Slots.ContainsKey("bg0"));
        Assert.True(result.State.Slots.ContainsKey("c1"));
    }

    [Fact]
    public void 배경_치수_표로_배경_슬롯의_사이징이_접힌다()
    {
        RuntimeTuningLibrary library = LoadLibrary();

        Assert.NotNull(library.Tuning!.BackgroundDimensions);

        CoreStageFoldResult result = CoreStageFold.Fold(
            Catalog,
            [Command("background.bg_spawn", ("rigKey", "bg0"), ("spriteKey", "class_day"))],
            [],
            library.Tuning);

        Assert.Empty(result.CoreState!.Unhandled);
    }

    [Fact]
    public void 튜닝_폴더의_motions_json을_런타임_규칙으로_읽는다()
    {
        StageReducerTuning tuning = LoadLibrary().Tuning!;

        Assert.NotNull(tuning.Motions);
        Assert.Equal(MotionEnd.Restore, tuning.Motions["nod"].End);
        Assert.Equal(MotionEnd.Commit, tuning.Motions["bow"].End);
    }

    [Fact]
    public void commit_모션은_마지막_키를_남긴다()
    {
        StageReducerTuning tuning = LoadLibrary().Tuning!;
        MiniStageFoldLine line = new("ln_1", false, [Command("char_rig_staging.motion", ("slot", "c1"), ("motion", "bow"))]);

        CoreStageFoldResult result = CoreStageFold.Fold(Catalog, CastHarin, [line], tuning);

        Assert.DoesNotContain(result.CoreState!.Unhandled, item => item.Command.Name == "motion");

        // bow — CharacterPortrait_SwayPivot의 z가 마지막 키 8°로 남는다.
        RectNodeState pivot = result.CoreState.Nodes.GetState(StageState.NodeKeyOf("c1", "CharacterPortrait_SwayPivot"));
        Assert.Equal(8f, pivot.LocalEulerAngles.Z, 3);
    }

    [Fact]
    public void restore_모션은_무대를_바꾸지_않는다()
    {
        StageReducerTuning tuning = LoadLibrary().Tuning!;
        MiniStageFoldLine line = new("ln_1", false, [Command("char_rig_staging.motion", ("slot", "c1"), ("motion", "nod"))]);

        StageState before = CoreStageFold.Fold(Catalog, CastHarin, [], tuning).CoreState!;
        StageState after = CoreStageFold.Fold(Catalog, CastHarin, [line], tuning).CoreState!;

        Assert.DoesNotContain(after.Unhandled, item => item.Command.Name == "motion");

        foreach (string key in before.Nodes.Keys)
        {
            Assert.Equal(before.Nodes.GetState(key), after.Nodes.GetState(key));
        }
    }

    [Fact]
    public void 모션_읽기는_end_없는_것과_같은_이름_둘째를_버린다()
    {
        const string json = """
            {
              "schema": "motions/2",
              "motions": [
                { "name": "tilt", "rig": "character", "duration": 0.5, "end": "commit",
                  "channels": [ { "node": "CharacterPortrait_SwayPivot", "kind": "euler",
                    "z": [ { "t": 0, "v": 0 }, { "t": 0.5, "v": 4 } ] } ] },
                { "name": "tilt", "rig": "character", "duration": 0.5, "end": "commit",
                  "channels": [ { "node": "CharacterPortrait_SwayPivot", "kind": "euler",
                    "z": [ { "t": 0, "v": 0 }, { "t": 0.5, "v": 9 } ] } ] },
                { "name": "noend", "rig": "character", "duration": 0.5,
                  "channels": [ { "node": "CharacterPortrait_SwayPivot", "kind": "euler",
                    "z": [ { "t": 0, "v": 0 }, { "t": 0.5, "v": 1 } ] } ] }
              ]
            }
            """;
        var problems = new List<string>();

        IReadOnlyDictionary<string, MotionClip> clips = MotionLibraryJson.Parse(json, "test", null, problems);

        MotionClip tilt = Assert.Single(clips).Value;
        Assert.Equal(4f, tilt.Channels[0].Z[^1].Value, 3);
        Assert.Contains(problems, problem => problem.Contains("같은 이름"));
        Assert.Contains(problems, problem => problem.Contains("noend"));
    }

    [Fact]
    public void 모션_읽기는_리그에_없는_노드를_거른다()
    {
        const string json = """
            { "schema": "motions/2", "motions": [
              { "name": "ghost", "rig": "character", "duration": 0.5, "end": "commit",
                "channels": [ { "node": "NoSuchNode", "kind": "position",
                  "x": [ { "t": 0, "v": 0 }, { "t": 0.5, "v": 1 } ] } ] } ] }
            """;
        var problems = new List<string>();

        IReadOnlyDictionary<string, MotionClip> clips =
            MotionLibraryJson.Parse(json, "test", new HashSet<string> { "CharSlot_Track" }, problems);

        Assert.Empty(clips);
        Assert.Contains(problems, problem => problem.Contains("ghost"));
    }
}
