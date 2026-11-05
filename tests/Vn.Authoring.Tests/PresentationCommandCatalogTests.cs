using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

public class PresentationCommandCatalogTests
{
    [Fact]
    public void 기본_카탈로그는_내장된_런타임_카탈로그_데이터다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(GameDefinition.Empty);

        // docs/game.definition.json — 런타임 등록 테이블과 교차 검증한다.
        // 실제 대조는 아래 `카탈로그_어휘는_런타임_등록_목록과_일치한다`가 진다.
        //
        // 89 = 커맨드 77(카테고리 15) + 줄 태그 12(범주 line_tag — #auto · #box 전환 다섯 · 대사창 종류 여섯).
        // 커맨드는 런타임 정본만 둔다 — 인자 하나를 이름에 박은 별칭은 없고, 프레임 대기 <N>fr과
        // char_visual_<효과> 일곱만 별칭으로 남는다.
        // 줄 태그는 커맨드가 아니라 대사 줄의 태그다(execution "lineTag") — 대사창 숨김·전환은
        // 커맨드가 아니라 줄 태그(#boxhide 등)로 연출 층이 든다.
        Assert.Equal(89, catalog.Definitions.Count);
        Assert.Equal(77, catalog.Definitions.Count(item => !item.IsLineTag));
        Assert.Equal(16, catalog.Categories.Count);
        Assert.Same(catalog, PresentationCommandCatalog.For(definition: null));
    }

    /// <summary>
    /// 카탈로그 어휘 = 런타임 등록 어휘. 이 둘이 갈리면 작가가 고르는 순간 unknown command이거나
    /// (반대로) 쓸 수 있는 커맨드가 팔레트에 없다. 실측 목록은 픽스처가 지고, 갱신 방법은
    /// 그 파일 머리에 적혀 있다 — 런타임이 커맨드를 늘리거나 줄이면 이 테스트가 먼저 운다.
    /// </summary>
    [Fact]
    public void 카탈로그_어휘는_런타임_등록_목록과_일치한다()
    {
        HashSet<string> runtime = RuntimeCommandFixture.Load();

        // 줄 태그(execution "lineTag")는 커맨드가 아니라 대사 줄의 태그다 — 런타임 등록 목록이 아니라
        // 줄 태그 표와 대조한다(아래 카탈로그의_줄_태그는_런타임이_읽는_태그다).
        HashSet<string> catalog = PresentationCommandCatalog.Default.Definitions
            .Where(item => !item.IsLineTag)
            .Select(item => item.OutputCommandName)
            .ToHashSet(StringComparer.Ordinal);

        // 예외는 툴 쪽에 하나다. 늘리려면 사유를 여기 적어야 한다.
        //   <N>fr  — 1fr~48fr 동적 별칭군을 묶은 합성 항목이라 등록 이름과 글자가 다르다.
        string[] toolOnly = ["<N>fr"];

        Assert.Equal(
            Array.Empty<string>(),
            catalog.Except(runtime).Except(toolOnly).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            Array.Empty<string>(),
            runtime.Except(catalog).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void 카탈로그의_줄_태그는_런타임이_읽는_태그다()
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "runtime-line-tags.txt"));

        HashSet<string> runtime = File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        PresentationCommandDefinition[] tags = PresentationCommandCatalog.Default.Definitions
            .Where(item => item.IsLineTag)
            .ToArray();

        Assert.NotEmpty(tags);

        Assert.All(tags, tag =>
        {
            Assert.Equal("line_tag", tag.CategoryId);
            Assert.StartsWith("#", tag.OutputCommandName);
            Assert.Empty(tag.Parameters);
            Assert.Contains(tag.OutputCommandName[1..], runtime);
        });

        // 런타임 예제가 쓰는 셋은 반드시 있다.
        Assert.Contains(tags, tag => tag.OutputCommandName == "#boxhide");
        Assert.Contains(tags, tag => tag.OutputCommandName == "#auto");
        Assert.Contains(tags, tag => tag.OutputCommandName == "#letterbox");
    }

    [Fact]
    public void 기본_카탈로그의_커맨드는_순서_있는_파라미터를_가진다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        // 인자 하나짜리 표본.
        PresentationCommandDefinition front = catalog.Find("char_rig_staging.sibling_front")!;
        Assert.Equal("sibling_front", front.OutputCommandName);
        Assert.Equal("char_rig_staging", front.CategoryId);
        PresentationCommandParameter slot = Assert.Single(front.Parameters);
        Assert.Equal("slot", slot.Name);
        Assert.True(slot.Required);

        // 기본값은 파라미터에 실려 온다. 기본값 없는 필수 인자뿐이면 빈 사전이다.
        Assert.Empty(front.DefaultArgumentValues());

        // 기본값이 실려 오는 예.
        PresentationCommandDefinition pause = catalog.Find("common_control.pause")!;
        Assert.Equal("0.18", pause.DefaultArgumentValues()["duration"]);
    }

    [Fact]
    public void 메인_레인_전용_표시는_이제_아무도_달고_있지_않다()
    {
        // 메인 레인 전용 표시를 단 커맨드는 없고, 플래그를 읽는 쪽도 없다 — 레인이 하나뿐이라
        // 가릴 대상이 없다. **읽는 쪽도 다는 쪽도 없다.**
        // 스키마에서 지우는 것은 레인이 정말 안 돌아온다고 정해질 때 한다(그때 이 테스트도 간다).
        string[] mainLaneOnly = PresentationCommandCatalog.Default.Definitions
            .Where(item => item.MainLaneOnly)
            .Select(item => item.OutputCommandName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(mainLaneOnly);
    }

    [Fact]
    public void 모션_선언은_move_by에만_있고_선언_없는_커맨드는_null이다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        // "이 커맨드가 무슨 축을 미는가"의 유일한 근거. 이름·정규식 추측 금지.
        PresentationMotionDeclaration motion = catalog.Find("char_rig_staging.move_by")!.Motion!;
        Assert.Equal("CharSlot_Track", motion.NodeId);
        Assert.True(motion.Relative);
        Assert.Equal("duration", motion.DurationParameterName);
        Assert.Equal("ease", motion.EaseParameterName); // 런타임 다섯째 인자
        Assert.Equal("OutCubic", motion.DefaultEase); // 런타임 스펙 기본값의 기록
        Assert.Equal("x", motion.FindAxis("x")!.ParameterName);
        Assert.Equal("y", motion.FindAxis("y")!.ParameterName);
        Assert.Equal("u", motion.FindAxis("x")!.Unit);

        // ease 파라미터는 다섯째 자리이고 기본값이 없다 — 미지정이면 다섯째 토큰이
        // 통째로 생략돼(트레일링 생략 규칙) 기존 대본이 한 글자도 안 바뀐다.
        PresentationCommandParameter ease = catalog.Find("char_rig_staging.move_by")!.Parameters[^1];
        Assert.Equal("ease", ease.Name);
        Assert.Equal("ease", ease.Type);
        Assert.False(ease.Required);
        Assert.Null(ease.Default);

        // 로더 무해성 — 선언이 없는 커맨드에는 모션이 붙지 않는다.
        Assert.Null(catalog.Find("char_rig_staging.scale_by")!.Motion);
        Assert.Single(catalog.Definitions, item => item.Motion is not null);
    }

    [Fact]
    public void 슬라이더_선언은_scale_by와_shot_zoom에_있고_없는_파라미터는_null이다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        // 무엇이 슬라이더로 만질 수 있는 수치인지는 코드가 아니라 선언이 정한다.
        PresentationParameterSlider multiplier =
            catalog.Find("char_rig_staging.scale_by")!.FindParameter("multiplier")!.Slider!;
        Assert.Equal(0.25, multiplier.Minimum);
        Assert.Equal(3.0, multiplier.Maximum);
        Assert.Equal(0.05, multiplier.Step);

        PresentationParameterSlider zoom =
            catalog.Find("shot.shot_zoom")!.FindParameter("zoom")!.Slider!;
        Assert.Equal(-10, zoom.Minimum);
        Assert.Equal(20, zoom.Maximum);

        // 로더 무해성 — 선언 없는 파라미터는 예전과 완전히 같다.
        Assert.Null(catalog.Find("char_rig_staging.scale_by")!.FindParameter("slot")!.Slider);
        Assert.Null(catalog.Find("char_rig_staging.move_by")!.FindParameter("x")!.Slider);
    }

    [Fact]
    public void 범주별_드롭다운_후보는_범주_Id로_거른다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        Assert.NotEmpty(catalog.For("shot"));
        Assert.All(catalog.For("shot"), item => Assert.Equal("shot", item.CategoryId));
        Assert.Empty(catalog.For("없는_범주"));
        Assert.Equal("샷·카메라", catalog.FindCategory("shot")!.DisplayName);
    }

    [Fact]
    public void 게임_정의가_있으면_기본값_대신_사용한다()
    {
        var definition = new GameDefinition
        {
            PresentationCommandCategories =
            {
                new PresentationCategorySpec { Id = "camera", Name = "카메라" }
            },
            PresentationCommands =
            {
                new PresentationCommandSpec
                {
                    Id = "camera.custom",
                    Name = "커스텀 카메라",
                    Category = "camera",
                    OutputCommand = "cam",
                    Parameters =
                    {
                        new PresentationParameterSpec
                        {
                            Name = "preset",
                            Type = "string",
                            Default = "custom"
                        },
                        new PresentationParameterSpec
                        {
                            Name = "duration",
                            Type = "float",
                            Required = true
                        }
                    }
                }
            }
        };

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);
        PresentationCommandDefinition command = Assert.Single(catalog.Definitions);

        Assert.Equal("camera.custom", command.Id);
        Assert.Equal("cam", command.OutputCommandName);
        Assert.Equal("camera", command.CategoryId);
        Assert.Equal(new[] { "preset", "duration" }, command.Parameters.Select(item => item.Name));
        Assert.Equal("custom", command.DefaultArgumentValues()["preset"]);
        Assert.Equal("카메라", Assert.Single(catalog.Categories).DisplayName);
    }

    [Fact]
    public void 범주_선언이_없으면_커맨드가_쓰는_범주를_등장_순서대로_파생한다()
    {
        var definition = new GameDefinition
        {
            PresentationCommands =
            {
                new PresentationCommandSpec { Id = "b.one", Category = "b" },
                new PresentationCommandSpec { Id = "a.one", Category = "a" },
                new PresentationCommandSpec { Id = "b.two", Category = "b" }
            }
        };

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);

        Assert.Equal(new[] { "b", "a" }, catalog.Categories.Select(item => item.Id));
    }

    [Fact]
    public void 카탈로그_기본값은_런타임_파서의_생략값과_같다()
    {
        // 툴은 내보낼 때 빈 인자를 카탈로그 기본으로 채운다 — 두 기본값이 다르면 손 대본의 <<size sigure 12>>와
        // 툴이 내보낸 <<size sigure 12 bust 0fr>>(런타임은 생략된 길이를 10fr로 읽는다)가 다르게 움직인다.
        // 스냅샷 TuningFixtures/runtime-defaults.txt는 런타임 파서 표의 c.Arg(i, 기본) · Float(c, i, 기본)이다.
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "runtime-defaults.txt"));
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;
        var mismatches = new List<string>();
        int compared = 0;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(' ', 3);
            string command = parts[0];
            int index = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            string runtimeDefault = parts[2];

            if (catalog.FindByOutputCommand(command) is not { } definition ||
                index >= definition.Parameters.Count ||
                string.IsNullOrEmpty(definition.Parameters[index].Default))
            {
                // 카탈로그에 기본이 없으면 툴은 그 인자를 비워 내보내고, 런타임이 제 기본을 쓴다 — 갈릴 것이 없다.
                continue;
            }

            compared++;
            string toolDefault = definition.Parameters[index].Default!;

            if (!SameValue(toolDefault, runtimeDefault))
            {
                mismatches.Add($"{command}[{index}] {definition.Parameters[index].Name}: 툴 {toolDefault} · 런타임 {runtimeDefault}");
            }
        }

        Assert.True(compared > 40, $"대조한 기본값이 너무 적습니다({compared}) — 픽스처를 읽지 못했을 수 있습니다.");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));

        static bool SameValue(string tool, string runtime) =>
            string.Equals(tool, runtime, StringComparison.Ordinal) ||
            (double.TryParse(tool, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double a) &&
             double.TryParse(runtime, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double b) &&
             a == b);
    }
}
