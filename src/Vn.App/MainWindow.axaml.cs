using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.App;

/// <summary>
/// 앱 셸. 세션을 하나 만들고 무대와 연출 편집기에 같은 것을 물려준다.
///
/// 화면들은 서로를 모른다. 모두 <see cref="AuthoringSession"/>만 보고 있고,
/// 편집은 전부 <see cref="ProjectEditor"/>를 지난다.
///
/// ⛔ 창은 무대 하나다(2026-10-29) — 챕터 그래프 · 연출 그래프 · 대사 편집기는 이야기의 뼈대와 대사를 쓰는
/// 화면이라 ked-story-director에 남았다(docs/plans/compositor-split.md C1).
/// </summary>
public partial class MainWindow : Window
{
    private const string BaseTitle = "VnTool";

    private readonly AuthoringSession _session = new();

    /// <summary>검증용 손잡이 — 창이 쓰는 세션 하나. 테스트가 판을 짓고 선택을 옮긴다.</summary>
    internal AuthoringSession SessionProbe => _session;

    // ── 연출 편집기 ──────────────────────────────────────────────────────────
    //
    // ⚠ 화면에 걸지 않는다. 이 편집기는 고른 연출 노드의 커맨드 행을 들고 있다가 무대에
    // 작업대(커맨드 하나의 편집 행 + 수치 조절)와 추가 콘솔을 공급한다 — 무대가 그것을
    // 보여 준다. 예전에 걸려 있던 자리(연출 그래프의 곁기둥)는 그래프와 함께 걷혔다.
    // 이름을 XAML 시절 그대로 둔 것은 의도다: 참조하는 자리가 여러 군데라 이름이 바뀌면
    // 이사와 무관한 diff가 그만큼 생긴다.
    private readonly PresentationNodeEditor PresentationEditor = new();
    private readonly AssetExplorerView AssetExplorer = new() { MaxHeight = 300 };

    private LiveOutputService? _liveOutput;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _restoredRecent;

    /// <summary>연출 채널을 세우는 중이다 — 채널이 선택을 옮기는 동안 그 선택이 또 채널을 부르지 않게 한다.</summary>
    private bool _enteringChannel;

    public MainWindow()
    {
        InitializeComponent();

        // 마지막 안전망 (공통 불변식 4). 개별 핸들러의 포획을 놓친 예외가 여기까지 오면
        // 로그와 상태줄에 남기고 앱은 계속 산다 — 클릭 하나가 미저장 원고를 끝내지 않는다.
        // 개별 경로의 포획을 대신하는 장치가 아니라, 새 코드가 실수했을 때의 바닥이다.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            UiGuard.Report(_session, "화면 동작", e.Exception);
        };

        // 오디오 실재생 (W62) — 재생 실패·미지원 보고는 상태줄 하나로 모이고,
        // 셸이 닫히면 소리도 함께 끝난다.
        AudioPreview.Problem = message => _session.SetStatus(message);
        Closed += (_, _) => AudioPreview.StopAll();

        // 에셋 탐색기는 무대 프리뷰 조절창 아래에 산다 (2026-08-26 소유자 — 연출
        // 그래프에서 이사). 자리는 뷰의 것, 배선(Attach·에셋 루트 지정)은 셸의 것이다.
        StagePreview.AssetExplorerHost.Content = AssetExplorer;

        ApplyExportChrome();

        PresentationEditor.Attach(_session);
        StagePreview.Attach(_session);
        AssetExplorer.Attach(_session);
        _liveOutput = new LiveOutputService(_session);
        StagePreview.LineMoveRequested += delta =>
        {
            // 선택은 활성 편집기의 것 하나뿐이다 — 프리뷰 창은 그걸 움직일 뿐이다.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.MoveStageLine(delta);
            }
        };
        StagePreview.LineSelectRequested += lineId =>
        {
            // 대본 패널의 대사 행 클릭 (2026-08-20) — 같은 규칙: 활성 편집기의 선택 하나.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.SelectStageLineById(lineId);
            }
        };
        StagePreview.DetourResumeRequested += lineId =>
        {
            // detour 복귀 (2026-08-22) — 돌아간 노드의 편집기가 떠난 줄을 짚고 한 줄
            // 나아간다. 다녀온 detour는 경로에서 빠져 있으므로(PopDetour가 표시했다)
            // 그 한 걸음이 곧 "나머지 대본"의 첫 줄이다.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.SelectStageLineById(lineId);
                PresentationEditor.MoveStageLine(1);
            }
        };
        StagePreview.SceneChosen += EnterPresentationChannel;
        // 작업대 = Inspector (2026-08-21 소유자: "점의 세부 조절창과 연출 편집창이
        // 합쳐지는 게 맞겠네") — 선택 커맨드 하나의 편집 행(연출 편집기) + 수치 조절
        // (무대 뷰)을 연출 편집기가 한 판으로 조합해 프리뷰 탭에 공급한다. 연출 추가는
        // 터미널 우클릭이 띄우는 콘솔이다(검색·종류별 탭·직접 입력 — 2026-08-21 소유자:
        // "콘솔을 띄운다는 느낌으로").
        StagePreview.CommandDetailProvider = commandId =>
            PresentationEditor.IsVisible
                ? PresentationEditor.BuildCommandInspector(commandId, StagePreview.BuildSceneInspector)
                : null;
        StagePreview.AddConsoleProvider = (lineId, setup, close) =>
            PresentationEditor.IsVisible
                ? PresentationEditor.BuildAddConsole(lineId, setup, close)
                : null;
        StagePreview.NodeExitRequested = () =>
        {
            // 문서 끝 도달 (W39) — 활성 편집기가 실행 출구를 따라 다음 노드로 전환하면
            // 재생이 이어진다. 전환된 노드의 편집기가 열리며 첫 라인 프리뷰를 민다.
            return PresentationEditor.IsVisible && PresentationEditor.TryExitPlaybackNode();
        };
        StagePreview.ManipulationApplied += () =>
        {
            // 직접 조작은 연출 노드의 바인딩을 바꿨다 — 커맨드 행에 즉시 보여야 한다.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.Rebuild();
            }
        };
        PresentationEditor.StagePreview = StagePreview;

        _session.Changed += OnSessionChanged;
        // 상태줄만 바뀐 경우는 셸 갱신 하나로 끝난다 (2026-08-18) — 판을 다시 그리거나
        // 챕터를 다시 읽을 이유가 없다. 그 구분이 없어서 노드가 늘수록 느려졌다.
        _session.StatusChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                RefreshShell();
            }
            else
            {
                Dispatcher.UIThread.Post(RefreshShell);
            }
        };
        _session.SelectionChanged += OnSelectionChanged;
        _session.FileGraphStateChanged += OnFileGraphStateChanged;

        NewButton.Click += OnNewClick;
        OpenButton.Click += OnOpenClick;
        SaveButton.Click += OnSaveClick;
        SaveAsButton.Click += OnSaveAsClick;
        UndoButton.Click += (_, _) => _session.Editor.Undo();
        RedoButton.Click += (_, _) => _session.Editor.Redo();
        ExportButton.Click += OnExportClick;
        CsvExportButton.Click += OnCsvExportClick;
        ExportFormatsButton.Click += (_, _) =>
            UiGuard.Run(_session, "내보내기 양식 선택", ShowExportFormatsFlyout);

        Opened += OnOpened;

        // 저장 단축키 (W49) — 어디에 포커스가 있어도 Ctrl+S가 저장이다.
        //
        // ⛔ <b>Ctrl+Z가 없었다</b> (2026-09-18에 알았다). 되돌리기는 <b>단추로만</b> 됐는데,
        //    그 단추는 창 위쪽 띠에 있어서 트리에서 실수로 지운 사람이 찾아 올라가야 했다.
        //    지우는 단추를 세우면서 소유자가 짚은 조건이 *"ctrl+z로 롤백이 가능해야"*이고,
        //    그것이 이미 되는 줄 알았던 것이 문제였다.
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!args.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                return;
            }

            switch (args.Key)
            {
                case Avalonia.Input.Key.S:
                    args.Handled = true;
                    OnSaveClick(this, new RoutedEventArgs());
                    break;

                // ⚠ 글 입력칸에서는 <b>가로채지 않는다</b> — 거기서 Ctrl+Z는 <b>타이핑</b>을
                //    되돌리는 것이고, 그것을 프로젝트 되돌리기로 바꾸면 쓰던 문장이 통째로
                //    날아간다. TextBox가 제 되돌리기를 먼저 하도록 둔다.
                case Avalonia.Input.Key.Z when FocusManager?.GetFocusedElement() is not TextBox:
                    args.Handled = true;
                    UiGuard.Run(_session, "되돌리기", _session.Editor.Undo);
                    break;

                case Avalonia.Input.Key.Y when FocusManager?.GetFocusedElement() is not TextBox:
                    args.Handled = true;
                    UiGuard.Run(_session, "다시 실행", _session.Editor.Redo);
                    break;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // 10분 자동 저장 (W49) — 저장 경로가 있고 바뀐 것이 있을 때만, 상태줄로 알린다.
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _autoSaveTimer.Tick += (_, _) => UiGuard.Run(_session, "자동 저장", () =>
        {
            if (_session.ProjectPath is not null && _session.IsDirty)
            {
                _session.Save();
                _session.SetStatus($"자동 저장했습니다 ({DateTime.Now:HH:mm})");
            }
        });
        _autoSaveTimer.Start();

        SupplyChapters();
        ShowSelectedNode();
        RefreshShell();
    }

    /// <summary>
    /// 무대의 챕터 · 씬 선택기에 프로젝트의 챕터를 공급한다. 원천은 프로젝트 하나다
    /// (⛔ 예전에는 챕터 그래프 뷰가 읽은 목록을 셸이 나눠 줬다 — 그 뷰가 걷혔다).
    /// </summary>
    private void SupplyChapters()
    {
        StagePreview.SupplyChapters(_session.ChapterEntries());
    }

    /// <summary>
    /// 씬 하나를 연출할 수 있는 상태로 만든다 (2026-08-21) — 대사 발행 → 연출 노드 →
    /// 결과 연결 → 연출 발행까지 한 번에(멱등). 소유자: "미리 다 해둔 다음에 무대
    /// 프리뷰측에서 뭘 할지 고르도록".
    ///
    /// 부르는 곳이 둘이다 — <b>씬 선택기</b>와 <b>대사 노드가 골라진 것</b>(프로젝트를 열면 시작 노드가
    /// 골라진다). 둘은 같은 뜻이라 같은 함수를 지난다(사본 금지) — 무대는 대사 노드를 그리지 않는다:
    /// 그것을 고른 채로 두면 발행본이 그려져 <b>잠긴 화면</b>이 된다(2026-08-22 소유자 보고).
    /// </summary>
    private void EnterPresentationChannel(string dialogueNodeId)
    {
        if (_enteringChannel)
        {
            return;
        }

        _enteringChannel = true;

        try
        {
            PresentationChannelOutcome outcome = _session.Editor.EnsurePresentationChannel(
                dialogueNodeId,
                _session.Definition);

            if (!outcome.Ready)
            {
                // 채널이 못 서면(대사 발행 불가 등) 무대는 비우고 이유는 상태줄로.
                _session.SetStatus(outcome.Problem ?? "연출 채널을 세울 수 없습니다.");
                _session.Select(dialogueNodeId);
                return;
            }

            _session.Select(outcome.Presentation!.Id);
        }
        finally
        {
            _enteringChannel = false;
        }
    }

    /// <summary>
    /// 내보내기 단추 chrome. ⏸ [CSV 내보내기…]와 [양식…]은 <b>임시로</b> 내렸다 (2026-08-25 소유자).
    /// 지우지 않고 접어 두는 이유: 뒤의 것(OnCsvExportClick·ShowExportFormatsFlyout·LiveOutputService의
    /// 양식 선택)은 그대로 살아 있고 라이브 출력이 계속 그 선택을 따르므로, 코드를 걷으면 되살릴 때
    /// 두 벌이 된다. 되돌리는 것은 아래 두 줄을 <c>true</c>로 하는 것뿐이다.
    /// (⛔ 예전에는 탭마다 달랐다 — 챕터 그래프에서는 [내보내기…]도 접혔다. 그 탭이 걷혔다.)
    /// </summary>
    private void ApplyExportChrome()
    {
        CsvExportButton.IsVisible = false;
        ExportFormatsButton.IsVisible = false;
    }

    /// <summary>
    /// 검증용 손잡이 — 최근 프로젝트 복원을 끈다. 헤드리스 테스트의 창은 "이 컴퓨터가
    /// 마지막에 연 프로젝트"를 물려받으면 안 된다: 복원된 챕터의 감시자가 부르는 에피소드
    /// 동기화가 테스트가 세운 노드를 솎아 내고 판을 다시 그려, 부하에 따라 매번 다른
    /// 테스트가 흔들렸다(2026-08-26 — 더블클릭 넷 사이에 판이 재건되면 제스처도 끊긴다).
    /// </summary>
    internal static bool RestoreRecentProjectOnOpen { get; set; } = true;

    /// <summary>
    /// 최근 프로젝트 복원은 편의 기능이다. 실패해도 빈 창은 그대로 쓸 수 있어야 한다.
    /// 이 핸들러는 async void라 예외가 새면 잡아 줄 곳이 없고 곧장 프로세스를 죽인다.
    /// </summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        if (_restoredRecent || !RestoreRecentProjectOnOpen)
        {
            return;
        }

        _restoredRecent = true;

        try
        {
            if (AppSettingsService.LoadRecentProject() is { } recent)
            {
                _session.Open(recent);
            }
        }
        catch (Exception exception)
        {
            StartupLog.TryWrite("최근 프로젝트 복원", exception);
            _session.SetStatus(
                $"마지막에 열었던 프로젝트를 열지 못했습니다. '열기…'로 다시 시도해 주세요. ({exception.Message})");
        }
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        void Apply()
        {
            ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(e.Kind, _session.SelectedNode);

            if (plan.RefreshChapters)
            {
                SupplyChapters();
            }

            if (plan.RebuildInspector)
            {
                RebuildInspector();
            }

            if (plan.RefreshStagePreview)
            {
                PresentationEditor.RefreshStagePreview();
            }

            RefreshShell();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        ShowSelectedNode();
        RefreshShell();

        // 무대는 대사 노드를 그리지 않는다 — 대사 노드가 골라지면(프로젝트를 열면 시작 노드가 골라진다)
        // 그 씬의 연출 채널로 들어간다. 이벤트 안에서 곧장 선택을 또 옮기면 뒤에 선 구독자가 바뀐 순서로
        // 받으므로 다음 차례로 미룬다. 구조 변경(되돌리기 등)이 부르는 다시 그리기에서는 들어가지 않는다 —
        // 사람이 고른 순간에만 채널을 세운다.
        if (_session.SelectedNode is DialogueNode dialogue && !_enteringChannel)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (string.Equals(_session.SelectedNodeId, dialogue.Id, StringComparison.Ordinal))
                {
                    UiGuard.Run(_session, "연출 채널", () => EnterPresentationChannel(dialogue.Id));
                }
            });
        }
    }

    private void OnFileGraphStateChanged(object? sender, FileGraphStateChangedEventArgs e)
    {
        void Apply()
        {
            // 프로젝트를 열거나 새로 만들면 챕터 목록이 바뀐다 — 무대의 선택기가 새 목록을 읽는다.
            if (e.FileListChanged || e.ActiveFileChanged)
            {
                SupplyChapters();
            }

            RefreshShell();
        }

        // 콤보의 RoutedEvent가 끝나기 전에 목록을 통째로 교체하면 고른 컨트롤을 자기 이벤트
        // 안에서 바꾸게 된다. 다음 UI turn에서 갱신한다.
        Dispatcher.UIThread.Post(Apply);
    }

    // ── 파일 ────────────────────────────────────────────────────────────────

    private async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _session.NewProject();

            // 새 프로젝트는 곧장 한 번 저장한다 (W49) — 저장돼야 프로젝트 폴더가 확정되고
            // 에셋 폴더·기본 튜닝 살림(W48)이 준비된다.
            await SaveAsAsync();

            if (_session.ProjectPath is null)
            {
                _session.SetStatus("저장을 건너뛰었습니다 — 저장하면 에셋 폴더와 기본 튜닝이 준비됩니다.");
            }
        }
        catch (Exception exception)
        {
            Report("새 프로젝트", exception);
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

            if (storage is null || !storage.CanOpen)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "VnTool 프로젝트 열기",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { ProjectFileType() }
                });

            if (files.Count > 0)
            {
                _session.Open(files[0].Path.LocalPath);
            }
        }
        catch (Exception exception)
        {
            Report("프로젝트 열기", exception);
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.ProjectPath is null)
            {
                await SaveAsAsync();
                return;
            }

            _session.Save();
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("저장", exception);
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsAsync();
        }
        catch (Exception exception)
        {
            Report("저장", exception);
        }
    }

    private async Task SaveAsAsync()
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanSave)
        {
            _session.SetStatus("이 환경에서는 저장 창을 열 수 없습니다.");
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "VnTool 프로젝트 저장",
                SuggestedFileName = "project" + ProjectManifestJson.FileExtension,
                FileTypeChoices = new[] { ProjectFileType() }
            });

        if (file is not null)
        {
            _session.Save(file.Path.LocalPath);
            RefreshShell();
        }
    }

    // ── 내보내기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 대사 노드들을 골라 런타임 .yarn 트리오로 내보낸다. 짝이 되는 연출은 명시적 합성이
    /// 아니라 <see cref="NodeExportResolver"/>가 연출 공급 연결에서 계산한다.
    /// 선언 파일은 폴더당 하나만 나온다.
    /// </summary>
    /// <summary>
    /// 내보내기 양식 선택 (X13). 프로젝트에 저장되고, 내보내기 버튼들이 이 선택을 따른다.
    /// </summary>
    private void ShowExportFormatsFlyout()
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 180 };
        ExportFormatSelection current = _session.Project.ExportFormats;

        panel.Children.Add(new TextBlock
        {
            Text = "내보내기에서 산출할 양식",
            FontSize = 11,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        void AddToggle(string label, bool value, Action<ExportFormatSelection, bool> assign)
        {
            var toggle = new CheckBox { Content = label, IsChecked = value, FontSize = 11 };

            toggle.IsCheckedChanged += (_, _) =>
            {
                ExportFormatSelection next = _session.Project.ExportFormats.Clone();
                assign(next, toggle.IsChecked == true);
                _session.Editor.SetExportFormats(next);
            };

            panel.Children.Add(toggle);
        }

        AddToggle("Yarn 트리오 (Story·Set·Pres + 선언)", current.YarnTrio, (f, v) => f.YarnTrio = v);
        AddToggle("Script CSV (번역·녹음)", current.ScriptCsv, (f, v) => f.ScriptCsv = v);
        AddToggle("Review CSV (기획 검수)", current.ReviewCsv, (f, v) => f.ReviewCsv = v);
        AddToggle("Direction CSV (연출 테이블)", current.DirectionCsv, (f, v) => f.DirectionCsv = v);

        // 라이브 출력 폴더 (X12c, D-1) — 지정하면 편집마다 자동 재합성·재저장된다.
        panel.Children.Add(new TextBlock
        {
            Text = $"라이브 출력: {_session.Project.OutputPath ?? "(미지정 — 자동 산출 없음)"}",
            FontSize = 10,
            Opacity = 0.7,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "클라우드 동기화 폴더는 쓰기 충돌이 날 수 있습니다.",
            FontSize = 9,
            Opacity = 0.5
        });

        var pickOutput = new Button { Content = "라이브 출력 폴더 지정…", FontSize = 11 };
        pickOutput.Click += async (_, _) => await UiGuard.RunAsync(_session, "출력 폴더 지정", async () =>
        {
            if (StorageProvider is not { CanPickFolder: true } storage)
            {
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "라이브 출력 폴더", AllowMultiple = false });

            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } picked)
            {
                return;
            }

            string stored = picked;

            if (_session.ProjectPath is { } projectPath)
            {
                string relative = Path.GetRelativePath(Path.GetDirectoryName(projectPath) ?? projectPath, picked);

                if (!Path.IsPathRooted(relative))
                {
                    stored = relative;
                }
            }

            _session.Editor.SetOutputPath(stored);
            _liveOutput?.WriteNow();
        });
        panel.Children.Add(pickOutput);

        var clearOutput = new Button { Content = "라이브 출력 해제", FontSize = 11 };
        clearOutput.Click += (_, _) => _session.Editor.SetOutputPath(null);
        panel.Children.Add(clearOutput);

        AddOrphanOutputs(panel);

        new Flyout { Content = panel, Placement = PlacementMode.Bottom }.ShowAt(ExportFormatsButton);
    }

    /// <summary>
    /// 고아 출력 목록 (K1 ②안) — 노드를 지우거나 이름을 바꾸면 옛 파일이 출력 폴더에 남고,
    /// 유니티가 폴더를 통째로 읽으면 그 옛 대사가 재생된다. VnTool은 <b>지우지 않고 보여 준다</b>:
    /// 출력 폴더는 사용자의 폴더이지 도구의 소유물이 아니다.
    /// </summary>
    private void AddOrphanOutputs(StackPanel panel)
    {
        OrphanOutputScan scan = _liveOutput?.Orphans ?? OrphanOutputScan.Empty;

        if (scan.Orphans.Count == 0 && scan.Note is null)
        {
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"낡은 산출 파일 {scan.Orphans.Count}개",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 11,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "지금 프로젝트가 만들지 않는 파일입니다. VnTool은 지우지 않습니다 — "
                + "탐색기에서 직접 지우세요.",
            FontSize = 9,
            Opacity = 0.6,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        foreach (OrphanOutput orphan in scan.Orphans)
        {
            panel.Children.Add(new TextBlock
            {
                Text = orphan.Source == OrphanOutputSource.Recorded
                    ? $"• {orphan.FileName}"
                    : $"• {orphan.FileName} (기록 없음 — 이름 형식만 같습니다)",
                FontSize = 10,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }

        if (scan.Note is { } note)
        {
            panel.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 9,
                Opacity = 0.6,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_session.Project.ExportFormats.YarnTrio)
            {
                _session.SetStatus("양식 선택에서 Yarn 트리오가 꺼져 있습니다. [양식…]에서 켜세요.");
                return;
            }

            IReadOnlyList<LiveComposition> exports = await PickNodeExportsAsync();

            if (exports.Count == 0)
            {
                return;
            }

            List<YarnBundle> bundles = exports.Select(export => export.Bundle!).ToList();

            if (await PickExportFolderAsync(".yarn 트리오를 내보낼 폴더") is not { } folder)
            {
                return;
            }

            var written = new List<string>(YarnBundleEmitter.WriteBundles(bundles, folder));

            // 커스텀 곡선(W67 후속) — 번들이 @이름으로 참조하는 곡선을 런타임 스키마로
            // 함께 낸다. 곡선이 없으면 파일도 없다(런타임의 무음 0개 경로).
            if (YarnBundleEmitter.WriteCurves(_session.Project.EaseCurves, folder) is { } curvesPath)
            {
                written.Add(curvesPath);
            }

            int warnings = bundles.Sum(bundle => bundle.Problems.Count) +
                exports.Sum(export => export.Warnings.Count);
            _session.SetStatus(
                $"{written.Count}개 파일을 내보냈습니다: " +
                string.Join(", ", written.Select(Path.GetFileName)) +
                (warnings > 0 ? $" · 경고 {warnings}건" : string.Empty));
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("내보내기", exception);
        }
    }

    /// <summary>
    /// 번역·녹음 / 기획 검수 / 연출 테이블 CSV 3종을 내보낸다.
    /// .yarn과 같은 입구(대사 노드 + 연출 공급 연결)를 쓰되, 파일 형식만 다르다.
    /// </summary>
    private async void OnCsvExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_session.Project.ExportFormats.AnyCsv)
            {
                _session.SetStatus("양식 선택에서 CSV가 전부 꺼져 있습니다. [양식…]에서 켜세요.");
                return;
            }

            IReadOnlyList<LiveComposition> exports = await PickNodeExportsAsync();

            if (exports.Count == 0)
            {
                return;
            }

            if (await PickExportFolderAsync("CSV를 내보낼 폴더") is not { } folder)
            {
                return;
            }

            var written = new List<string>();

            foreach (LiveComposition export in exports)
            {
                CsvBundle bundle = CsvBundleExporter.Export(
                    export.WorkingDialogue!,
                    export.WorkingPresentation,
                    _session.Project,
                    _session.Definition);
                // 선택한 양식만 산출된다 (X13).
                written.AddRange(CsvBundleExporter.WriteTo(bundle, folder, _session.Project.ExportFormats));
            }

            _session.SetStatus(
                $"CSV {written.Count}개를 내보냈습니다: " +
                string.Join(", ", written.Select(Path.GetFileName)));
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("CSV 내보내기", exception);
        }
    }

    /// <summary>
    /// 내보낼 대사 노드를 고른다. 하나뿐이면 바로 그것을 쓴다.
    /// 내보낼 수 없는 노드(발행 없음 등)는 목록에서 이유와 함께 비활성으로 보인다.
    /// </summary>
    /// <summary>
    /// 내보낼 노드를 고른다. 발행은 게이트가 아니다 (D-2) — 합성은 라이브 출력과 같은
    /// <see cref="LiveNodeComposer"/>(현재 작업 상태의 Freeze)를 지나므로 바이트가 같다.
    /// </summary>
    private async Task<IReadOnlyList<LiveComposition>> PickNodeExportsAsync()
    {
        List<LiveComposition> candidates = _session.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .Select(node => LiveNodeComposer.Compose(
                _session.Project, node.Id, _session.Definition, DateTimeOffset.UtcNow))
            .ToList();

        List<LiveComposition> exportable = candidates.Where(item => item.CanWrite).ToList();

        if (exportable.Count == 0)
        {
            LiveComposition? firstBlocked = candidates.FirstOrDefault(item => item.BlockingProblems.Count > 0);
            _session.SetStatus(firstBlocked is null
                ? "내보낼 대사 노드가 없습니다."
                : $"내보낼 수 있는 노드가 없습니다 — {firstBlocked.DialogueNodeName}: {firstBlocked.BlockingProblems[0]}");
            return Array.Empty<LiveComposition>();
        }

        if (exportable.Count == 1)
        {
            return new[] { exportable[0] };
        }

        // 챕터를 실어 나른다 — 목록의 글줄과 거르개가 같은 값 하나를 본다.
        List<ExportPick> picks = exportable
            .Select(item => new ExportPick(item, ChapterLabelOf(item)))
            .OrderBy(pick => pick.Chapter, StringComparer.Ordinal)
            .ThenBy(pick => pick.Composition.DialogueNodeName, StringComparer.Ordinal)
            .ToList();

        var list = new ListBox { SelectionMode = SelectionMode.Multiple };

        // ⛔ 거르개 (2026-08-25 소유자: "내보낼 대사노드 선택하는 팝업창에 챕터 단위
        //    필터기능을 추가해"). 챕터가 여럿이면 목록이 수십 줄이 되는데, 사람이
        //    내보내려는 것은 <b>대개 한 챕터</b>다. 거르개가 없으면 남의 챕터를 하나씩
        //    풀어야 하고, 하나 빠뜨리면 그냥 같이 나간다 — 나간 뒤에는 안 보인다.
        var chapters = new List<string> { AllChapters };
        chapters.AddRange(picks.Select(pick => pick.Chapter).Distinct(StringComparer.Ordinal));

        var filter = new ComboBox
        {
            ItemsSource = chapters,
            SelectedIndex = 0,
            MinWidth = 180
        };

        // 고른 챕터만 남기고 <b>전부 고른 채로</b> 둔다 — 거르개는 "무엇을 볼까"가 아니라
        // "무엇을 낼까"이므로, 거른 뒤 다시 전체 선택을 시키면 두 번 시키는 것이 된다.
        void ApplyFilter()
        {
            string chosen = filter.SelectedItem as string ?? AllChapters;

            list.ItemsSource = string.Equals(chosen, AllChapters, StringComparison.Ordinal)
                ? picks
                : picks.Where(pick => string.Equals(pick.Chapter, chosen, StringComparison.Ordinal)).ToList();

            list.SelectAll();
        }

        filter.SelectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();

        var confirm = new Button
        {
            Content = "내보내기",
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "챕터", VerticalAlignment = VerticalAlignment.Center },
                filter
            }
        };

        DockPanel.SetDock(header, Dock.Top);

        var dialog = new Window
        {
            Title = "내보낼 대사 노드 선택 (여러 개 선택 가능)",
            Width = 520,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    header,
                    Docked(confirm, Dock.Bottom),
                    new ScrollViewer { Content = list }
                }
            }
        };

        var picked = new List<LiveComposition>();

        // ⚠ 자리(index)가 아니라 <b>고른 것 자체</b>를 읽는다. 거르개가 목록을 갈아 끼우면
        //   자리는 원본 목록과 어긋나므로, 자리로 되짚으면 남의 노드를 내보낸다.
        confirm.Click += (_, _) =>
        {
            foreach (object? item in list.SelectedItems ?? Array.Empty<object>())
            {
                if (item is ExportPick pick)
                {
                    picked.Add(pick.Composition);
                }
            }

            dialog.Close();
        };

        await dialog.ShowDialog(this);
        return picked;
    }

    /// <summary>거르개의 "전부" 자리. 챕터 이름과 겹치지 않게 괄호를 쓴다.</summary>
    private const string AllChapters = "(전체)";

    /// <summary>
    /// 그 노드가 어느 챕터의 것인가. 판 이름이 곧 챕터다(챕터=판 1:1).
    /// 어디에도 안 속한 노드는 <b>숨기지 않고</b> 제 자리를 준다 — 안 보이면 못 고친다.
    /// </summary>
    private static string ChapterLabelOf(LiveComposition composition) =>
        composition.Bundle?.ChapterId is { Length: > 0 } chapter ? chapter : "(챕터 없음)";

    /// <summary>목록 한 줄. <c>ToString</c>이 곧 보이는 글줄이다.</summary>
    private sealed record ExportPick(LiveComposition Composition, string Chapter)
    {
        public override string ToString()
        {
            string pair = Composition.WorkingPresentation is not null
                ? "현재 대사 + 연출"
                : "현재 대사 (연출 공급 없음)";
            string warning = Composition.Warnings.Count > 0
                ? $" · 경고 {Composition.Warnings.Count}"
                : string.Empty;

            // 챕터를 <b>앞에</b> 세운다 — 거르개를 (전체)로 두고 훑을 때, 눈이 왼쪽 끝에서
            // 묶음을 읽는다. 뒤에 붙이면 노드 이름 길이에 따라 자리가 들쭉날쭉해진다.
            return $"[{Chapter}] {Composition.DialogueNodeName} · {pair}{warning}";
        }
    }

    private async Task<string?> PickExportFolderAsync(string title)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanPickFolder)
        {
            _session.SetStatus("이 환경에서는 폴더 선택 창을 열 수 없습니다.");
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private static Control Docked(Control control, Dock dock)
    {
        control.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static FilePickerFileType ProjectFileType()
    {
        return new FilePickerFileType("VnTool 프로젝트")
        {
            Patterns = new[] { "*.vnproject.json", "*.vnstory.json", "*.json" }
        };
    }

    // ── 화면 ────────────────────────────────────────────────────────────────



    private static Control MenuSeparator() => new Border
    {
        Height = 1,
        Margin = new Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128))
    };

    // [편집 자료] 현황판(RebuildResourcePanel)은 2026-08-21에 사라졌다 (소유자) —
    // 대본·발행 결과·연출 공급을 읽기 전용으로 나열하던 판인데, 발행·배선이 자동이
    // 된 뒤로는 볼 이유가 없다. 대본은 노드 카드가, 짝은 내보내기가 말한다.

    private void ShowSelectedNode()
    {
        StoryNode? node = _session.SelectedNode;

        PresentationEditor.IsVisible = node is PresentationNode;

        if (node is PresentationNode)
        {
            PresentationEditor.Show(node.Id);
        }
        else
        {
            // 무대는 연출 편집기가 채운다. 다른 노드에서는 빈 상태로 돌린다(대사 노드는 곧 채널로 들어간다).
            StagePreview.Show(null);
        }

        // 씬 선택기가 현재 씬을 따라온다 — 연출 노드는 공급 대상(대사 노드)으로 옮겨 읽는다.
        StagePreview.SetCurrentScene(node switch
        {
            DialogueNode dialogue => dialogue.Id,
            PresentationNode presentation => _session.Project.Links.FirstOrDefault(link =>
                    link.Kind == NodeLinkKind.PresentationSupply &&
                    link.IsEnabled &&
                    string.Equals(link.SourceNodeId, presentation.Id, StringComparison.Ordinal))
                ?.TargetNodeId,
            _ => null
        });
    }

    /// <summary>
    /// 구조가 바뀌었을 때 연출 편집기(와 그것이 채우는 무대)를 다시 만든다.
    /// 선택된 노드가 그대로면 같은 노드를 다시 그리고, 바뀌었으면 그쪽으로 옮긴다.
    /// </summary>
    private void RebuildInspector()
    {
        StoryNode? node = _session.SelectedNode;

        if (node is PresentationNode && PresentationEditor.NodeId == node.Id)
        {
            PresentationEditor.Rebuild();
            return;
        }

        ShowSelectedNode();
    }

    private void RefreshShell()
    {
        string name = _session.ProjectPath is null
            ? _session.Project.Title
            : ProjectDisplayName(_session.ProjectPath);

        ProjectNameText.Text = name;
        ProjectPathText.Text = _session.ProjectPath ?? "저장되지 않음";
        StatusText.Text = _session.StatusMessage;

        bool dirty = _session.IsDirty;
        DirtyText.Text = dirty ? "● 저장되지 않은 변경" : string.Empty;

        UndoButton.IsEnabled = _session.Editor.CanUndo;
        RedoButton.IsEnabled = _session.Editor.CanRedo;

        Title = dirty ? $"* {BaseTitle} — {name}" : $"{BaseTitle} — {name}";
    }

    private static string ProjectDisplayName(string path)
    {
        string fileName = Path.GetFileName(path) ?? string.Empty;
        return fileName.EndsWith(ProjectManifestJson.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ProjectManifestJson.FileExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName) ?? fileName;
    }

    private void Report(string action, Exception exception)
    {
        StartupLog.TryWrite(action, exception);
        _session.SetStatus($"{action}하지 못했습니다. {exception.Message}");
        RefreshShell();
    }
}
