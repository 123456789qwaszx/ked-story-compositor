using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Vn.Authoring.Chapters;

namespace Vn.App.Views;

/// <summary>
/// <b>챕터 지도</b> (2026-11-04 소유자: *"슬레이 더 스파이어의 미니맵 같은 노드 기반 연결 + 마야의 애님스쿨 피커 같은 느낌"*).
/// 무대 왼쪽의 씬 선택기 위에 선다 — 점이 에피소드, 선이 간선, 띠가 장면. 점을 누르면 그 씬이 무대에 오른다(드롭다운과 같은 길).
///
/// - 자리는 director 챕터 그래프의 것을 축소해 그린다(편집 없음 — 자리의 주인은 director).
/// - 모양: 보통 ● · 갈림 ◆ · 장면 루트 ◎(굵은 테) · 챕터 끝 ● 안에 ■ · 분기 카드 ▪(작은 네모).
/// - 선: 선택지 실선 · 자동 가는 점선 · 분기 다녀오기 점선.
/// - 전체 재생의 길(<see cref="ChapterRunTrail"/>): 지나온 점 채움, 탄 선 굵게, 안 탄 선 흐리게, 조건에 막혀 곧장 돌아온 분기는 빈 네모,
///   지금 자리는 바깥 고리.
///
/// 모델(<see cref="ChapterMinimap"/>)은 순수하고 여기는 그리기만 한다 — 지도의 규칙은 테스트가 잠근 모델에 있다.
/// </summary>
public sealed class ChapterMinimapView : UserControl
{
    private const double NodeSize = 20;          // 애님스쿨 피커처럼 누를 수 있는 크기
    private const double MapPadding = 28;
    private const double MinSpacing = 34;        // 점 사이 최소 간격 — director 자리를 축소해도 겹치지 않게
    private const double MapMaxWidth = 460;

    private static readonly IBrush NodeFill = new SolidColorBrush(Color.FromRgb(0x4a, 0x4f, 0x58));
    private static readonly IBrush NodeStroke = new SolidColorBrush(Color.FromRgb(0xc8, 0xcc, 0xd2));
    private static readonly IBrush VisitedFill = new SolidColorBrush(Color.FromRgb(0xe8, 0xa3, 0x2a));
    private static readonly IBrush CurrentStroke = new SolidColorBrush(Color.FromRgb(0xff, 0xd8, 0x6e));
    private static readonly IBrush EdgeStroke = new SolidColorBrush(Color.FromRgb(0x9a, 0x9f, 0xa8));
    private static readonly IBrush TakenStroke = new SolidColorBrush(Color.FromRgb(0xe8, 0xa3, 0x2a));
    private static readonly IBrush BandFill = new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff));
    private static readonly IBrush BandAltFill = new SolidColorBrush(Color.FromArgb(0x10, 0xff, 0xff, 0xff));
    private static readonly IBrush BandText = new SolidColorBrush(Color.FromArgb(0x99, 0xff, 0xff, 0xff));
    private static readonly IBrush PlanStroke = new SolidColorBrush(Color.FromRgb(0x5a, 0xa9, 0xf0));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xf0, 0x6a, 0x5a));

    private readonly Canvas _canvas = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _caption = new() { FontSize = 11, Margin = new Thickness(4, 2, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    private ChapterMinimap? _map;
    private ChapterRunTrail? _trail;
    private string? _currentDialogueId;

    /// <summary>점을 눌렀다 — 그 카드(대사 노드)로 씬을 바꾼다. 카드 없는 점은 누를 수 없다.</summary>
    public event Action<string>? NodeClicked;

    public ChapterMinimapView()
    {
        _scroll = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(0x2b, 0x2e, 0x33))
        };

        // 확대 · 축소 (2026-11-04 소유자) — Ctrl+휠, 또는 아래 줄의 − · ＋ · 맞춤. 배율은 director 자리에 곱하는 하나의 수다.
        var zoomOut = new Button { Content = "−", FontSize = 11, Padding = new Thickness(6, 0), MinWidth = 0 };
        var zoomIn = new Button { Content = "＋", FontSize = 11, Padding = new Thickness(6, 0), MinWidth = 0 };
        var zoomFit = new Button { Content = "맞춤", FontSize = 11, Padding = new Thickness(6, 0), MinWidth = 0 };
        ToolTip.SetTip(zoomOut, "축소 (Ctrl+휠 아래)");
        ToolTip.SetTip(zoomIn, "확대 (Ctrl+휠 위)");
        ToolTip.SetTip(zoomFit, "폭에 맞춤");
        zoomOut.Click += (_, _) => SetZoom(_zoom / ZoomStep);
        zoomIn.Click += (_, _) => SetZoom(_zoom * ZoomStep);
        zoomFit.Click += (_, _) => SetZoom(1);

        var bottom = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var zoomBar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2, Children = { zoomOut, zoomIn, zoomFit } };
        DockPanel.SetDock(zoomBar, Dock.Right);
        bottom.Children.Add(zoomBar);
        bottom.Children.Add(_caption);

        _scroll.PointerWheelChanged += (_, args) =>
        {
            if (!args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                return;
            }

            args.Handled = true;
            SetZoom(args.Delta.Y > 0 ? _zoom * ZoomStep : _zoom / ZoomStep);
        };

        Content = new DockPanel
        {
            Children =
            {
                Docked(bottom, Dock.Bottom),
                _scroll
            }
        };
    }

    private const double ZoomStep = 1.25;
    private const double ZoomMin = 0.4;
    private const double ZoomMax = 4;

    /// <summary>폭 맞춤 배율에 곱하는 수. 1이 "폭에 맞춤"이다.</summary>
    private double _zoom = 1;

    private void SetZoom(double zoom)
    {
        double clamped = Math.Clamp(zoom, ZoomMin, ZoomMax);

        if (Math.Abs(clamped - _zoom) < 0.0001)
        {
            return;
        }

        _zoom = clamped;
        Redraw();
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>지도를 다시 그린다. 모델이 없으면 비운다.</summary>
    /// <summary>선택지 선을 눌렀다 — (갈림 에피소드, 선택지 문구). 활성 루트에 적힌다.</summary>
    public event Action<string, string>? EdgeClicked;

    private PreviewRouteWalker.Result? _plan;

    /// <param name="plan">미리 정한 루트로 걸은 예정 길 — 파란 점선으로, 멈춘 자리는 ⚠. null이면 안 그린다.</param>
    public void Show(ChapterMinimap? map, ChapterRunTrail? trail, string? currentDialogueId, PreviewRouteWalker.Result? plan = null)
    {
        _map = map;
        _trail = trail;
        _currentDialogueId = currentDialogueId;
        _plan = plan;
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();

        if (_map is null || _map.Nodes.Count == 0)
        {
            _caption.Text = "챕터 지도 — 챕터를 고르면 에피소드가 점으로 섭니다.";
            _canvas.Width = 0;
            _canvas.Height = 0;
            return;
        }

        (double minX, double minY, double maxX, double maxY) = _map.Bounds;
        double scale = ScaleFor(_map, minX, maxX) * _zoom;
        double width = (maxX - minX) * scale + MapPadding * 2;
        double height = (maxY - minY) * scale + MapPadding * 2;
        _canvas.Width = Math.Max(width, 80);
        _canvas.Height = Math.Max(height, 80);

        Point At(ChapterMinimapNode node) => new((node.X - minX) * scale + MapPadding, (node.Y - minY) * scale + MapPadding);
        var positions = _map.Nodes.ToDictionary(node => node.EpisodeId, At, StringComparer.Ordinal);

        HashSet<string> visited = _trail?.Visited.ToHashSet(StringComparer.Ordinal) ?? [];
        HashSet<string> blocked = _trail?.BlockedBranches.ToHashSet(StringComparer.Ordinal) ?? [];
        HashSet<(string, string)> taken = _trail?.TakenEdges.ToHashSet() ?? [];
        HashSet<(string, string, string?)> planned = _plan?.Edges
            .Select(edge => (edge.FromEpisodeId, edge.ToEpisodeId, string.IsNullOrWhiteSpace(edge.OptionLabel) ? null : edge.OptionLabel))
            .ToHashSet() ?? [];
        string? stoppedAt = _plan is { ReachedEnd: false } ? _plan.StoppedAt : null;
        bool hasTrail = _trail is { Steps.Count: > 0 };
        string? currentEpisode = _map.FindByDialogue(_currentDialogueId)?.EpisodeId;

        // 장면 띠 — 점들의 경계 상자. 띠 경계가 "장면이 바뀌면 무대가 비워진다"를 말한다.
        for (int index = 0; index < _map.Scenes.Count; index++)
        {
            ChapterMinimapScene scene = _map.Scenes[index];
            Point[] points = scene.EpisodeIds.Where(positions.ContainsKey).Select(id => positions[id]).ToArray();

            if (points.Length == 0)
            {
                continue;
            }

            double left = points.Min(point => point.X) - NodeSize;
            double top = points.Min(point => point.Y) - NodeSize - 6;
            double right = points.Max(point => point.X) + NodeSize;
            double bottom = points.Max(point => point.Y) + NodeSize;

            var band = new Border
            {
                Width = right - left,
                Height = bottom - top,
                Background = index % 2 == 0 ? BandFill : BandAltFill,
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = scene.DisplayName,
                    FontSize = 10,
                    Foreground = BandText,
                    Margin = new Thickness(6, 2, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                }
            };
            Canvas.SetLeft(band, left);
            Canvas.SetTop(band, top);
            _canvas.Children.Add(band);
        }

        // 선 — 점보다 아래에.
        foreach (ChapterMinimapEdge edge in _map.Edges)
        {
            if (!positions.TryGetValue(edge.FromEpisodeId, out Point from) ||
                !positions.TryGetValue(edge.ToEpisodeId, out Point to))
            {
                continue;
            }

            bool wasTaken = taken.Contains((edge.FromEpisodeId, edge.ToEpisodeId));
            bool isPlanned = planned.Contains((edge.FromEpisodeId, edge.ToEpisodeId, edge.Label));
            var line = new Line
            {
                StartPoint = from,
                EndPoint = to,
                Stroke = wasTaken ? TakenStroke : isPlanned ? PlanStroke : EdgeStroke,
                StrokeThickness = wasTaken ? 3 : isPlanned ? 2.4 : edge.Kind == ChapterMinimapEdgeKind.Choice ? 1.6 : 1,
                Opacity = hasTrail && !wasTaken && !isPlanned ? 0.35 : 0.9,
                StrokeDashArray = edge.Kind switch
                {
                    _ when isPlanned && !wasTaken => new AvaloniaList<double> { 6, 3 },
                    ChapterMinimapEdgeKind.Auto => new AvaloniaList<double> { 4, 3 },
                    ChapterMinimapEdgeKind.Detour => new AvaloniaList<double> { 1.5, 3 },
                    _ => null
                },
                IsHitTestVisible = false
            };

            _canvas.Children.Add(line);

            // 선택지 선은 누를 수 있다 — 루트에 적힌다. 보이는 선은 가늘어 넓은 투명 선이 대신 맞는다.
            if (edge.Kind == ChapterMinimapEdgeKind.Choice && edge.Label is { } label)
            {
                var hit = new Line
                {
                    StartPoint = from,
                    EndPoint = to,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 10,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                ToolTip.SetTip(hit, (isPlanned ? "✔ 루트에 적힘 — " : string.Empty) + label + "\n(누르면 루트에 적거나 뺍니다)");
                string fromId = edge.FromEpisodeId;
                hit.PointerPressed += (_, args) =>
                {
                    args.Handled = true;
                    EdgeClicked?.Invoke(fromId, label);
                };
                _canvas.Children.Add(hit);
            }
            else if (edge.Label is not null)
            {
                ToolTip.SetTip(line, edge.Label);
            }
        }

        // 점.
        foreach (ChapterMinimapNode node in _map.Nodes)
        {
            Point at = positions[node.EpisodeId];
            bool isVisited = visited.Contains(node.EpisodeId);
            bool isBlocked = blocked.Contains(node.EpisodeId);
            bool isCurrent = string.Equals(node.EpisodeId, currentEpisode, StringComparison.Ordinal);
            IBrush fill = isVisited ? VisitedFill : isBlocked ? Brushes.Transparent : NodeFill;

            Shape shape = ShapeFor(node, fill);
            shape.Opacity = hasTrail && !isVisited && !isBlocked && !isCurrent ? 0.55 : 1;
            Canvas.SetLeft(shape, at.X - shape.Width / 2);
            Canvas.SetTop(shape, at.Y - shape.Height / 2);

            if (isCurrent)
            {
                var ring = new Ellipse
                {
                    Width = NodeSize + 12,
                    Height = NodeSize + 12,
                    Stroke = CurrentStroke,
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, at.X - ring.Width / 2);
                Canvas.SetTop(ring, at.Y - ring.Height / 2);
                _canvas.Children.Add(ring);
            }

            string kind = node.Shape switch
            {
                ChapterMinimapShape.Branch => "분기 카드 — <<detour>>로 다녀간다",
                ChapterMinimapShape.SceneRoot => "장면 루트 — 여기서 무대를 세운다",
                ChapterMinimapShape.End => "챕터 끝",
                ChapterMinimapShape.Fork => "선택지 갈림",
                _ => "에피소드"
            };
            string state = isBlocked ? " · 조건에 막혀 곧장 돌아옴" : isVisited ? " · 지나옴" : string.Empty;

            if (string.Equals(node.EpisodeId, stoppedAt, StringComparison.Ordinal))
            {
                var warn = new TextBlock { Text = "⚠", FontSize = 13, Foreground = WarnBrush, IsHitTestVisible = false };
                ToolTip.SetTip(warn, _plan!.StoppedBecause);
                Canvas.SetLeft(warn, at.X + NodeSize / 2);
                Canvas.SetTop(warn, at.Y - NodeSize);
                _canvas.Children.Add(warn);
                state += $" · 예정 길이 여기서 멈춤: {_plan.StoppedBecause}";
            }
            ToolTip.SetTip(shape, $"{node.Title} ({node.EpisodeId})\n{kind} · 장면 {node.SceneId}{state}" +
                (node.DialogueNodeId is null ? "\n⚠ 카드가 없어 고를 수 없습니다 — director에서 세우세요" : string.Empty));

            if (node.DialogueNodeId is { } dialogueId)
            {
                shape.Cursor = new Cursor(StandardCursorType.Hand);
                shape.PointerPressed += (_, args) =>
                {
                    args.Handled = true;
                    NodeClicked?.Invoke(dialogueId);
                };
            }

            _canvas.Children.Add(shape);
        }

        _caption.Text = currentEpisode is null
            ? $"{_map.ChapterId} — 점을 누르면 그 씬이 무대에 오릅니다."
            : $"▶ {_map.Find(currentEpisode)!.Title}" +
              (hasTrail ? $" · 지나온 에피소드 {visited.Count}" + (blocked.Count > 0 ? $" · 막힌 분기 {blocked.Count}" : string.Empty) : string.Empty) +
              (Math.Abs(_zoom - 1) > 0.0001 ? $" · {_zoom:P0}" : string.Empty);

        if (currentEpisode is not null && positions.TryGetValue(currentEpisode, out Point focus))
        {
            // 지금 자리가 보이게 스크롤을 맞춘다 — 사람이 찾아 내려가지 않게.
            _scroll.Offset = new Vector(
                Math.Max(0, focus.X - _scroll.Viewport.Width / 2),
                Math.Max(0, focus.Y - _scroll.Viewport.Height / 2));
        }
    }

    /// <summary>director 자리를 폭에 맞춰 축소하되, 가장 가까운 두 점이 겹치지 않을 만큼은 지킨다.</summary>
    private static double ScaleFor(ChapterMinimap map, double minX, double maxX)
    {
        double span = Math.Max(maxX - minX, 1);
        double fit = Math.Min(1, (MapMaxWidth - MapPadding * 2) / span);

        double nearest = double.MaxValue;
        for (int i = 0; i < map.Nodes.Count; i++)
        {
            for (int j = i + 1; j < map.Nodes.Count; j++)
            {
                double dx = map.Nodes[i].X - map.Nodes[j].X;
                double dy = map.Nodes[i].Y - map.Nodes[j].Y;
                nearest = Math.Min(nearest, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        if (nearest is double.MaxValue or <= 0)
        {
            return fit;
        }

        return Math.Max(fit, Math.Min(1, MinSpacing / nearest));
    }

    private static Shape ShapeFor(ChapterMinimapNode node, IBrush fill)
    {
        switch (node.Shape)
        {
            case ChapterMinimapShape.Branch:
                return new Rectangle
                {
                    Width = NodeSize * 0.7, Height = NodeSize * 0.7, Fill = fill, Stroke = NodeStroke, StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2
                };

            case ChapterMinimapShape.Fork:
                double half = NodeSize / 2 + 2;
                return new Polygon
                {
                    Width = half * 2, Height = half * 2, Fill = fill, Stroke = NodeStroke, StrokeThickness = 1.5,
                    Points = [new Point(half, 0), new Point(half * 2, half), new Point(half, half * 2), new Point(0, half)]
                };

            case ChapterMinimapShape.SceneRoot:
                return new Ellipse
                {
                    Width = NodeSize + 4, Height = NodeSize + 4, Fill = fill, Stroke = NodeStroke, StrokeThickness = 3.5
                };

            case ChapterMinimapShape.End:
                return new Ellipse
                {
                    Width = NodeSize, Height = NodeSize, Fill = fill, Stroke = NodeStroke, StrokeThickness = 1.5,
                    StrokeDashArray = new AvaloniaList<double> { 2, 2 }
                };

            default:
                return new Ellipse { Width = NodeSize, Height = NodeSize, Fill = fill, Stroke = NodeStroke, StrokeThickness = 1.5 };
        }
    }
}
