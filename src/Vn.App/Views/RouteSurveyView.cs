using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// <b>[루트] 탭 — 설문</b> (2026-11-04 소유자: *"특정 장면 내 에피소드별 선택지가 별도 탭에서 아주 정확하게 일종의 설문처럼
/// 표시가 되어 있고, 그걸 원하는 대로 세팅해 두는 방식 … 각 선택지를 고르는 것에 따라서 스탯이랑, 선택지 루트 같은 게 도달
/// 가능한지, 스탯 변화가 진짜 실제로 어떻게 되는지 같은 걸 수기로 체크한다는 느낌"*).
///
/// 활성 챕터의 에피소드를 장면 순 · 길 순으로 문항처럼 늘어놓는다. 문항마다 그 자리의 스탯, 다녀오는 분기와 그 조건의 성립
/// 여부, 보기(선택지)와 숨김/해금 판정 · 스탯 변화. 보기를 고르면 루트에 적히고 아래 문항들이 그 길로 다시 계산된다.
/// 루트는 여기서 <b>완전히</b> 정한다 — 지도는 보여 주기만 한다(소유자: "미니맵은 그걸 보여주는 용도면 충분").
/// 모델은 <see cref="RouteSurvey"/>(순수)이고 여기는 그리기와 편집 명령 호출뿐이다.
/// </summary>
public sealed class RouteSurveyView : UserControl
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0xc8, 0x7a));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xf0, 0x6a, 0x5a));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xff, 0xff, 0xff));
    private static readonly IBrush PlanBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0xa9, 0xf0));

    private readonly ComboBox _routeCombo = new() { FontSize = 11, MinWidth = 160, PlaceholderText = "루트 (없음)" };
    private readonly TextBox _nameBox = new() { FontSize = 11, Width = 140, PlaceholderText = "루트 이름" };
    private readonly Button _addButton = new() { Content = "＋ 루트", FontSize = 11 };
    private readonly Button _removeButton = new() { Content = "✕", FontSize = 11 };
    private readonly StackPanel _statsRow = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBlock _verdict = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _items = new() { Spacing = 8, Margin = new Thickness(0, 4, 8, 12) };
    private readonly TextBlock _chapterText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold };

    private AuthoringSession? _session;
    private Func<IReadOnlyList<ChapterEntry>>? _chapters;
    private bool _building;

    /// <summary>루트가 바뀌었다 — 셸이 지도를 다시 그린다.</summary>
    public event Action? RouteChanged;

    public RouteSurveyView()
    {
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(12, 8, 12, 4) };
        header.Children.Add(_chapterText);

        var routeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        routeRow.Children.Add(new TextBlock { Text = "루트", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        routeRow.Children.Add(_routeCombo);
        routeRow.Children.Add(_addButton);
        routeRow.Children.Add(_nameBox);
        routeRow.Children.Add(_removeButton);
        header.Children.Add(routeRow);

        var statsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        statsRow.Children.Add(new TextBlock { Text = "시작 스탯", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        statsRow.Children.Add(_statsRow);
        header.Children.Add(statsRow);
        header.Children.Add(_verdict);

        ToolTip.SetTip(_addButton, "새 루트를 만들고 활성으로 둡니다.");
        ToolTip.SetTip(_removeButton, "활성 루트를 지웁니다.");
        ToolTip.SetTip(_nameBox, "활성 루트의 이름 — Enter로 바꿉니다.");

        _routeCombo.SelectionChanged += (_, _) =>
        {
            if (_building || _session is null || ChapterId is not { } chapterId ||
                _routeCombo.Tag is not List<string?> ids || _routeCombo.SelectedIndex < 0 || _routeCombo.SelectedIndex >= ids.Count)
            {
                return;
            }

            Run("루트 고르기", () => _session.Editor.SetActivePreviewRoute(chapterId, ids[_routeCombo.SelectedIndex]));
        };
        _addButton.Click += (_, _) =>
        {
            if (ChapterId is { } chapterId)
            {
                Run("새 루트", () => _session!.Editor.AddPreviewRoute(chapterId));
            }
        };
        _removeButton.Click += (_, _) =>
        {
            if (ActiveRoute is { } route)
            {
                Run("루트 지우기", () => _session!.Editor.RemovePreviewRoute(route.Id));
            }
        };
        _nameBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && ActiveRoute is { } route)
            {
                args.Handled = true;
                Run("루트 이름", () => _session!.Editor.RenamePreviewRoute(route.Id, _nameBox.Text ?? string.Empty));
            }
        };

        Content = new DockPanel
        {
            Children =
            {
                Docked(header, Dock.Top),
                new ScrollViewer { Content = _items, Padding = new Thickness(12, 0, 0, 0) }
            }
        };
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    internal void Attach(AuthoringSession session, Func<IReadOnlyList<ChapterEntry>> chapters)
    {
        _session = session;
        _chapters = chapters;
        session.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        session.FileGraphStateChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private string? ChapterId => _session?.ActiveFile?.Name;

    private PreviewRoute? ActiveRoute => ChapterId is { } chapterId ? _session?.Editor.ActivePreviewRoute(chapterId) : null;

    private void Run(string what, Action action)
    {
        UiGuard.Run(_session, what, () =>
        {
            action();
            Refresh();
            RouteChanged?.Invoke();
        });
    }

    public void Refresh()
    {
        if (_building)
        {
            return;
        }

        _building = true;

        try
        {
            Build();
        }
        finally
        {
            _building = false;
        }
    }

    private void Build()
    {
        _items.Children.Clear();
        _statsRow.Children.Clear();

        ChapterGraphModel? model = ChapterId is { } chapterId
            ? _chapters?.Invoke().FirstOrDefault(entry => string.Equals(entry.ChapterId, chapterId, StringComparison.Ordinal))?.Model
            : null;

        if (_session is null || model is null)
        {
            _chapterText.Text = "챕터를 고르면 설문이 섭니다 — 무대의 챕터 선택기에서.";
            _routeCombo.ItemsSource = null;
            _verdict.Text = string.Empty;
            _nameBox.Text = string.Empty;
            _removeButton.IsEnabled = false;
            return;
        }

        PreviewRoute? route = _session.Editor.ActivePreviewRoute(model.ChapterId);
        RouteSurvey survey = RouteSurvey.Build(model, _session.Project, _session.Definition, route);

        _chapterText.Text = $"{model.ChapterId} — 에피소드 {model.Episodes.Count} · 장면 {survey.Scenes.Count}";

        // 루트 고르기.
        List<PreviewRoute> routes = _session.Editor.PreviewRoutesOf(model.ChapterId).ToList();
        var ids = new List<string?> { null };
        var labels = new List<string> { "(루트 없음 — 갈림마다 묻는다)" };
        foreach (PreviewRoute item in routes)
        {
            ids.Add(item.Id);
            labels.Add($"{item.Name} · 선택 {item.Choices.Count}");
        }

        _routeCombo.ItemsSource = labels;
        _routeCombo.Tag = ids;
        _routeCombo.SelectedIndex = route is null ? 0 : Math.Max(0, ids.IndexOf(route.Id));
        _nameBox.Text = route?.Name ?? string.Empty;
        _nameBox.IsEnabled = route is not null;
        _removeButton.IsEnabled = route is not null;

        // 시작 스탯 — 스탯마다 칸, 비우면 초기값.
        foreach (ChapterStat stat in model.Stats)
        {
            var box = new TextBox
            {
                Width = 56,
                FontSize = 11,
                IsEnabled = route is not null,
                PlaceholderText = stat.Initial.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Text = route is not null && route.Stats.TryGetValue(stat.Key, out int value)
                    ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty
            };
            ToolTip.SetTip(box, $"{stat.DisplayName} {stat.Minimum}~{stat.Maximum} · 비우면 초기값 {stat.Initial}");
            box.LostFocus += (_, _) =>
            {
                if (route is null)
                {
                    return;
                }

                int? parsed = int.TryParse(box.Text, out int number) ? number : null;
                Run("시작 스탯", () => _session.Editor.SetPreviewRouteStat(route.Id, stat.Key, parsed));
            };

            _statsRow.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = stat.DisplayName, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
                    box
                }
            });
        }

        // 결론.
        if (route is null)
        {
            _verdict.Text = "루트가 없습니다 — [＋ 루트]로 만들고 아래 문항에서 고르세요. 전체 재생은 갈림마다 묻습니다.";
            _verdict.Foreground = DimBrush;
        }
        else if (survey.Walk.ReachedEnd)
        {
            _verdict.Text = $"예정: {string.Join(" → ", survey.Walk.EpisodeIds)} (끝까지) · 끝 스탯 " +
                string.Join(" · ", survey.Walk.FinalStats.Select(value => $"{value.Stat.DisplayName} {value.DisplayText}"));
            _verdict.Foreground = OkBrush;
        }
        else
        {
            _verdict.Text = $"예정: {string.Join(" → ", survey.Walk.EpisodeIds)} ⚠ {survey.Walk.StoppedBecause}" +
                (survey.Unanswered.Count > 0 ? $" · 아직 안 고른 문항 {survey.Unanswered.Count}" : string.Empty);
            _verdict.Foreground = WarnBrush;
        }

        // 문항 — 장면마다 머리, 에피소드마다 한 칸.
        foreach (ChapterMinimapScene scene in survey.Scenes)
        {
            _items.Children.Add(new TextBlock
            {
                Text = $"장면 · {scene.DisplayName}",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            });

            foreach (RouteSurveyEpisode episode in survey.Episodes.Where(episode => episode.SceneId == scene.SceneId))
            {
                _items.Children.Add(EpisodeCard(episode, route, model));
            }
        }
    }

    private Control EpisodeCard(RouteSurveyEpisode episode, PreviewRoute? route, ChapterGraphModel model)
    {
        var body = new StackPanel { Spacing = 3 };

        string order = episode.OnRoute ? $"{episode.StepIndex + 1}." : episode.IsBranchCard ? "▪" : "·";
        string stats = episode.StatsOnArrival is { } arrival
            ? string.Join(" · ", arrival.Select(value => $"{value.Stat.DisplayName} {value.DisplayText}"))
            : (episode.IsBranchCard ? "분기 카드 — 부르는 자리에서 다녀온다" : "미도달 — 루트가 여기로 오지 않는다");

        body.Children.Add(new TextBlock
        {
            Text = $"{order} {episode.Title}  ({episode.EpisodeId})",
            FontSize = 12,
            FontWeight = episode.OnRoute ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = episode.OnRoute || episode.IsBranchCard ? 1 : 0.6
        });
        body.Children.Add(new TextBlock { Text = $"   스탯: {stats}", FontSize = 11, Opacity = 0.8 });

        // 분기 — "한 번 갔다가 진행".
        foreach (RouteSurveyDetour detour in episode.Detours)
        {
            (string mark, IBrush brush) = detour switch
            {
                { Reached: false } => ("미도달", DimBrush),
                { Passes: true } => ("통과 — 재생된다", OkBrush),
                { Passes: false } => ("막힘 — 곧장 돌아온다", WarnBrush),
                _ => ("평가 불가 — 무대에서 직접 고른다", DimBrush)
            };
            body.Children.Add(new TextBlock
            {
                Text = $"   ↳ 분기 {detour.Title} ({detour.BranchEpisodeId}) · 조건 {(string.IsNullOrEmpty(detour.Condition) ? "없음" : detour.Condition)} · {mark}",
                FontSize = 11,
                Foreground = brush
            });
        }

        // 보기 — 선택지.
        if (episode.Choices.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = episode.IsBranchCard ? string.Empty : "   (나가는 간선 없음 — 챕터 끝)", FontSize = 11, Opacity = 0.7 });
        }
        else if (!episode.IsFork)
        {
            RouteSurveyChoice only = episode.Choices[0];
            body.Children.Add(new TextBlock
            {
                Text = $"   → {only.Edge.ToEpisodeId} {(only.IsAuto ? "(자동 간선 — 묻지 않는다)" : $"'{only.Label}' (하나뿐)")}{Deltas(only, episode)}",
                FontSize = 11,
                Opacity = 0.85
            });
        }
        else
        {
            foreach (RouteSurveyChoice choice in episode.Choices)
            {
                var radio = new RadioButton
                {
                    GroupName = $"route:{episode.EpisodeId}",
                    FontSize = 11,
                    IsChecked = choice.IsSelected,
                    IsEnabled = route is not null,
                    Content = new TextBlock
                    {
                        Text = $"{choice.Label} → {choice.Edge.ToEpisodeId}{Gates(choice)}{Deltas(choice, episode)}",
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = choice.IsSelectable == false ? WarnBrush : choice.IsSelected ? PlanBrush : null
                    }
                };
                radio.IsCheckedChanged += (_, _) =>
                {
                    if (_building || route is null || radio.IsChecked != true || choice.IsSelected)
                    {
                        return;
                    }

                    Run("루트 선택", () => _session!.Editor.SetPreviewRouteChoice(route.Id, episode.EpisodeId, choice.Edge.OptionLabel));
                };
                body.Children.Add(radio);
            }

            if (route is not null && episode.Choices.Any(choice => choice.IsSelected))
            {
                var clear = new Button { Content = "선택 비우기", FontSize = 10, Padding = new Thickness(6, 1), Margin = new Thickness(24, 0, 0, 0) };
                clear.Click += (_, _) => Run("루트 선택 비우기", () => _session!.Editor.SetPreviewRouteChoice(route.Id, episode.EpisodeId, null));
                body.Children.Add(clear);
            }
        }

        return new Border
        {
            Padding = new Thickness(8, 6),
            BorderBrush = episode.OnRoute ? PlanBrush : (IBrush)new SolidColorBrush(Color.FromArgb(0x40, 0xff, 0xff, 0xff)),
            BorderThickness = new Thickness(episode.OnRoute ? 2 : 1, 0, 0, 0),
            Child = body
        };
    }

    /// <summary>숨김 · 해금 관문 — 라벨과 그 자리의 판정.</summary>
    private static string Gates(RouteSurveyChoice choice)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(choice.Edge.VisibleConditionLabel))
        {
            parts.Add($"👁 {choice.Edge.VisibleConditionLabel}{Verdict(choice.Visible, "보임", "숨김")}");
        }

        if (!string.IsNullOrWhiteSpace(choice.Edge.ConditionLabel))
        {
            parts.Add($"🔒 {choice.Edge.ConditionLabel}{Verdict(choice.Unlock, "열림", "잠김")}");
        }

        return parts.Count == 0 ? string.Empty : "  [" + string.Join(" · ", parts) + "]";
    }

    private static string Verdict(ChapterGateVerdict? verdict, string open, string closed) => verdict switch
    {
        null => string.Empty,
        ChapterGateVerdict.Open => $" ✔{open}",
        ChapterGateVerdict.Blocked => $" ✖{closed}",
        _ => " ⚠조건 깨짐"
    };

    /// <summary>스탯 변화 — 그 자리의 스탯 위에 실제로 어떻게 되는지("성실 +2 → 2").</summary>
    private static string Deltas(RouteSurveyChoice choice, RouteSurveyEpisode episode)
    {
        if (choice.Edge.StatChanges.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        foreach (StatDelta delta in choice.Edge.StatChanges)
        {
            ChapterRunStatValue? now = episode.StatsOnArrival?.FirstOrDefault(value => value.Stat.Key == delta.Key);
            string change = delta.IsSet ? $"= {delta.Amount}" : $"{(delta.Amount >= 0 ? "+" : string.Empty)}{delta.Amount}";
            string result = now is null
                ? string.Empty
                : $" → {Math.Clamp(delta.IsSet ? delta.Amount : now.Value + delta.Amount, now.Stat.Minimum, now.Stat.Maximum)}";
            parts.Add($"{(now?.Stat.DisplayName ?? delta.Key)} {change}{result}");
        }

        return "  {" + string.Join(" · ", parts) + "}";
    }
}
