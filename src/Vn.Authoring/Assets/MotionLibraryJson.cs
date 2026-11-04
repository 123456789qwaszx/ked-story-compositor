using System.Text.Json;
using System.Text.Json.Serialization;
using Ked.Presentation.Core;

namespace Vn.Authoring.Assets;

/// <summary>
/// <c>motions.json</c>(모션 저장소, <c>motions/2</c>)을 코어 <see cref="MotionClip"/>으로 읽는다.
///
/// 런타임 <c>MotionLibrary</c>(ked-presentation-runtime <c>Runtime/Commands/Parser/MotionLibrary.cs</c>)와
/// <b>같은 규칙</b>이다 — 다른 것은 JSON 읽개(Unity JsonUtility ↔ System.Text.Json)뿐이다:
/// <list type="bullet">
/// <item><c>end</c>는 restore | commit, 기본값 없음 — 빠지거나 다르면 그 모션을 버린다.</item>
/// <item><c>kind</c>는 position | scale | euler. 채널의 <c>mode</c>(motions/1)는 읽지 않는다 — 재생하는 커맨드가 정한다.</item>
/// <item>빈 성분 배열은 "그 성분 없음"(null)이다.</item>
/// <item>검증은 코어 <see cref="MotionRules.TryValidate"/> 한 곳 — 어긋난 모션은 사유를 남기고 버린다.</item>
/// <item>같은 이름이 둘이면 뒤의 것을 버린다.</item>
/// </list>
/// 읽은 클립은 <see cref="StageReducerTuning.Motions"/>에 그대로 들어가 리듀서의 <c>motion</c> 폴드가 쓴다 —
/// 재생과 프리뷰가 같은 클립을 본다.
/// </summary>
public static class MotionLibraryJson
{
    public const string FileName = "motions.json";
    public const string ExpectedSchema = "motions/2";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <param name="problems">버린 모션 · 스키마 경고가 사람이 읽는 문장으로 쌓인다.</param>
    public static IReadOnlyDictionary<string, MotionClip> Parse(
        string json,
        string sourceLabel,
        ICollection<string>? rigNodeIds,
        List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        FileDto? file;

        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException error)
        {
            problems.Add($"{sourceLabel}: JSON을 읽지 못했습니다 — {error.Message}. 모션 0개로 봅니다.");
            return new Dictionary<string, MotionClip>(StringComparer.Ordinal);
        }

        var clips = new Dictionary<string, MotionClip>(StringComparer.Ordinal);

        if (file is null)
        {
            problems.Add($"{sourceLabel}: 빈 문서입니다. 모션 0개로 봅니다.");
            return clips;
        }

        if (!string.Equals(file.Schema, ExpectedSchema, StringComparison.Ordinal))
        {
            problems.Add(
                $"{sourceLabel}: schema '{file.Schema}' (기대 '{ExpectedSchema}'). " +
                "읽기는 시도합니다 — 어긋나면 모션 단위 검증이 거릅니다.");
        }

        foreach (MotionDto? dto in file.Motions ?? new List<MotionDto?>())
        {
            if (!TryConvert(dto, sourceLabel, problems, out MotionClip? clip))
            {
                continue;
            }

            if (!MotionRules.TryValidate(clip!, rigNodeIds, out string why))
            {
                problems.Add($"{sourceLabel}: 모션 '{clip!.Name}' 무시 — {why}.");
                continue;
            }

            if (!clips.TryAdd(clip!.Name, clip))
            {
                problems.Add($"{sourceLabel}: 모션 '{clip.Name}' 무시 — 같은 이름이 둘이다 — 뒤의 것을 무시한다.");
            }
        }

        return clips;
    }

    private static bool TryConvert(MotionDto? dto, string sourceLabel, List<string> problems, out MotionClip? clip)
    {
        clip = null;

        if (dto is null)
        {
            problems.Add($"{sourceLabel}: 모션 '(null)' 무시 — 항목이 비어 있다.");
            return false;
        }

        string name = string.IsNullOrEmpty(dto.Name) ? "(이름 없음)" : dto.Name;

        if (!TryParseEnd(dto.End, out MotionEnd end))
        {
            problems.Add($"{sourceLabel}: 모션 '{name}' 무시 — end는 restore|commit 이어야 한다 ('{dto.End}') — 기본값은 없다.");
            return false;
        }

        List<ChannelDto?> source = dto.Channels ?? new List<ChannelDto?>();
        var channels = new MotionChannel[source.Count];

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelDto? c = source[i];

            if (c is null)
            {
                problems.Add($"{sourceLabel}: 모션 '{name}' 무시 — 채널 {i}가 비어 있다.");
                return false;
            }

            if (!TryParseKind(c.Kind, out MotionChannelKind kind))
            {
                problems.Add($"{sourceLabel}: 모션 '{name}' 무시 — 채널 {i}: kind는 position|scale|euler 여야 한다 ('{c.Kind}').");
                return false;
            }

            channels[i] = new MotionChannel
            {
                Node = c.Node ?? "",
                Kind = kind,
                X = ToKeys(c.X),
                Y = ToKeys(c.Y),
                Z = ToKeys(c.Z),
            };
        }

        clip = new MotionClip
        {
            Name = dto.Name ?? "",
            Rig = dto.Rig ?? "",
            Duration = dto.Duration,
            End = end,
            Channels = channels,
        };

        return true;
    }

    // 런타임과 같다 — 빈 배열은 "성분 없음"(null).
    private static CurveKey[]? ToKeys(List<KeyDto>? keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return null;
        }

        var result = new CurveKey[keys.Count];

        for (int i = 0; i < keys.Count; i++)
        {
            result[i] = new CurveKey(keys[i].T, keys[i].V, keys[i].InTangent, keys[i].OutTangent);
        }

        return result;
    }

    private static bool TryParseKind(string? token, out MotionChannelKind kind)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "position": kind = MotionChannelKind.Position; return true;
            case "scale": kind = MotionChannelKind.Scale; return true;
            case "euler": kind = MotionChannelKind.Euler; return true;
            default: kind = default; return false;
        }
    }

    private static bool TryParseEnd(string? token, out MotionEnd end)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "restore": end = MotionEnd.Restore; return true;
            case "commit": end = MotionEnd.Commit; return true;
            default: end = default; return false;
        }
    }

    // ── DTO — 필드명이 곧 스키마다(런타임 MotionLibrary의 DTO와 같은 이름) ──

    private sealed class FileDto
    {
        [JsonPropertyName("schema")] public string? Schema { get; set; }
        [JsonPropertyName("motions")] public List<MotionDto?>? Motions { get; set; }
    }

    private sealed class MotionDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("rig")] public string? Rig { get; set; }
        [JsonPropertyName("duration")] public float Duration { get; set; }
        [JsonPropertyName("end")] public string? End { get; set; }
        [JsonPropertyName("channels")] public List<ChannelDto?>? Channels { get; set; }
    }

    private sealed class ChannelDto
    {
        [JsonPropertyName("node")] public string? Node { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("x")] public List<KeyDto>? X { get; set; }
        [JsonPropertyName("y")] public List<KeyDto>? Y { get; set; }
        [JsonPropertyName("z")] public List<KeyDto>? Z { get; set; }
    }

    private sealed class KeyDto
    {
        [JsonPropertyName("t")] public float T { get; set; }
        [JsonPropertyName("v")] public float V { get; set; }
        [JsonPropertyName("inTangent")] public float InTangent { get; set; }
        [JsonPropertyName("outTangent")] public float OutTangent { get; set; }
    }
}
