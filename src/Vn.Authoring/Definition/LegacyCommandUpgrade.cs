namespace Vn.Authoring.Definition;

/// <summary>
/// 걷은 별칭 정의 → 정본 정의 (2026-11-03).
///
/// 런타임 어휘에서 "인자 하나를 이름에 박아 다시 부르던" 이름(slotNN · bg_slotNN · screen_blur_sN ·
/// place_* · size_&lt;라벨&gt; · tx_&lt;프리셋&gt;)을 걷고 정본만 남겼다. 카탈로그에도 정본만 있다.
/// 예외(소유자 결정)는 걷지 않았으므로 옮기지 않는다: place_left · place_center · place_right,
/// 프레임 대기 &lt;&lt;Nfr&gt;&gt;(control.frame_wait), char_visual_&lt;효과&gt; 일곱(캐릭터 비주얼은 이름에서 효과가 드러나야 한다).
///
/// 정본 없이 걷은 커맨드는 옮기지 않는다(ui_patch · debug_log · box_* · char_to와 그 별칭 char_to_sN · gesture).
/// char_to는 2026-11-06에 걷었다 — 리그는 무대 · 층을 옮기지 않고, 다른 자리는 fade_out 뒤 새 slot이다.
/// gesture는 2026-11-07에 걷었다 — 몸짓은 모션 툴이 만든 motion으로 한다. 남은 줄은 모르는 명령으로 드러난다.
///
/// 이 툴의 데이터는 명령을 <b>정의 id + 이름 인자</b>로 저장한다. 걷은 정의를 가리키는 저장본이 있으면
/// <b>읽을 때</b> 여기서 정본으로 옮긴다 — 작업 데이터(연출 노드 · 프리셋 · 칩 · 최근 명령)도, 얼린 발행본도.
/// 여기서만 옮기므로 무대 · 프리뷰 · 폴드 · 이미터는 옛 이름을 모른다.
///
/// <b>옮김은 재생 결과를 바꾸지 않는다.</b> 옛 정의의 생략값이 정본과 다른 자리는 값을 명시해 옮긴다
/// (place_*의 focus "face" · tx_daze/strip의 무대).
/// 이름에 박혀 있던 값은 고정값으로 덮는다 — 옛 정의에는 그 인자가 없었으니 덮을 것도 없다.
///
/// 예외 하나: 커스텀 곡선 참조(ease 인자의 <c>@이름</c>)는 2026-11-07에 걷었다 — 런타임이 곡선 파일을 더 읽지 않는다.
/// 값을 빼서 기본 이징으로 둔다. 재생이 바뀌는 유일한 옮김이라, 열 때 몇 곳인지
/// <see cref="Serialization.CompositionLoadResult.RetiredEaseReferences"/>로 알린다.
/// </summary>
public static class LegacyCommandUpgrade
{
    /// <param name="Fixed">언제나 이 값으로 둔다 — 옛 이름에 박혀 있던 것.</param>
    /// <param name="Defaults">비어 있을 때만 채운다 — 옛 정의의 생략값이 정본과 다른 자리.</param>
    /// <param name="Renames">옛 인자 이름 → 정본 인자 이름.</param>
    private sealed record Rule(
        string TargetId,
        IReadOnlyDictionary<string, string>? Fixed = null,
        IReadOnlyDictionary<string, string>? Defaults = null,
        IReadOnlyDictionary<string, string>? Renames = null);

    private static readonly IReadOnlyDictionary<string, Rule> Rules = BuildRules();

    /// <summary>카탈로그에서 이징 타입 인자는 모두 이 이름이다.</summary>
    private const string EaseParameter = "ease";

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

        var result = new Dictionary<string, string>(arguments, StringComparer.Ordinal);
        bool retiredCurve = result.TryGetValue(EaseParameter, out string? ease) &&
                            ease.StartsWith('@') &&
                            result.Remove(EaseParameter);

        if (!Rules.TryGetValue(definitionId, out Rule? rule))
        {
            if (!retiredCurve)
            {
                return false;
            }

            upgradedArguments = result;
            return true;
        }

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
