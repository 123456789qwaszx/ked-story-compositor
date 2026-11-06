using System.Text.Json.Nodes;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 2026-11-03 런타임이 정본만 남기며 걷은 별칭 정의를 읽을 때 정본으로 옮긴다.
/// 옮김은 재생 결과를 바꾸지 않아야 한다 — 옛 정의의 생략값이 정본과 다르면 값을 명시해 옮긴다.
/// </summary>
public class LegacyCommandUpgradeTests
{
    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static (string Id, Dictionary<string, string> Arguments) Upgrade(
        string definitionId, params (string Key, string Value)[] pairs)
    {
        Assert.True(LegacyCommandUpgrade.TryUpgrade(
            definitionId, Args(pairs), out string id, out Dictionary<string, string> arguments));
        return (id, arguments);
    }

    [Fact]
    public void 옮김표의_목적지는_카탈로그에_있고_인자_이름도_목적지의_것이다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        foreach (string legacy in LegacyCommandUpgrade.LegacyDefinitionIds)
        {
            Assert.Null(legacy == "common_control.pause" ? null : catalog.Find(legacy));

            LegacyCommandUpgrade.TryUpgrade(
                legacy, Args(("seconds", "0.3")), out string id, out Dictionary<string, string> arguments);

            PresentationCommandDefinition target = catalog.Find(id)
                ?? throw new Xunit.Sdk.XunitException($"{legacy} → {id}가 카탈로그에 없다");

            foreach (string key in arguments.Keys.Where(key => key is not "seconds"))
            {
                Assert.True(target.FindParameter(key) is not null, $"{legacy} → {id}: 인자 '{key}'가 목적지에 없다");
            }
        }
    }

    [Fact]
    public void place_tl은_place의_화면_지점이_되고_focus_생략값_face를_들고_간다()
    {
        (string id, Dictionary<string, string> args) = Upgrade("char_rig_placement.place_tl", ("slot", "c1"));
        Assert.Equal("char_rig_placement.place", id);
        Assert.Equal("tl", args["screenPoint"]);
        Assert.Equal("face", args["focus"]);

        (_, args) = Upgrade("char_rig_placement.place_inner_br", ("slot", "c1"), ("focus", "bust"), ("duration", "12fr"));
        Assert.Equal("inner_br", args["screenPoint"]);
        Assert.Equal("bust", args["focus"]);
        Assert.Equal("12fr", args["duration"]);
    }

    [Fact]
    public void 남긴_예외는_옮기지_않는다()
    {
        // 소유자 결정(2026-11-03): place_left · center · right, 프레임 대기, char_visual_<효과>는 정본이다.
        foreach (string id in new[]
                 {
                     "char_rig_placement.place_left", "control.frame_wait",
                     "char_rig_visual.char_visual_focus", "char_rig_visual.char_visual_clear"
                 })
        {
            Assert.False(LegacyCommandUpgrade.TryUpgrade(id, Args(("slot", "c1"), ("n", "12")), out _, out _), id);
            Assert.NotNull(PresentationCommandCatalog.Default.Find(id));
        }
    }

    [Fact]
    public void size_라벨은_인자가_된다()
    {
        (string id, Dictionary<string, string> args) = Upgrade("char_rig_depth.size_close", ("slot", "c1"));
        Assert.Equal("char_rig_depth.size", id);
        Assert.Equal("close", args["depth"]);
    }

    [Fact]
    public void tx는_프리셋_키로_옮기고_daze_strip은_무대_00을_들고_간다()
    {
        (string id, Dictionary<string, string> args) = Upgrade("transition.tx_daze_in");
        Assert.Equal("transition.tx", id);
        Assert.Equal("daze_close", args["preset"]);
        Assert.Equal("00", args["stage"]);

        (_, args) = Upgrade("transition.tx_iris_in", ("duration", "12fr"));
        Assert.Equal("iris_in", args["preset"]);
        Assert.False(args.ContainsKey("stage"), "정본 생략값(stage01)과 같다");
        Assert.Equal("12fr", args["duration"]);
    }

    [Fact]
    public void 무대를_이름에_박은_것은_stage_인자가_된다()
    {
        Assert.Equal("stage01", Upgrade("char_rig_cast.slot01", ("slotKey", "c1")).Arguments["stage"]);
        Assert.Equal("stage01", Upgrade("background.bg_slot01", ("rigKey", "bg"), ("spriteKey", "x")).Arguments["stage"]);
        Assert.Equal("stage02", Upgrade("screen_blur.screen_blur_s2", ("layer", "far")).Arguments["stage"]);
    }

    [Fact]
    public void char_to와_그_별칭은_옮기지_않는다_정본_없이_걷었다()
    {
        // 리그는 무대 · 층을 옮기지 않는다(런타임 2026-11-06). 다른 자리는 fade_out 뒤 새 slot이라 옮길 정본이 없다.
        foreach (string id in new[] { "char_rig_staging.char_to", "char_rig_staging.char_to_s1" })
        {
            Assert.False(LegacyCommandUpgrade.TryUpgrade(id, Args(("slot", "c1")), out _, out _), id);
            Assert.Null(PresentationCommandCatalog.Default.Find(id));
        }
    }

    [Fact]
    public void 옛_pause의_초_인자는_시간_토큰_인자가_된다()
    {
        (string id, Dictionary<string, string> args) = Upgrade("common_control.pause", ("seconds", "0.5"));
        Assert.Equal("common_control.pause", id);
        Assert.Equal("0.5", args["duration"]);

        Assert.False(LegacyCommandUpgrade.TryUpgrade(
            "common_control.pause", Args(("duration", "12fr")), out _, out _), "이미 정본이면 그대로");
    }

    [Fact]
    public void 칩과_최근_명령은_읽을_때_옮긴다()
    {
        var chips = new JsonArray(new JsonObject
        {
            ["name"] = "긴 암전",
            ["steps"] = new JsonArray(new JsonObject
            {
                ["command"] = "transition.tx_daze_in",
                ["args"] = new JsonObject { ["duration"] = "24fr" }
            })
        });

        StageQuickStep step = Assert.Single(Assert.Single(ProjectManifestJson.ReadQuickCommands(chips)!).Steps);
        Assert.Equal("transition.tx", step.DefinitionId);
        Assert.Equal("daze_close", step.Arguments["preset"]);
        Assert.Equal("24fr", step.Arguments["duration"]);

        IReadOnlyList<string> recent = ProjectManifestJson.ReadRecentCommands(new JsonArray(
            "char_rig_depth.size_close", "char_rig_depth.size_far", "char_rig_staging.move_by"));
        Assert.Equal(["char_rig_depth.size", "char_rig_staging.move_by"], recent);
    }

    [Fact]
    public void 얼린_발행본도_읽을_때_옮기고_해시를_다시_잰다()
    {
        var source = new DialogueResultReference("dlg", 1, "sha256:dialogue");
        PresentationResultCommand[] setup =
        [
            new("cmd1", "char_rig_cast.slot01", Args(("slotKey", "c1")))
        ];
        PresentationResultBinding[] bindings =
        [
            new("line1", [new PresentationResultCommand("cmd2", "char_rig_depth.size_close", Args(("slot", "c1")))], false)
        ];

        var draft = new PresentationResult(
            new ResultIdentity("pres", 1, PresentationResult.CurrentSchemaVersion, "sha256:placeholder"),
            "node", "노드", source, setup, bindings, DateTimeOffset.UnixEpoch);
        var published = new PresentationResult(
            draft.Identity with { ContentHash = ResultHash.Of(draft) },
            "node", "노드", source, setup, bindings, DateTimeOffset.UnixEpoch);
        Assert.True(ResultHash.IsIntact(published));

        PresentationResult reread = PresentationResultJson.Read(PresentationResultJson.Write(published));

        Assert.Equal("char_rig_cast.slot", reread.SetupCommands[0].DefinitionId);
        Assert.Equal("stage01", reread.SetupCommands[0].Arguments["stage"]);
        PresentationResultCommand size = Assert.Single(reread.Bindings[0].Commands);
        Assert.Equal("char_rig_depth.size", size.DefinitionId);
        Assert.Equal("close", size.Arguments["depth"]);

        Assert.True(ResultHash.IsIntact(reread), "옮긴 본문으로 해시를 다시 잰다");
        Assert.Equal(published.Identity.ResultId, reread.Identity.ResultId);
        Assert.Equal(published.Identity.Version, reread.Identity.Version);
    }

    [Fact]
    public void 무결하지_않던_발행본은_옮겨도_해시를_고치지_않는다()
    {
        var source = new DialogueResultReference("dlg", 1, "sha256:dialogue");
        var tampered = new PresentationResult(
            new ResultIdentity("pres", 1, PresentationResult.CurrentSchemaVersion, "sha256:not-the-body"),
            "node", "노드", source,
            [new PresentationResultCommand("cmd1", "char_rig_cast.slot01", Args(("slotKey", "c1")))],
            [], DateTimeOffset.UnixEpoch);

        PresentationResult reread = PresentationResultJson.Read(PresentationResultJson.Write(tampered));

        Assert.Equal("char_rig_cast.slot", reread.SetupCommands[0].DefinitionId);
        Assert.Equal("sha256:not-the-body", reread.Identity.ContentHash);
        Assert.False(ResultHash.IsIntact(reread));
    }
}
