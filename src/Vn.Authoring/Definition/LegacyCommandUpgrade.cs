namespace Vn.Authoring.Definition;

/// <summary>
/// 걷은 별칭 정의 → 정본 정의 (2026-11-03).
///
/// 런타임 어휘에서 "인자 하나를 이름에 박아 다시 부르던" 이름(slotNN · char_to_sN · bg_slotNN · screen_blur_sN ·
/// place_* · size_&lt;라벨&gt; · char_visual_&lt;프리셋&gt; · tx_&lt;프리셋&gt; · &lt;&lt;Nfr&gt;&gt;)을 걷고 정본만 남겼다.
/// 예외는 place_left · place_center · place_right 셋이다(소유자 결정). 카탈로그에도 정본만 있다.
///
/// 이 툴의 데이터는 명령을 <b>정의 id + 이름 인자</b>로 저장한다. 걷은 정의를 가리키는 저장본이 있으면
/// <b>읽을 때</b> 여기서 정본으로 옮긴다 — 작업 데이터(연출 노드 · 프리셋 · 칩 · 최근 명령)도, 얼린 발행본도.
/// 여기서만 옮기므로 무대 · 프리뷰 · 폴드 · 이미터는 옛 이름을 모른다.
///
/// <b>옮김은 재생 결과를 바꾸지 않는다.</b> 옛 정의의 생략값이 정본과 다른 자리는 값을 명시해 옮긴다
/// (place_*의 focus "face" · char_visual_focus/defocus의 시간 · tx_daze/strip의 무대).
/// 이름에 박혀 있던 값은 고정값으로 덮는다 — 옛 정의에는 그 인자가 없었으니 덮을 것도 없다.
/// </summary>
public static class LegacyCommandUpgrade
{
    /// <param name="Fixed">언제나 이 값으로 둔다 — 옛 이름에 박혀 있던 것.</param>
    /// <param name="Defaults">비어 있을 때만 채운다 — 옛 정의의 생략값이 정본과 다른 자리.</param>
    /// <param name="Renames">옛 인자 이름 → 정본 인자 이름.</param>
    /// <param name="Transform">이름 · 값 둘 다 바뀌는 특수한 경우(&lt;&lt;Nfr&gt;&gt;의 n → pause의 duration).</param>
    private sealed record Rule(
        string TargetId,
        IReadOnlyDictionary<string, string>? Fixed = null,
        IReadOnlyDictionary<string, string>? Defaults = null,
        IReadOnlyDictionary<string, string>? Renames = null,
        Func<IReadOnlyDictionary<string, string>, Dictionary<string, string>>? Transform = null);

    private static readonly IReadOnlyDictionary<string, Rule> Rules = BuildRules();

    private static Dictionary<string, Rule> BuildRules()
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);

        static Dictionary<string, string> One(string key, string value) =>
            new(StringComparer.Ordinal) { [key] = value };

        foreach (string stage in new[] { "00", "01", "02" })
        {
            rules[$"char_rig_cast.slot{stage}"] = new Rule("char_rig_cast.slot", Fixed: One("stage", $"stage{stage}"));
            rules[$"background.bg_slot{stage}"] = new Rule("background.bg_spawn", Fixed: One("stage", $"stage{stage}"));
        }

        for (int stage = 0; stage <= 2; stage++)
        {
            rules[$"char_rig_staging.char_to_s{stage}"] =
                new Rule("char_rig_staging.char_to", Fixed: One("stage", $"stage0{stage}"));
        }

        rules["screen_blur.screen_blur_s1"] = new Rule("screen_blur.screen_blur", Fixed: One("stage", "stage01"));
        rules["screen_blur.screen_blur_s2"] = new Rule("screen_blur.screen_blur", Fixed: One("stage", "stage02"));

        // place_*의 focus 생략값은 "face", 정본 place는 "bust"다.
        foreach (string point in new[]
                 {
                     "tl", "top", "tr", "bl", "bottom", "br",
                     "inner_tl", "inner_tr", "inner_bl", "inner_br"
                 })
        {
            rules[$"char_rig_placement.place_{point}"] = new Rule(
                "char_rig_placement.place",
                Fixed: One("screenPoint", point),
                Defaults: One("focus", "face"));
        }

        foreach (string label in new[] { "far", "back", "mid", "front", "close" })
        {
            rules[$"char_rig_depth.size_{label}"] = new Rule("char_rig_depth.size", Fixed: One("depth", label));
        }

        // 정본 char_visual의 시간 생략값은 6fr. focus(10fr) · defocus(17fr)만 달랐다.
        foreach (string preset in new[] { "focus", "defocus", "dim", "silhouette", "inner_rim", "outer_rim", "clear" })
        {
            string? oldDuration = preset switch
            {
                "focus" => "10fr",
                "defocus" => "17fr",
                _ => null
            };

            rules[$"char_rig_visual.char_visual_{preset}"] = new Rule(
                "char_rig_visual.char_visual",
                Fixed: One("preset", preset),
                Defaults: oldDuration is null ? null : One("duration", oldDuration));
        }

        // 마스크 트랜지션 — 옛 이름 → 프리셋 키. 정본 tx의 무대 생략값은 stage01, daze · strip은 00이었다.
        foreach ((string name, string preset) in new[]
                 {
                     ("slant_in", "slant_in"), ("slant_out", "slant_out"),
                     ("hstrip_open", "hstrip_open"), ("hstrip_close", "hstrip_close"),
                     ("hstrip_in", "hstrip_in"), ("hstrip_out", "hstrip_out"),
                     ("vstrip_open", "vstrip_open"), ("vstrip_close", "vstrip_close"),
                     ("vstrip_in", "vstrip_in"), ("vstrip_out", "vstrip_out"),
                     ("band_in", "band_in"), ("band_out", "band_out"),
                     ("iris_in", "iris_in"), ("iris_out", "iris_out"),
                     ("daze_in", "daze_close"), ("daze_out", "daze_open"),
                     ("strip_in", "strip_cover"), ("strip_out", "strip_clear")
                 })
        {
            bool stage00 = name.StartsWith("daze", StringComparison.Ordinal) ||
                           name.StartsWith("strip", StringComparison.Ordinal);

            rules[$"transition.tx_{name}"] = new Rule(
                "transition.tx",
                Fixed: One("preset", preset),
                Defaults: stage00 ? One("stage", "00") : null);
        }

        // pause의 인자는 초 숫자(seconds)에서 시간 토큰(duration)이 됐다. 맨 숫자는 그대로 초로 읽힌다.
        rules["common_control.pause"] = new Rule(
            "common_control.pause",
            Renames: One("seconds", "duration"));

        rules["control.frame_wait"] = new Rule(
            "common_control.pause",
            Transform: arguments =>
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach ((string key, string value) in arguments)
                {
                    if (string.Equals(key, "n", StringComparison.Ordinal))
                    {
                        result["duration"] = string.IsNullOrWhiteSpace(value) ? value : value.Trim() + "fr";
                    }
                    else
                    {
                        result[key] = value;
                    }
                }

                return result;
            });

        return rules;
    }

    /// <summary>
    /// 옛 정의를 가리키면 정본 정의와 인자를 돌려준다. 옮길 것이 없으면 false — 그때 out 값은 입력 그대로다.
    /// </summary>
    public static bool TryUpgrade(
        string definitionId,
        IReadOnlyDictionary<string, string> arguments,
        out string upgradedId,
        out Dictionary<string, string> upgradedArguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        upgradedId = definitionId;
        upgradedArguments = new Dictionary<string, string>(arguments, StringComparer.Ordinal);

        if (!Rules.TryGetValue(definitionId, out Rule? rule))
        {
            return false;
        }

        Dictionary<string, string> result = rule.Transform is { } transform
            ? transform(arguments)
            : new Dictionary<string, string>(arguments, StringComparer.Ordinal);

        if (rule.Renames is { } renames)
        {
            foreach ((string from, string to) in renames)
            {
                if (result.Remove(from, out string? value))
                {
                    result.TryAdd(to, value);
                }
            }
        }

        if (rule.Defaults is { } defaults)
        {
            foreach ((string key, string value) in defaults)
            {
                if (!result.TryGetValue(key, out string? current) || string.IsNullOrWhiteSpace(current))
                {
                    result[key] = value;
                }
            }
        }

        if (rule.Fixed is { } fixedValues)
        {
            foreach ((string key, string value) in fixedValues)
            {
                result[key] = value;
            }
        }

        bool changed =
            !string.Equals(rule.TargetId, definitionId, StringComparison.Ordinal) ||
            result.Count != arguments.Count ||
            result.Any(pair =>
                !arguments.TryGetValue(pair.Key, out string? before) ||
                !string.Equals(before, pair.Value, StringComparison.Ordinal));

        if (!changed)
        {
            return false;
        }

        upgradedId = rule.TargetId;
        upgradedArguments = result;
        return true;
    }

    /// <summary>옮김 표가 아는 옛 정의 id 전부 — 표의 목적지가 카탈로그에 있는지 테스트가 대조한다.</summary>
    internal static IEnumerable<string> LegacyDefinitionIds => Rules.Keys;

    /// <summary>인자 없이 정의 id만 옮긴다 — 최근 명령 목록처럼 id만 저장하는 자리.</summary>
    public static string UpgradeId(string definitionId) =>
        Rules.TryGetValue(definitionId, out Rule? rule) ? rule.TargetId : definitionId;
}
