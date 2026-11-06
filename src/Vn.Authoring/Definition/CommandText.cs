using System.Globalization;
using System.Text;

namespace Vn.Authoring.Definition;

/// <summary>커맨드 텍스트의 인자 하나 — 카탈로그 파라미터 순서를 이미 지킨 값이다.</summary>
public sealed record OrderedCommandArgument(string Name, string Value);

/// <summary>
/// 텍스트 입력 파싱 결과. 실패면 <see cref="Error"/>에 이유가 있다 —
/// 비슷한 이름을 추측해 보정하지 않는다(원칙 §2.3). 무엇이 틀렸는지만 정확히 말한다.
/// </summary>
public sealed record CommandTextParseResult(
    PresentationCommandDefinition? Definition,
    IReadOnlyDictionary<string, string>? Arguments,
    string? Error)
{
    public bool Success => Definition is not null && Error is null;
}

/// <summary>
/// <c>&lt;&lt;이름 인자…&gt;&gt;</c> 텍스트와 구조화 커맨드 사이의 변환 규칙 단일 구현.
///
/// 조립(<see cref="ResolveOrdered"/>)은 이미터·Preview가 쓰던 규칙 그대로다 —
/// 카탈로그 파라미터 순서, 트레일링 기본값 생략, 정의 밖 인자는 이름순으로 뒤에.
/// 파싱은 그 역방향 <b>입력 방법</b>이다(산출물 되읽기가 아니므로 역파싱 금지 원칙과
/// 무관). 검증 기준도 같은 카탈로그 하나다.
/// </summary>
public static class CommandText
{
    /// <summary>
    /// 빈 자리를 메우는 자리표. 뒤 인자가 값을 가졌는데 앞이 비면 <b>자리가 밀리므로</b>
    /// 반드시 무언가를 적어야 한다 — Yarn은 이것을 빈 문자열로 넘기고, 브리지의
    /// <c>IsNullOrWhiteSpace</c> 폴백이 "안 적은 것"과 같게 받는다.
    /// </summary>
    public const string EmptyArgumentPlaceholder = "\"\"";

    /// <summary>
    /// 출력 이름의 자리표 — <b>첫 인자가 이름 안으로 들어간다</b> (프레임 대기 <c>&lt;N&gt;fr</c> → <c>&lt;&lt;24fr&gt;&gt;</c>).
    ///
    /// 런타임에는 <c>1fr</c>~<c>48fr</c>이 각각 인자 없는 이름으로 등록돼 있다(2026-11-03 되살림 — "엄청 유용하고 자주 쓴다").
    /// 카탈로그는 그 마흔여덟 개를 정의 하나로 묶었으므로, 이름을 짓는 자리(<see cref="ResolveInvocation"/>)와
    /// 이름을 읽는 자리(<see cref="Parse"/>)가 이 규칙을 한 벌로 든다.
    /// </summary>
    public const string NameTemplateToken = "<N>";

    /// <summary>
    /// 자리표 이름이 런타임에 실제로 있는 이름이 되는가. 값은 첫 인자의 슬라이더 범위 안의 정수여야 한다
    /// (<c>&lt;N&gt;fr</c>은 1~48 — 런타임 등록이 거기까지다). 아니면 이유, 맞으면 null.
    /// 자리표 이름이 아닌 정의에는 언제나 null이다.
    /// </summary>
    public static string? ValidateNameTemplate(
        PresentationCommandDefinition? definition,
        IReadOnlyDictionary<string, string> arguments)
    {
        if (!IsNameTemplate(definition))
        {
            return null;
        }

        PresentationCommandParameter head = definition!.Parameters[0];

        if (!arguments.TryGetValue(head.Name, out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            return $"'{definition.OutputCommandName}'의 '{head.Name}'이 비어 있습니다 — 이름을 지을 수 없습니다.";
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return $"'{definition.OutputCommandName}'의 '{head.Name}'은 정수여야 하는데 '{raw}'가 왔습니다.";
        }

        if (head.Slider is { } range && (value < range.Minimum || value > range.Maximum))
        {
            return $"'{definition.OutputCommandName.Replace(NameTemplateToken, raw.Trim(), StringComparison.Ordinal)}'은 " +
                   $"런타임에 없는 이름입니다 — {range.Minimum}~{range.Maximum}만 됩니다. 더 길게 기다리려면 pause를 쓰세요.";
        }

        return null;
    }

    /// <summary>자리표가 든 출력 이름인가.</summary>
    public static bool IsNameTemplate(PresentationCommandDefinition? definition) =>
        definition is { Parameters.Count: > 0 } &&
        definition.OutputCommandName.Contains(NameTemplateToken, StringComparison.Ordinal);

    /// <summary>
    /// 런타임에 나가는 <b>이름과 인자</b>. 보통은 출력 이름 그대로 + <see cref="ResolveOrdered"/>이고,
    /// 자리표 이름이면 첫 인자를 이름에 넣고 인자 목록에서 뺀다. Yarn 텍스트 · 무대 폴드 · 문서가 모두 이것을 쓴다 —
    /// 셋이 갈리면 툴이 접은 무대와 게임이 읽는 대본이 다른 커맨드가 된다.
    /// </summary>
    public static (string Name, IReadOnlyList<OrderedCommandArgument> Arguments) ResolveInvocation(
        PresentationCommandDefinition? definition,
        string fallbackName,
        IReadOnlyDictionary<string, string> arguments)
    {
        IReadOnlyList<OrderedCommandArgument> ordered = ResolveOrdered(definition, arguments);

        if (!IsNameTemplate(definition))
        {
            return (definition?.OutputCommandName ?? fallbackName, ordered);
        }

        string head = definition!.Parameters[0].Name;
        OrderedCommandArgument? filled = ordered.FirstOrDefault(argument =>
            string.Equals(argument.Name, head, StringComparison.Ordinal));

        // 값이 없으면 자리표를 그대로 둔다 — 내보내기 검증이 막고(필수 인자), 무대는 모르는 이름으로 남긴다.
        string value = filled is null || filled.Value == EmptyArgumentPlaceholder ? NameTemplateToken : filled.Value;

        return (
            definition.OutputCommandName.Replace(NameTemplateToken, value, StringComparison.Ordinal),
            ordered.Where(argument => !ReferenceEquals(argument, filled)).ToArray());
    }

    /// <summary>
    /// 카탈로그의 파라미터 순서대로 인자 값을 해석한다. 작성 값이 없으면 정의의 기본값을 쓰고,
    /// <b>값이 있는 마지막 파라미터 뒤부터</b> 트레일링 생략으로 자른다.
    /// 정의를 모르는 명령이나 파라미터 밖의 인자는 버리지 않고 이름순으로 뒤에 붙인다.
    ///
    /// <b>중간이 비면 자리표를 적는다.</b> 값 없는 파라미터에서 끊으면, 뒤에 값이 있을 때 그 인자가
    /// <b>앞 자리로 밀려</b> 런타임이 다른 것을 읽는다 — <c>slide_in</c>에 punch만 적으면 그 값이 ease 자리로 간다.
    /// 선택 인자가 맨 뒤 하나뿐인 커맨드에서는 드러나지 않는다.
    /// </summary>
    public static IReadOnlyList<OrderedCommandArgument> ResolveOrdered(
        PresentationCommandDefinition? definition,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var ordered = new List<OrderedCommandArgument>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        if (definition is not null)
        {
            // 값(작성값 또는 기본값)이 있는 마지막 자리까지만 낸다 — 그 뒤는 트레일링 생략.
            var values = definition.Parameters
                .Select(parameter => arguments.TryGetValue(parameter.Name, out string? provided)
                    ? provided
                    : parameter.Default)
                .ToList();

            int last = values.FindLastIndex(value => value is not null);

            for (int index = 0; index <= last; index++)
            {
                PresentationCommandParameter parameter = definition.Parameters[index];

                ordered.Add(new OrderedCommandArgument(
                    parameter.Name, values[index] ?? EmptyArgumentPlaceholder));
                consumed.Add(parameter.Name);
            }
        }

        foreach ((string key, string value) in arguments
                     .Where(pair => !consumed.Contains(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ordered.Add(new OrderedCommandArgument(key, value));
        }

        return ordered;
    }

    /// <summary>어떤 방식으로 만들었든 항상 병기되는 <c>&lt;&lt;…&gt;&gt;</c> 텍스트.</summary>
    public static string Format(
        PresentationCommandDefinition? definition,
        string fallbackName,
        IReadOnlyDictionary<string, string> arguments)
    {
        (string name, IReadOnlyList<OrderedCommandArgument> ordered) =
            ResolveInvocation(definition, fallbackName, arguments);

        var builder = new StringBuilder("<<");
        builder.Append(name);

        foreach (OrderedCommandArgument argument in ordered)
        {
            builder.Append(' ').Append(argument.Value);
        }

        return builder.Append(">>").ToString();
    }

    /// <summary>
    /// 한 줄 텍스트를 카탈로그 기준으로 파싱한다. 꺾쇠는 있어도 없어도 된다.
    ///
    /// 커맨드명은 outputCommand 정확 일치(같은 이름이 여럿이면 카탈로그 첫 정의 —
    /// 결정적), 그다음 정의 Id 정확 일치만 본다. 인자는 포지셔널로 파라미터에 붙이고,
    /// 초과·필수 누락·숫자 타입 불일치는 즉시 오류다. 추측 보정은 하지 않는다.
    /// </summary>
    public static CommandTextParseResult Parse(string? input, PresentationCommandCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        string text = (input ?? string.Empty).Trim();

        if (text.StartsWith("<<", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        if (text.EndsWith(">>", StringComparison.Ordinal))
        {
            text = text[..^2];
        }

        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return new CommandTextParseResult(null, null, "커맨드 이름이 없습니다.");
        }

        string name = tokens[0];
        PresentationCommandDefinition? definition =
            catalog.FindByOutputCommand(name) ?? catalog.Find(name);

        // 자리표 이름(<N>fr)은 이름 안의 값을 첫 인자로 돌린다 — 24fr → (frame_wait, n=24).
        if (definition is null && TryMatchNameTemplate(name, catalog, out PresentationCommandDefinition? template, out string? headValue))
        {
            definition = template;
            tokens = [name, headValue!, .. tokens[1..]];
        }

        if (definition is null)
        {
            return new CommandTextParseResult(
                null, null, $"카탈로그에 없는 커맨드입니다: '{name}'");
        }

        string[] values = tokens[1..];

        if (values.Length > definition.Parameters.Count)
        {
            return new CommandTextParseResult(
                definition,
                null,
                $"'{definition.OutputCommandName}'의 인자는 최대 {definition.Parameters.Count}개인데 " +
                $"{values.Length}개가 왔습니다.");
        }

        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < definition.Parameters.Count; index++)
        {
            PresentationCommandParameter parameter = definition.Parameters[index];

            // 자리표("")는 안 적은 자리다 — 검증도 저장도 하지 않는다(필수면 빠진 것과 같다).
            if (index < values.Length && values[index] != EmptyArgumentPlaceholder)
            {
                if (ValidateType(parameter, values[index]) is { } typeError)
                {
                    return new CommandTextParseResult(definition, null, typeError);
                }

                arguments[parameter.Name] = values[index];
            }
            else if (parameter.Required && parameter.Default is null)
            {
                return new CommandTextParseResult(
                    definition,
                    null,
                    $"필수 인자 '{parameter.Name}'이 없습니다.");
            }
        }

        if (ValidateNameTemplate(definition, arguments) is { } templateError)
        {
            return new CommandTextParseResult(definition, null, templateError);
        }

        return new CommandTextParseResult(definition, arguments, null);
    }

    /// <summary>
    /// 이름이 어느 자리표 정의의 모양(<c>앞 · 값 · 뒤</c>)에 맞는가. 값 자리는 비어 있지 않아야 하고 공백이 없다 —
    /// 값의 타입은 <see cref="ValidateType"/>이 첫 인자의 타입으로 본다.
    /// </summary>
    private static bool TryMatchNameTemplate(
        string name,
        PresentationCommandCatalog catalog,
        out PresentationCommandDefinition? definition,
        out string? value)
    {
        foreach (PresentationCommandDefinition candidate in catalog.Definitions)
        {
            if (!IsNameTemplate(candidate))
            {
                continue;
            }

            int at = candidate.OutputCommandName.IndexOf(NameTemplateToken, StringComparison.Ordinal);
            string prefix = candidate.OutputCommandName[..at];
            string suffix = candidate.OutputCommandName[(at + NameTemplateToken.Length)..];

            if (name.Length > prefix.Length + suffix.Length &&
                name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(suffix, StringComparison.Ordinal))
            {
                definition = candidate;
                value = name[prefix.Length..^suffix.Length];
                return true;
            }
        }

        definition = null;
        value = null;
        return false;
    }

    /// <summary>
    /// 숫자 타입과 ease만 검증한다. 그 외 토큰 타입(slot·duration 등)의 어휘는 런타임
    /// 파서의 몫이다. ease를 여기서 잡는 이유: 런타임은 모르는 이름을 로그만 남기고
    /// OutCubic으로 조용히 굴러가므로(ease-open-orders §4), 오타를 저작에서 미리 짚는
    /// 것이 유일한 방어다 — 판정 규칙은 런타임 YarnEaseParser와 같다(대소문자 무시,
    /// 숫자 토큰 거부).
    /// </summary>
    private static string? ValidateType(PresentationCommandParameter parameter, string value)
    {
        return parameter.Type switch
        {
            "int" when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) =>
                $"'{parameter.Name}'은 정수여야 하는데 '{value}'가 왔습니다.",
            "float" or "seconds" when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) =>
                $"'{parameter.Name}'은 숫자여야 하는데 '{value}'가 왔습니다.",
            "bool" when !bool.TryParse(value, out _) && value is not ("0" or "1") =>
                $"'{parameter.Name}'은 true/false여야 하는데 '{value}'가 왔습니다.",
            "ease" when long.TryParse(value, out _) ||
                        !Enum.TryParse<Ked.Presentation.Core.EaseKind>(value, ignoreCase: true, out _) =>
                $"'{parameter.Name}'은 이징 이름이어야 하는데 '{value}'가 왔습니다 (예: OutCubic, Linear, InOutSine).",
            _ => null
        };
    }
}
