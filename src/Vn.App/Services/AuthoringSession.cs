using Avalonia.Media.Imaging;
using Vn.Authoring.Assets;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.App.Services;

/// <summary>
/// 앱이 지금 무엇을 열고 있는지 아는 유일한 객체.
///
/// 편집 자체는 <see cref="Vn.Authoring.Editing.ProjectEditor"/>가 한다. 이 클래스는 그 위에
/// 파일 경로·저장 여부·선택 상태처럼 <em>도구</em>의 관심사만 얹는다.
/// 도메인이 파일 경로를 알 필요는 없고, 알게 되면 콘솔이나 테스트에서 쓰기 어려워진다.
/// </summary>
internal sealed class AuthoringSession
{
    private string _fileListSignature = string.Empty;
    private string _savedSnapshot;

    public AuthoringSession()
    {
        Editor = new ProjectEditor(NewProjectInstance());
        ResetFileGraphState(Editor.Project);
        _savedSnapshot = ProjectSnapshotCodec.Encode(Editor.Project);
        Editor.Changed += OnEditorChanged;
    }

    public ProjectEditor Editor { get; }

    public StoryProject Project => Editor.Project;

    /// <summary>
    /// <b>이 툴의 파일</b>(연출 층, 원고 옆 <c>.vncompose.json</c>) 경로. 원고를 아직 열지 않았으면 null이다.
    /// 아직 없는 파일일 수 있다 — 첫 저장이 만든다. 에셋 · 튜닝 · 출력 폴더의 상대 경로는 이 자리(= 원고 폴더)가 기준이다.
    /// </summary>
    public string? ProjectPath { get; private set; }

    /// <summary>
    /// 원고 manifest(<c>.vnproject.json</c>) — ked-story-director의 파일이라 <b>읽기만</b> 한다
    /// (<c>docs/plans/compositor-split.md</c> K3). 아직 열지 않았으면 null이다.
    /// </summary>
    public string? ManuscriptPath { get; private set; }

    /// <summary>
    /// `game.definition.json`의 내용 — 기획자가 소유하는 어휘(스탯 · 조건 · 화자 · 연출 카탈로그).
    /// 열기 · 저장이 다시 읽는다.
    /// </summary>
    public GameDefinition Definition { get; private set; } = GameDefinition.Empty;

    /// <summary>
    /// 프로젝트가 든 챕터를 화면이 쓰는 모양으로 낸다 — 무대의 챕터 · 씬 선택기가 읽는다.
    /// (⛔ 예전에는 챕터 그래프 뷰가 폈고 셸이 나눠 줬다 — 그 뷰는 2026-10-29에 걷혔다.)
    /// </summary>
    public IReadOnlyList<ChapterEntry> ChapterEntries() =>
        Project.Chapters
            .Select(chapter => new ChapterEntry(
                chapter.ChapterId, chapter.ToGraphModel(chapter.ChapterId, Definition)))
            .OrderBy(entry => entry.ChapterId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 지금 고른 챕터의 판 — 무대의 챕터 선택기가 비춘다. 저장 콘텐츠나 되돌리기 대상이 아니라
    /// 앱 workspace 상태다.
    /// </summary>
    public string? ActiveFileId { get; private set; }

    public StoryFile? ActiveFile => Project.FindFile(ActiveFileId);

    public string? SelectedNodeId { get; private set; }

    public StoryNode? SelectedNode => Project.FindNode(SelectedNodeId);

    public string StatusMessage { get; private set; } = NoManuscriptMessage;

    /// <summary>원고를 열기 전의 안내 — 이 툴은 원고를 만들지 않는다.</summary>
    internal const string NoManuscriptMessage =
        "ked-story-director의 프로젝트(.vnproject.json)를 여세요 — 연출은 그 옆에 따로 저장됩니다.";

    // ── 프리뷰 에셋 ────────────────────────────────────────────────────────

    private PreviewAssetLibrary? _assetLibrary;
    private string _assetLibrarySignature = string.Empty;

    /// <summary>
    /// 프리뷰 에셋 인덱스. 에셋 루트 설정이 바뀌면(편집·되돌리기·열기) 자동으로 다시
    /// 읽지만, 폴더 <b>내용</b>의 변경은 감지하지 않는다 — 그건 <see cref="RefreshAssets"/>
    /// (명시적 새로 고침)만 반영한다.
    /// </summary>
    public PreviewAssetLibrary AssetLibrary
    {
        get
        {
            (string? backgrounds, string? portraits) = ResolveAssetRoots();
            string signature = $"{backgrounds}{portraits}";

            if (_assetLibrary is null ||
                !string.Equals(_assetLibrarySignature, signature, StringComparison.Ordinal))
            {
                _assetLibrary = PreviewAssetLibrary.Load(backgrounds, portraits);
                _assetLibrarySignature = signature;
                ImageCache.Clear();
            }

            return _assetLibrary;
        }
    }

    /// <summary>프리뷰 비트맵 캐시. 해석이 끝난 절대 경로가 키다.</summary>
    public PreviewImageCache<Bitmap> ImageCache { get; } = new(path => new Bitmap(path));

    /// <summary>
    /// 프리뷰 갈래 선택 (W35) — "지금 어느 갈래를 보고 있는가". 뷰 상태라 저장하지 않고,
    /// 프로젝트를 새로 열면 비워진다. 대사·연출 편집기가 같은 선택 하나를 본다(LineId 불변 덕).
    /// </summary>
    public StageBranchSelection BranchSelection { get; } = new();

    /// <summary>
    /// 조건 값 시뮬의 시작값 오버라이드 (W36-b) — 변수명 → "이 값으로 시작한다고 치자".
    /// 뷰 상태라 저장하지 않는다. 비어 있으면 등록 초기값 그대로다.
    /// </summary>
    public Dictionary<string, string> SimulationValues { get; } = new(StringComparer.Ordinal);

    // ── 런타임 tuning (W23) ────────────────────────────────────────────────

    private RuntimeTuningLibrary? _tuningLibrary;
    private string _tuningLibrarySignature = string.Empty;

    /// <summary>
    /// 런타임 tuning 덤프 인덱스. 경로 설정·정의 해상도가 바뀌면 다시 읽지만, 폴더
    /// <b>내용</b>의 변경은 <see cref="RefreshAssets"/>(명시적 새로 고침)만 반영한다 —
    /// <see cref="AssetLibrary"/>와 같은 규칙이다.
    /// </summary>
    public RuntimeTuningLibrary TuningLibrary
    {
        get
        {
            string? directory = ResolveTuningRoot();
            (double Width, double Height) fallback = Definition.PreviewResolution;
            string signature = $"{directory}{fallback.Width}x{fallback.Height}";

            if (_tuningLibrary is null ||
                !string.Equals(_tuningLibrarySignature, signature, StringComparison.Ordinal))
            {
                _tuningLibrary = RuntimeTuningLibrary.Load(directory, fallback);
                _tuningLibrarySignature = signature;
            }

            return _tuningLibrary;
        }
    }

    /// <summary>
    /// tuning 폴더 해석 — 정의 파일의 재지정이 있으면 그 경로(없으면 경고가 남도록 그대로 전달),
    /// 없으면 프로젝트 폴더의 <c>ExportedTuning</c> 규약 폴더(없으면 미수입 상태로 조용히 null —
    /// 아직 아무것도 약속하지 않은 기본값이므로 경고가 아니라 안내 대상이다).
    /// </summary>
    private string? ResolveTuningRoot()
    {
        string basePath = ProjectPath
            ?? Path.Combine(Environment.CurrentDirectory, "unsaved" + ProjectManifestJson.FileExtension);

        if (!string.IsNullOrWhiteSpace(Definition.RuntimeTuningPath))
        {
            return AssetRootSettings.ResolveFrom(basePath, Definition.RuntimeTuningPath);
        }

        string? conventional = AssetRootSettings.ResolveFrom(basePath, RuntimeTuningLibrary.DefaultFolderName);
        return conventional is not null && Directory.Exists(conventional) ? conventional : null;
    }

    /// <summary>해석된 초상화 루트 절대 경로 — 표정 스프라이트 복제(설정노드)가 쓴다.</summary>
    public string? PortraitsRoot => ResolveAssetRoots().Portraits;

    /// <summary>해석된 배경 루트 절대 경로 — 가져오기·폴더 열기(W48)가 쓴다.</summary>
    public string? BackgroundsRoot => ResolveAssetRoots().Backgrounds;

    /// <summary>해석된 BGM 루트 절대 경로 (W59). 파일명이 곧 clipKey다.</summary>
    public string? BgmRoot => ResolvePath(Project.AssetRoots.BgmPath);

    /// <summary>해석된 효과음 루트 절대 경로 (W59). 파일명이 곧 clipKey다.</summary>
    public string? SfxRoot => ResolvePath(Project.AssetRoots.SfxPath);

    /// <summary>오디오 파일 확장자 규약 (W59) — 이 셋만 clipKey 후보가 된다.</summary>
    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg"];

    /// <summary>루트 폴더의 오디오 clipKey 목록 (W59) — 파일명(확장자 제외)이 곧 키다.</summary>
    public IReadOnlyList<string> AudioClipKeys(string? root)
    {
        if (root is null || !Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(root)
            .Where(file => AudioExtensions.Contains(
                Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// clipKey를 실제 파일 경로로 되찾는다 (W62 미리 듣기) — 파일명(확장자 제외)=키
    /// 규약의 역방향. 없으면 null — 호출자가 사유를 상태줄로 알린다.
    /// </summary>
    public string? ResolveAudioClipPath(string? root, string clipKey)
    {
        if (root is null || string.IsNullOrWhiteSpace(clipKey) || !Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root)
            .Where(file => AudioExtensions.Contains(
                Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault(file => string.Equals(
                Path.GetFileNameWithoutExtension(file), clipKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 아무 곳의 오디오 파일을 BGM/효과음 루트로 복제해 들여온다 (W59) — 배경 가져오기와
    /// 같은 규약: 파일명이 곧 키, 같은 이름은 건너뛴다.
    /// </summary>
    public int ImportAudio(bool bgm, IEnumerable<string> files)
    {
        string kind = bgm ? "BGM" : "효과음";

        if ((bgm ? BgmRoot : SfxRoot) is not { } root)
        {
            SetStatus($"{kind} 폴더가 없습니다 — 프로젝트를 저장하면 assets 아래 준비됩니다.");
            return 0;
        }

        Directory.CreateDirectory(root);
        int copied = 0;
        var skipped = new List<string>();

        foreach (string file in files.Where(item => AudioExtensions.Contains(
            Path.GetExtension(item), StringComparer.OrdinalIgnoreCase)))
        {
            string destination = Path.Combine(root, Path.GetFileName(file));

            if (File.Exists(destination))
            {
                skipped.Add(Path.GetFileName(file));
                continue;
            }

            File.Copy(file, destination);
            copied++;
        }

        RefreshAssets();
        SetStatus($"{kind} {copied}개를 가져왔습니다." +
            (skipped.Count > 0 ? $" 같은 이름이 있어 건너뜀: {string.Join(", ", skipped)}" : string.Empty));
        return copied;
    }

    private string? ResolvePath(string? configured)
    {
        string basePath = ProjectPath
            ?? Path.Combine(Environment.CurrentDirectory, "unsaved" + ProjectManifestJson.FileExtension);

        return AssetRootSettings.ResolveFrom(basePath, configured);
    }

    /// <summary>
    /// 앱에 내장된 기본 튜닝(런타임 실측 덤프의 스냅샷)을 프로젝트 옆 규약 폴더에 만든다 (W46).
    /// 이미 내용이 있는 폴더는 덮어쓰지 않는다 — 교체는 <see cref="ConnectTuningFolder"/>가 한다.
    /// </summary>
    public void CreateDefaultTuning()
    {
        if (TuningTargetRoot() is not { } target)
        {
            return;
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            SetStatus(
                $"튜닝 폴더가 이미 있습니다: {target} — 덮어쓰지 않습니다. " +
                "교체하려면 '튜닝 폴더 연결…'로 다른 폴더를 복사해 오세요.");
            return;
        }

        var assembly = typeof(AuthoringSession).Assembly;
        const string prefix = "DefaultTuning/";
        int written = 0;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // LogicalName의 경로 구분자는 빌드 OS를 따른다 — 양쪽 다 받아 준다.
            string relative = name[prefix.Length..].Replace('\\', '/');
            string path = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using Stream source = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"내장 기본 튜닝 리소스를 열 수 없습니다: {name}");
            using FileStream file = File.Create(path);
            source.CopyTo(file);
            written++;
        }

        RefreshAssets();
        SetStatus($"기본 튜닝 {written}개 파일을 만들었습니다: {target} · {TuningLibrary.Summary}");
    }

    /// <summary>
    /// 기존 튜닝 폴더를 골라 프로젝트 옆 규약 폴더로 복사해 연결한다 (W46).
    /// 같은 이름의 파일은 덮어쓴다 — "통째 교체"가 튜닝 관리 규약이다(io-reference §1-3).
    /// </summary>
    public void ConnectTuningFolder(string sourceDirectory)
    {
        if (TuningTargetRoot() is not { } target)
        {
            return;
        }

        string source = Path.GetFullPath(sourceDirectory);

        if (!Directory.Exists(source))
        {
            SetStatus($"폴더를 찾을 수 없습니다: {source}");
            return;
        }

        // 튜닝처럼 보이는지 최소 확인 — 엉뚱한 폴더를 통째로 복사하는 사고를 막는다.
        bool looksLikeTuning =
            File.Exists(Path.Combine(source, "base-resolution.json")) ||
            File.Exists(Path.Combine(source, "rig-schemas.json")) ||
            Directory.Exists(Path.Combine(source, "presets"));

        if (!looksLikeTuning)
        {
            SetStatus(
                "선택한 폴더에서 튜닝 파일을 찾지 못했습니다 " +
                "(base-resolution.json · rig-schemas.json · presets/ 중 하나는 있어야 합니다).");
            return;
        }

        if (string.Equals(source, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            RefreshAssets(); // 이미 규약 자리다 — 다시 읽기만 한다
            return;
        }

        int copied = 0;

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            copied++;
        }

        RefreshAssets();
        SetStatus($"튜닝 폴더를 연결했습니다({copied}개 파일 복사): {target} · {TuningLibrary.Summary}");
    }

    /// <summary>
    /// 아무 곳의 배경 PNG를 배경 루트로 복제해 들여온다 (W48) — "폴더에 채워 넣는" 느낌의
    /// 짧은 길. 파일명이 곧 키이므로 이름 그대로 복사하고, 같은 이름이 있으면 건너뛴다
    /// (덮어쓰면 기존 배경이 소리 없이 바뀐다).
    /// </summary>
    public int ImportBackgrounds(IEnumerable<string> files)
    {
        if (ResolveAssetRoots().Backgrounds is not { } root)
        {
            SetStatus("배경 폴더가 없습니다 — 프로젝트를 저장하면 assets/backgrounds가 준비됩니다.");
            return 0;
        }

        Directory.CreateDirectory(root);
        int copied = 0;
        var skipped = new List<string>();

        foreach (string file in files.Where(item =>
            string.Equals(Path.GetExtension(item), ".png", StringComparison.OrdinalIgnoreCase)))
        {
            string destination = Path.Combine(root, Path.GetFileName(file));

            if (File.Exists(destination))
            {
                skipped.Add(Path.GetFileName(file));
                continue;
            }

            File.Copy(file, destination);
            copied++;
        }

        RefreshAssets();
        SetStatus($"배경 {copied}개를 가져왔습니다." +
            (skipped.Count > 0 ? $" 같은 이름이 있어 건너뜀: {string.Join(", ", skipped)}" : string.Empty));
        return copied;
    }

    /// <summary>
    /// 아무 곳의 PNG를 초상화 규약 자리({캐릭터}/{변형}/{표정 2자리}.png)로 복제해 들여온다
    /// (W48). 표정 번호는 그 폴더의 빈 번호부터 차례로 붙는다 — 이름 규약을 몰라도
    /// 채워 넣는 순서대로 01, 02…가 된다.
    /// </summary>
    public int ImportPortraits(string characterKey, char variant, IEnumerable<string> files)
    {
        if (ResolveAssetRoots().Portraits is not { } root)
        {
            SetStatus("초상화 폴더가 없습니다 — 프로젝트를 저장하면 assets/portraits가 준비됩니다.");
            return 0;
        }

        string key = characterKey.Trim();

        if (key.Length == 0)
        {
            SetStatus("캐릭터 키를 입력해야 초상화를 들여올 수 있습니다.");
            return 0;
        }

        string folder = Path.Combine(root, key, char.ToLowerInvariant(variant).ToString());
        Directory.CreateDirectory(folder);

        // 다음 빈 표정 번호부터 — 이미 01·02가 있으면 03부터 붙는다.
        int next = Directory.EnumerateFiles(folder, "*.png")
            .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file), out int parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        int copied = 0;

        foreach (string file in files.Where(item =>
            string.Equals(Path.GetExtension(item), ".png", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(file, Path.Combine(folder, $"{next + copied:00}.png"));
            copied++;
        }

        RefreshAssets();
        SetStatus(copied > 0
            ? $"{key}/{char.ToLowerInvariant(variant)}에 초상화 {copied}개를 {next:00}번부터 넣었습니다."
            : "가져올 PNG가 없습니다.");
        return copied;
    }

    /// <summary>튜닝이 놓일 규약 자리. 저장 전에는 기준 폴더가 없어 안내만 남기고 null.</summary>
    private string? TuningTargetRoot()
    {
        if (ProjectPath is null)
        {
            SetStatus("튜닝은 프로젝트 폴더 옆에 놓입니다 — 프로젝트를 먼저 저장해 주세요.");
            return null;
        }

        return Path.Combine(
            Path.GetDirectoryName(ProjectPath)!,
            RuntimeTuningLibrary.DefaultFolderName);
    }

    public void RefreshAssets()
    {
        _assetLibrary = null;
        _tuningLibrary = null;
        ImageCache.Clear();
        SetStatus("프리뷰 에셋을 다시 읽었습니다." +
            (TuningLibrary.IsLoaded ? $" · {TuningLibrary.Summary}" : string.Empty));
    }

    private (string? Backgrounds, string? Portraits) ResolveAssetRoots()
    {
        // 저장 전 프로젝트는 기준 디렉터리가 없어 상대 루트를 해석할 수 없다.
        // 절대 경로 루트는 그대로 동작한다.
        string basePath = ProjectPath
            ?? Path.Combine(Environment.CurrentDirectory, "unsaved" + ProjectManifestJson.FileExtension);

        return (
            AssetRootSettings.ResolveFrom(basePath, Project.AssetRoots.BackgroundsPath),
            AssetRootSettings.ResolveFrom(basePath, Project.AssetRoots.PortraitsPath));
    }

    /// <summary>저장한 뒤로 바뀐 것이 있는지. 실제 내용을 비교하므로 되돌리기로 원상복구하면 깨끗해진다.</summary>
    public bool IsDirty => !string.Equals(_savedSnapshot, ProjectSnapshotCodec.Encode(Project), StringComparison.Ordinal);

    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary>
    /// 상태줄 글자가 바뀌었을 때. <b><see cref="Changed"/>와 갈라 둔 이유가 성능이다</b>
    /// (2026-08-18): 예전에는 <see cref="SetStatus"/>가 프로젝트 변경으로 방송돼서,
    /// "에피소드 3개를 반영했습니다" 한 줄이 챕터 전체 재읽기 + 판 재구성을
    /// 불렀다. 동기화가 상태를 여러 번 적으면 그만큼 되풀이됐고, 그 되풀이가 다시
    /// 상태를 적어 스스로를 먹였다 — 노드 60개에서 첫 화면이 58초 걸리던 정체다.
    ///
    /// 상태줄은 프로젝트가 아니다. 듣는 쪽은 글자만 갈아 끼운다.
    /// </summary>
    public event EventHandler? StatusChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// <see cref="Definition"/>이 갈렸을 때 — 기획자의 어휘가 바뀌었다는 알림이다.
    ///
    /// ⚠ <b>프로젝트 변경(<see cref="Changed"/>)과 갈라 둔다.</b> 정의 파일은 프로젝트
    /// 바깥에 살고, 저것으로 방송하면 챕터 재읽기·판 재구성이 딸려 온다(2026-08-18
    /// 성능 항목의 그 고리다). 듣는 쪽은 <b>제가 그리는 목록만</b> 다시 세운다.
    /// </summary>

    /// <summary>
    /// 현재 파일 또는 그래프 펼침 상태가 바뀌었을 때.
    /// StoryProject 변경이 아니므로 <see cref="Changed"/>와 분리한다.
    /// </summary>
    public event EventHandler<FileGraphStateChangedEventArgs>? FileGraphStateChanged;

    public void Select(string? nodeId)
    {
        if (string.Equals(SelectedNodeId, nodeId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedNodeId = nodeId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // 상태를 모두 맞춘 뒤에 알린다. 알린 다음에 상태를 바꾸면
    // 화면은 방금 지나간 값을 그린 채로 남는다.

    /// <summary>
    /// 원고 manifest나 이 툴의 파일을 연다 — 둘을 합쳐 한 프로젝트로(<see cref="CompositionStore.Load"/>).
    /// ⚠ 원고는 <b>맞추지 않는다</b> — 카드 없는 에피소드에 카드를 세우는 일 같은 원고 쓰기는 director의 몫이다.
    /// </summary>
    public void Open(string path)
    {
        CompositionLoadResult loaded = CompositionStore.Load(path);
        StoryProject project = loaded.Project;

        ManuscriptPath = loaded.ManuscriptPath;
        ProjectPath = loaded.ComposePath;
        Definition = GameDefinition.LoadBeside(loaded.ManuscriptPath);
        BranchSelection.Clear();
        SimulationValues.Clear();
        _savedSnapshot = ProjectSnapshotCodec.Encode(project);
        _tuningLibrary = null;
        StatusMessage =
            $"{Path.GetFileName(ManuscriptPath)} · 대본 {project.Scripts.Count}개 · " +
            $"노드 {project.EnumerateNodes().Count()}개 · " +
            $"발행 결과 {project.Results.DialogueResults.Count + project.Results.PresentationResults.Count}개" +
            (TuningLibrary.IsLoaded ? $" · {TuningLibrary.Summary}" : string.Empty) +
            SourceNote(loaded);

        AppSettingsService.SaveRecentProject(loaded.ManuscriptPath);

        string? activeFileId = project.FindFileContainingNode(project.StartNodeId)?.Id
            ?? project.Files.FirstOrDefault()?.Id;
        ResetFileGraphState(project, activeFileId);

        Editor.Replace(project);
        Select(project.StartNodeId ?? project.EnumerateNodes().FirstOrDefault()?.Id);
        RaiseFileGraphStateChanged(
            activeFileChanged: true,
            fileListChanged: true);
    }

    /// <summary>열었을 때 연출이 어디서 왔는지 한 마디 — 옛 프로젝트에서 가져왔으면 저장이 옮긴다는 것까지.</summary>
    private static string SourceNote(CompositionLoadResult loaded)
    {
        string compose = Path.GetFileName(loaded.ComposePath);
        string note = loaded.Source switch
        {
            CompositionSource.EmbeddedInManuscript =>
                $" · 프로젝트 파일 안의 연출을 가져왔습니다 — 저장하면 {compose}에 따로 남고, 프로젝트 파일은 건드리지 않습니다",
            CompositionSource.PresentationOriginal =>
                $" · 옛 원본({CompositionStore.PresentationBackupFolder})에서 연출을 가져왔습니다 — 저장하면 {compose}에 남습니다",
            _ => string.Empty
        };

        return loaded.DroppedLinks > 0
            ? note + $" · 원고에서 사라진 노드를 가리키던 연출 배선 {loaded.DroppedLinks}개를 버렸습니다"
            : note;
    }

    /// <summary>
    /// 연출 층을 이 툴의 파일에 쓴다(<see cref="CompositionStore.Save"/>) — <b>원고 파일은 쓰지 않는다</b>.
    /// 원고를 아직 안 열었으면 쓸 자리가 없다: 이 툴의 파일은 원고 옆에 산다.
    /// </summary>
    public void Save()
    {
        if (ManuscriptPath is null || ProjectPath is null)
        {
            throw new InvalidOperationException(NoManuscriptMessage);
        }

        bool firstSave = !File.Exists(ProjectPath);

        // 첫 저장 (W48): 에셋 폴더 규약을 미리 심는다 — 루트가 저장본에 들어가야 하므로 디스크에 쓰기 전에
        // 지정한다. 오디오도 같은 구조 (W59).
        if (firstSave && Project.AssetRoots.IsEmpty)
        {
            Editor.SetAssetRoots("assets/backgrounds", "assets/portraits", "assets/bgm", "assets/sfx");
        }

        CompositionStore.Save(ProjectPath, ManuscriptPath, Project);

        _savedSnapshot = ProjectSnapshotCodec.Encode(Project);
        Definition = GameDefinition.LoadBeside(ManuscriptPath);
        AppSettingsService.SaveRecentProject(ManuscriptPath);

        bool provisioned = EnsureProjectFolders();

        StatusMessage = $"{Path.GetFileName(ProjectPath)}에 저장했습니다." +
            (provisioned ? " 없던 에셋 폴더·기본 튜닝을 준비했습니다." : string.Empty);
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Content));
    }

    /// <summary>
    /// 프로젝트 폴더의 기본 살림 (W48·W60) — <b>매 저장마다</b> 없는 에셋 폴더를 만들고,
    /// 튜닝 폴더가 통째로 없으면 기본 튜닝을 깐다. 첫 저장뿐 아니라 다른 이름으로 저장한
    /// 새 위치, 실수로 폴더를 지운 경우까지 복구한다. 내용이 있는 폴더는 불가침이다.
    /// 무언가 실제로 만들었을 때만 true — 조용한 저장이 시끄러워지지 않는다.
    /// </summary>
    private bool EnsureProjectFolders()
    {
        if (ProjectPath is null)
        {
            return false;
        }

        bool created = false;

        foreach (string? root in new[] { BackgroundsRoot, PortraitsRoot, BgmRoot, SfxRoot })
        {
            if (root is not null && !Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                created = true;
            }
        }

        // 정의 파일 — 없으면 뼈대를 깐다. 손으로 만들어야 하는 파일이면 새 프로젝트마다
        // 스탯 경고부터 맞는다(실사례). 있는 파일은 불가침.
        if (GameDefinitionStore.EnsureBeside(ProjectPath))
        {
            created = true;
        }

        // 기본 튜닝 — 재지정(runtimeTuningPath)이 없고 규약 폴더가 비었을 때만.
        if (string.IsNullOrWhiteSpace(Definition.RuntimeTuningPath))
        {
            string tuningRoot = Path.Combine(
                Path.GetDirectoryName(ProjectPath)!, RuntimeTuningLibrary.DefaultFolderName);

            if (!Directory.Exists(tuningRoot) || !Directory.EnumerateFileSystemEntries(tuningRoot).Any())
            {
                CreateDefaultTuning(); // 상태 메시지는 저장 요약이 덮는다
                created = true;
            }
        }

        if (created)
        {
            RefreshAssets();
        }

        return created;
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectFile(string? fileId)
    {
        if (fileId is not null && Project.FindFile(fileId) is null)
        {
            return;
        }

        if (string.Equals(ActiveFileId, fileId, StringComparison.Ordinal))
        {
            return;
        }

        ActiveFileId = fileId;

        RaiseFileGraphStateChanged(
            activeFileChanged: true,
            fileListChanged: false);
    }

    private void OnEditorChanged(object? sender, ProjectChangedEventArgs e)
    {
        bool activeChanged = false;
        bool fileListChanged = false;

        if (Project.FindFile(ActiveFileId) is null)
        {
            ActiveFileId = Project.Files.FirstOrDefault()?.Id;
            activeChanged = true;
        }

        string nextFileListSignature = BuildFileListSignature(Project);

        if (!string.Equals(_fileListSignature, nextFileListSignature, StringComparison.Ordinal))
        {
            _fileListSignature = nextFileListSignature;
            fileListChanged = true;
        }

        // 선택하고 있던 노드가 사라졌다면 선택을 놓는다.
        if (SelectedNodeId is not null && Project.FindNode(SelectedNodeId) is null)
        {
            SelectedNodeId = Project.EnumerateNodes().FirstOrDefault()?.Id;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, e);

        if (activeChanged || fileListChanged)
        {
            RaiseFileGraphStateChanged(activeChanged, fileListChanged);
        }
    }

    private void ResetFileGraphState(StoryProject project, string? activeFileId = null)
    {
        ActiveFileId = activeFileId ?? project.Files.FirstOrDefault()?.Id;
        _fileListSignature = BuildFileListSignature(project);
    }

    private void RaiseFileGraphStateChanged(bool activeFileChanged, bool fileListChanged)
    {
        FileGraphStateChanged?.Invoke(
            this,
            new FileGraphStateChangedEventArgs(activeFileChanged, fileListChanged));
    }

    private static string BuildFileListSignature(StoryProject project)
    {
        return string.Join(
            "\n",
            project.Files.Select(file =>
                $"{file.Id}\u001f{file.Name}\u001f{file.RelativePath}\u001f{file.Nodes.Count}"));
    }

    private static StoryProject NewProjectInstance()
    {
        // 대본을 미리 만들지 않는다 (X4, D-3) — 대사 노드가 생성될 때 전용 대본이
        // 함께 태어나므로, 미리 둔 대본은 아무도 안 읽는 빈 파일로 남을 뿐이다.
        var project = new StoryProject { Title = "새 프로젝트" };
        project.Files.Add(new StoryFile(name: "기본 파일"));
        return project;
    }
}

internal sealed class FileGraphStateChangedEventArgs : EventArgs
{
    public FileGraphStateChangedEventArgs(bool activeFileChanged, bool fileListChanged)
    {
        ActiveFileChanged = activeFileChanged;
        FileListChanged = fileListChanged;
    }

    public bool ActiveFileChanged { get; }

    /// <summary>파일 이름·경로·순서 또는 소유 노드 개수가 바뀌어 목록 행을 다시 만들어야 하는가.</summary>
    public bool FileListChanged { get; }
}
