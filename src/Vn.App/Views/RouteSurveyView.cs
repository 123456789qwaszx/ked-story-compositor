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
/// <b>[루트] 탭 — 설문</b>. 에피소드별 선택지를 별도 탭에 설문처럼 펼쳐 놓고 원하는 대로 정해 둔다 —
/// 스탯, 도달 가능 여부, 스탯이 실제로 어떻게 바뀌는지를 순서 · 목차 · 표로 손수 확인한다.
///
/// 세 칸이다. ① 루트 — 고르기 · 만들기 · 이름 · 시작 스탯 · 결론. ② 경로 표 — 루트가 지나는 에피소드를 <b>순서대로</b> 한 줄씩:
/// 장면 · 에피소드 · 도착 스탯 · 다녀오는 분기와 판정 · 선택(라디오) · 변화 → 결과. ③ 루트 밖 — 길이 안 닿는 에피소드와 분기 카드.
/// 고르면 아래 줄들이 그 길로 다시 계산된다. 루트는 여기서 <b>완전히</b> 정하고 지도는 보여 주기만 한다.
///
/// 글자색은 테마의 것을 쓴다(흰 글자는 밝은 테마에서 사라진다) — 뜻이 있는 셋(가능 · 막힘 · 루트)만 색이고,
/// 둘 다에서 읽히는 중간 명도다. 모델은 <see cref="RouteSurvey"/>(순수)이고 여기는 그리기와 편집 명령 호출뿐이다.
/// </summary>
public sealed class RouteSurveyView : UserControl
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.FromRgb(0x2e, 0x9e, 0x5b));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xd0, 0x4b, 0x3a));
    private static readonly IBrush PlanBrush = new SolidColorBrush(Color.FromRgb(0x1f, 0x6f, 0xd0));
    private static readonly IBrush GridLine = new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80));
    private static readonly IBrush HeaderFill = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80));
    private static readonly IBrush RowAltFill = new SolidColorBrush(Color.FromArgb(0x0e, 0x80, 0x80, 0x80));
    private static readonly IBrush PlanRowFill = new SolidColorBrush(Color.FromArgb(0x18, 0x1f, 0x6f, 0xd0));

    private readonly ComboBox _routeCombo = new() { FontSize = 12, MinWidth = 200, PlaceholderText = "루트 (없음)" };
    private readonly TextBox _nameBox = new() { FontSize = 12, Width = 160, PlaceholderText = "루트 이름 — Enter" };
    private readonly Button _addButton = new() { Content = "＋ 루트", FontSize = 12 };
    private readonly Button _removeButton = new() { Content = "✕ 지우기", FontSize = 12 };
    private readonly StackPanel _statsRow = new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _verdict = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _chapterText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(0, 0, 16, 16) };

    private AuthoringSession? _session;
    private Func<IReadOnlyList<ChapterEntry>>? _chapters;
    private bool _building;

    /// <summary>루트가 바뀌었다 — 셸이 지도를 다시 그린다.</summary>
    public event Action? RouteChanged;

    public RouteSurveyView()
    {
        // ① 루트 — 한 줄에 고르기 · 만들기 · 이름 · 지우기, 다음 줄에 시작 스탯, 그다음 결론.
        var routeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        routeRow.Children.Add(Label("루트"));
        routeRow.Children.Add(_routeCombo);
        routeRow.Children.Add(_addButton);
        routeRow.Children.Add(_nameBox);
        routeRow.Children.Add(_removeButton);

        var statsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        statsRow.Children.Add(Label("시작 스탯"));
        statsRow.Children.Add(_statsRow);

        var header = new StackPanel { Spacing = 8, Margin = new Thickness(16, 10, 16, 6) };
        header.Children.Add(_chapterText);
        header.Children.Add(Section("① 루트", routeRow, statsRow, _verdict));

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
                new ScrollViewer { Content = _body, Padding = new Thickness(16, 0, 0, 0) }
            }
        };
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 56 };

    private static Control Section(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });

        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
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

    // ── 짓기 ─────────────────────────────────────────────────────────────────

    private void Build()
    {
        _body.Children.Clear();
        _statsRow.Children.Clear();

        ChapterGraphModel? model = ChapterId is { } chapterId
            ? _chapters?.Invoke().FirstOrDefault(entry => string.Equals(entry.ChapterId, chapterId, StringComparison.Ordinal))?.Model
            : null;

        if (_session is null || model is null)
        {
            _chapterText.Text = "챕터를 고르면 설문이 섭니다 — 무대 탭의 챕터 선택기에서.";
            _routeCombo.ItemsSource = null;
            _verdict.Text = string.Empty;
            _nameBox.Text = string.Empty;
            _nameBox.IsEnabled = false;
            _removeButton.IsEnabled = false;
            return;
        }

        PreviewRoute? route = _session.Editor.ActivePreviewRoute(model.ChapterId);
        RouteSurvey survey = RouteSurvey.Build(model, _session.Project, _session.Definition, route);

        _chapterText.Text = $"{model.ChapterId} — 에피소드 {model.Episodes.Count} · 장면 {survey.Scenes.Count} · 스탯 {model.Stats.Count}";

        BuildRouteHeader(model, route, survey);
        _body.Children.Add(Section("② 경로 — 루트가 지나는 순서", RouteTable(survey, route)));
        _body.Children.Add(Section("③ 루트 밖 — 길이 닿지 않는 에피소드 · 분기 카드", OffRouteTable(survey)));
    }

    private void BuildRouteHeader(ChapterGraphModel model, PreviewRoute? route, RouteSurvey survey)
    {
        List<PreviewRoute> routes = _session!.Editor.PreviewRoutesOf(model.ChapterId).ToList();
        var ids = new List<string?> { null };
        var labels = new List<string> { "(루트 없음 — 전체 재생이 갈림마다 묻는다)" };

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

        foreach (ChapterStat stat in model.Stats)
        {
            var box = new TextBox
            {
                Width = 56,
                FontSize = 12,
                IsEnabled = route is not null,
                PlaceholderText = stat.Initial.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Text = route is not null && route.Stats.TryGetValue(stat.Key, out int value)
                    ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty
            };
            ToolTip.SetTip(box, $"{stat.DisplayName} {stat.Minimum}~{stat.Maximum} · 비우면 초기값 {stat.Initial}. 다음 전체 재생부터 씁니다.");
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
                Children = { new TextBlock { Text = stat.DisplayName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, box }
            });
        }

        if (route is null)
        {
            _verdict.Text = "결론: 루트가 없습니다 — [＋ 루트]로 만들고 ② 경로 표의 선택 칸에서 고르세요.";
            _verdict.ClearValue(TextBlock.ForegroundProperty);
        }
        else if (survey.Walk.ReachedEnd)
        {
            _verdict.Text = $"결론: 끝까지 간다 — {survey.Walk.EpisodeIds.Count} 에피소드 · 끝 스탯 " +
                string.Join(" · ", survey.Walk.FinalStats.Select(value => $"{value.Stat.DisplayName} {value.DisplayText}"));
            _verdict.Foreground = OkBrush;
        }
        else
        {
            _verdict.Text = $"결론: {survey.Walk.StoppedAt}에서 멈춘다 — {survey.Walk.StoppedBecause}" +
                (survey.Unanswered.Count > 0 ? $" · 아직 안 고른 갈림 {survey.Unanswered.Count}" : string.Empty);
            _verdict.Foreground = WarnBrush;
        }
    }

    // ── 표 ─────────────────────────────────────────────────────────────────

    private static readonly string[] RouteColumns = ["#", "장면", "에피소드", "도착 스탯", "다녀오는 분기", "선택", "변화 → 결과"];
    private static readonly GridLength[] RouteWidths =
    [
        new(36), new(90), new(200), new(120), new(220), new(1, GridUnitType.Star), new(160)
    ];

    private Control RouteTable(RouteSurvey survey, PreviewRoute? route)
    {
        Grid grid = NewTable(RouteColumns, RouteWidths);
        int row = 1;

        foreach (string episodeId in survey.Walk.EpisodeIds)
        {
            RouteSurveyEpisode episode = survey.Find(episodeId)!;
            IBrush? fill = row % 2 == 0 ? RowAltFill : null;

            Cell(grid, row, 0, Text($"{row}", bold: true), fill);
            Cell(grid, row, 1, Text(episode.SceneId), fill);
            Cell(grid, row, 2, Text($"{episode.Title}\n{episode.EpisodeId}", dimSecond: true), fill);
            Cell(grid, row, 3, Text(Stats(episode.StatsOnArrival)), fill);
            Cell(grid, row, 4, Detours(episode), fill);
            Cell(grid, row, 5, Choices(episode, route), fill);
            Cell(grid, row, 6, Outcome(episode), fill);
            row++;
        }

        if (survey.Walk is { ReachedEnd: false } walk)
        {
            var note = new TextBlock
            {
                Text = $"⚠ {walk.StoppedBecause}",
                FontSize = 12,
                Foreground = WarnBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 4)
            };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(note, row);
            Grid.SetColumnSpan(note, RouteColumns.Length);
            grid.Children.Add(note);
        }

        return grid;
    }

    private static readonly string[] OffColumns = ["장면", "에피소드", "종류", "선택지 · 조건 · 변화"];
    private static readonly GridLength[] OffWidths = [new(90), new(200), new(150), new(1, GridUnitType.Star)];

    private Control OffRouteTable(RouteSurvey survey)
    {
        Grid grid = NewTable(OffColumns, OffWidths);
        int row = 1;

        foreach (RouteSurveyEpisode episode in survey.Episodes.Where(episode => !episode.OnRoute))
        {
            IBrush? fill = row % 2 == 0 ? RowAltFill : null;
            string kind = episode.IsBranchCard ? "분기 카드 — 부르는 자리에서 다녀온다" : "미도달 — 루트가 오지 않는다";
            string choices = episode.Choices.Count == 0
                ? (episode.IsBranchCard ? "—" : "(나가는 간선 없음 — 챕터 끝)")
                : string.Join("\n", episode.Choices.Select(choice =>
                    $"{(choice.IsAuto ? "(자동)" : choice.Label)} → {choice.Edge.ToEpisodeId}{GateText(choice)}{DeltaText(choice, null)}"));

            Cell(grid, row, 0, Text(episode.SceneId), fill);
            Cell(grid, row, 1, Text($"{episode.Title}\n{episode.EpisodeId}", dimSecond: true), fill);
            Cell(grid, row, 2, Text(kind), fill);
            Cell(grid, row, 3, Text(choices), fill);
            row++;
        }

        if (row == 1)
        {
            Cell(grid, 1, 0, Text("모든 에피소드가 루트 위에 있습니다."), null, span: OffColumns.Length);
        }

        return grid;
    }

    private static Grid NewTable(string[] columns, GridLength[] widths)
    {
        var grid = new Grid();

        foreach (GridLength width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int column = 0; column < columns.Length; column++)
        {
            Cell(grid, 0, column, new TextBlock { Text = columns[column], FontSize = 12, FontWeight = FontWeight.SemiBold }, HeaderFill);
        }

        return grid;
    }

    private static void Cell(Grid grid, int row, int column, Control content, IBrush? fill, int span = 1)
    {
        while (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        var border = new Border
        {
            BorderBrush = GridLine,
            BorderThickness = new Thickness(column == 0 ? 1 : 0, row == 0 ? 1 : 0, 1, 1),
            Background = fill,
            Padding = new Thickness(6, 4),
            Child = content,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        Grid.SetColumnSpan(border, span);
        grid.Children.Add(border);
    }

    private static Control Text(string text, bool bold = false, bool dimSecond = false)
    {
        if (dimSecond && text.Contains('\n'))
        {
            string[] parts = text.Split('\n', 2);
            return new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = parts[0], FontSize = 12, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal },
                    new TextBlock { Text = parts[1], FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.65 }
                }
            };
        }

        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
        };
    }

    private static string Stats(IReadOnlyList<ChapterRunStatValue>? stats) =>
        stats is null ? "미도달" : string.Join("\n", stats.Select(value => $"{value.Stat.DisplayName} {value.DisplayText}"));

    /// <summary>대사 앞부분만 — 한 줄 넘게 늘어지지 않게.</summary>
    private static string Excerpt(string text, int max)
    {
        string oneLine = text.Replace("\r", string.Empty).Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }

    /// <summary>다녀오는 분기 — 차례대로 한 줄씩: 카드 · 줄 · 조건 · 판정.</summary>
    private static Control Detours(RouteSurveyEpisode episode)
    {
        if (episode.Detours.Count == 0)
        {
            return Text("—");
        }

        var panel = new StackPanel { Spacing = 2 };

        foreach (RouteSurveyDetour detour in episode.Detours)
        {
            (string mark, IBrush? brush) = detour switch
            {
                { Reached: false } => ("미도달", null),
                { Passes: true } => ("✔ 통과 — 재생된다", OkBrush),
                { Passes: false } => ("✖ 막힘 — 곧장 돌아온다", WarnBrush),
                _ => ("? 평가 불가 — 무대에서 고른다", null)
            };

            // 어느 줄 앞에서 갈라지는지 — 줄 번호는 무대의 줄 번호와 같다. 정확히 어느 줄에서 생기는지 보여야 한다.
            string at = string.IsNullOrWhiteSpace(detour.Speaker) && string.IsNullOrWhiteSpace(detour.Text)
                ? $"{detour.LineNumber}줄 앞"
                : $"{detour.LineNumber}줄 앞 — {(string.IsNullOrWhiteSpace(detour.Speaker) ? string.Empty : detour.Speaker + ": ")}“{Excerpt(detour.Text, 24)}”";

            var line = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Text = $"↳ {detour.Title}\n   {at}\n   {(string.IsNullOrEmpty(detour.Condition) ? "조건 없음" : detour.Condition)} · {mark}"
            };

            if (brush is not null)
            {
                line.Foreground = brush;
            }

            panel.Children.Add(line);
        }

        return panel;
    }

    /// <summary>선택 칸 — 갈림이면 라디오, 아니면 자동/하나뿐/끝.</summary>
    private Control Choices(RouteSurveyEpisode episode, PreviewRoute? route)
    {
        if (episode.Choices.Count == 0)
        {
            return Text("(챕터 끝)");
        }

        if (!episode.IsFork)
        {
            RouteSurveyChoice only = episode.Choices[0];
            return Text(only.IsAuto
                ? $"(자동) → {only.Edge.ToEpisodeId}"
                : $"'{only.Label}' → {only.Edge.ToEpisodeId} (하나뿐){GateText(only)}");
        }

        var panel = new StackPanel { Spacing = 2 };

        foreach (RouteSurveyChoice choice in episode.Choices)
        {
            var label = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Text = $"{choice.Label} → {choice.Edge.ToEpisodeId}{GateText(choice)}"
            };

            if (choice.IsSelectable == false)
            {
                label.Foreground = WarnBrush;
            }
            else if (choice.IsSelected)
            {
                label.Foreground = PlanBrush;
                label.FontWeight = FontWeight.SemiBold;
            }

            var radio = new RadioButton
            {
                GroupName = $"route:{episode.EpisodeId}",
                IsChecked = choice.IsSelected,
                IsEnabled = route is not null,
                Content = label,
                MinHeight = 0,
                Padding = new Thickness(4, 0, 0, 0)
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (_building || route is null || radio.IsChecked != true || choice.IsSelected)
                {
                    return;
                }

                Run("루트 선택", () => _session!.Editor.SetPreviewRouteChoice(route.Id, episode.EpisodeId, choice.Edge.OptionLabel));
            };
            panel.Children.Add(radio);
        }

        if (route is null)
        {
            panel.Children.Add(new TextBlock { Text = "루트를 만들면 고를 수 있습니다.", FontSize = 11, Opacity = 0.7 });
        }
        else if (!episode.Choices.Any(choice => choice.IsSelected))
        {
            panel.Children.Add(new TextBlock { Text = "⚠ 아직 안 골랐습니다.", FontSize = 11, Foreground = WarnBrush });
        }
        else
        {
            var clear = new Button { Content = "선택 비우기", FontSize = 11, Padding = new Thickness(6, 1), HorizontalAlignment = HorizontalAlignment.Left };
            clear.Click += (_, _) => Run("루트 선택 비우기", () => _session!.Editor.SetPreviewRouteChoice(route.Id, episode.EpisodeId, null));
            panel.Children.Add(clear);
        }

        return panel;
    }

    /// <summary>변화 → 결과 — 이 줄에서 실제로 타는 간선(고른 것 · 자동 · 하나뿐)의 스탯 변화와 그 결과.</summary>
    private static Control Outcome(RouteSurveyEpisode episode)
    {
        RouteSurveyChoice? taken = episode.Choices.FirstOrDefault(choice => choice.IsSelected)
            ?? (episode.IsFork ? null : episode.Choices.FirstOrDefault());

        if (taken is null)
        {
            return Text(episode.Choices.Count == 0 ? "—" : "(선택 전)");
        }

        if (taken.Edge.StatChanges.Count == 0)
        {
            return Text("변화 없음");
        }

        return Text(string.Join("\n", taken.Edge.StatChanges.Select(delta =>
        {
            ChapterRunStatValue? now = episode.StatsOnArrival?.FirstOrDefault(value => value.Stat.Key == delta.Key);
            string change = delta.IsSet ? $"= {delta.Amount}" : $"{(delta.Amount >= 0 ? "+" : string.Empty)}{delta.Amount}";
            string result = now is null ? string.Empty
                : $" → {Math.Clamp(delta.IsSet ? delta.Amount : now.Value + delta.Amount, now.Stat.Minimum, now.Stat.Maximum)}";
            return $"{now?.Stat.DisplayName ?? delta.Key} {change}{result}";
        })));
    }

    /// <summary>숨김 · 해금 관문 — 라벨과 그 자리의 판정.</summary>
    private static string GateText(RouteSurveyChoice choice)
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

    private static string DeltaText(RouteSurveyChoice choice, IReadOnlyList<ChapterRunStatValue>? stats)
    {
        if (choice.Edge.StatChanges.Count == 0)
        {
            return string.Empty;
        }

        return "  {" + string.Join(" · ", choice.Edge.StatChanges.Select(delta =>
        {
            string name = stats?.FirstOrDefault(value => value.Stat.Key == delta.Key)?.Stat.DisplayName ?? delta.Key;
            return $"{name} {(delta.IsSet ? "= " : (delta.Amount >= 0 ? "+" : string.Empty))}{delta.Amount}";
        })) + "}";
    }
}
