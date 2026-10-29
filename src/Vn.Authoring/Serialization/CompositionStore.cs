using System.Text;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Serialization;

/// <summary>
/// <b>이 툴의 파일</b> — 원고 위에 얹는 연출 층 (2026-10-29, <c>docs/plans/compositor-split.md</c> K3 · C2).
///
/// 원고(챕터 · 판 · 대사 노드 · 대본 · 조건)의 주인은 <c>ked-story-director</c>이고 그 파일은 <b>읽기만</b> 한다.
/// 연출 노드 · 명령 공급 노드 · 연출 배선 · 발행 결과 · 결과 조합 · 곡선 · 빠른 명령 · 최근 명령 · 에셋 폴더 ·
/// 내보내기 양식 · 출력 폴더는 원고 옆의 이 파일(<c>{원고 이름}.vncompose.json</c>)에 산다 — 두 편집기가 한 파일을
/// 쓰지 않는다.
///
/// <code>
/// 예제.vnproject.json        원고 — director가 쓴다
/// script/ · story/            원고 — director가 쓴다
/// 예제.vncompose.json        연출 층 — 이 툴이 쓴다
/// results.vnresults.json      발행 결과 — 이 툴이 쓴다
/// </code>
///
/// 메모리에서는 둘을 한 <see cref="StoryProject"/>로 합쳐 엔진(무대 · 발행 · 이미터)이 그대로 돈다.
/// 저장은 연출 층만 떼어 이 파일에 쓰고, 원고 파일은 건드리지 않는다.
///
/// <b>옛 프로젝트</b>(연출이 아직 원고 파일 안에 있던 VnTool · director 판)에서는 연출을 <b>가져온다</b>:
/// 이 파일이 없고 원고에 연출이 들어 있으면 그것을, 원고에 없으면 director가 남긴
/// <c>presentation-original/</c>의 원본에서 꺼낸다. 첫 저장이 이 파일에 남기고, 원고는 그대로다.
/// </summary>
public static class CompositionStore
{
    public const int CurrentFormatVersion = 1;
    public const string FileExtension = ".vncompose.json";

    /// <summary>director가 연출을 담은 옛 원본을 남기는 폴더(director <c>ProjectStore.PresentationBackupFolder</c>).</summary>
    public const string PresentationBackupFolder = "presentation-original";

    /// <summary>원고 manifest 옆의 연출 층 파일 자리 — 이름이 같고 확장자만 다르다.</summary>
    public static string PathFor(string manuscriptPath)
    {
        string full = Path.GetFullPath(manuscriptPath);
        string directory = Path.GetDirectoryName(full)!;
        string name = Path.GetFileName(full);

        string baseName = name.EndsWith(ProjectManifestJson.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^ProjectManifestJson.FileExtension.Length]
            : Path.GetFileNameWithoutExtension(name);

        return Path.Combine(directory, baseName + FileExtension);
    }

    /// <summary>
    /// 원고 manifest(<c>.vnproject.json</c>)나 이 파일(<c>.vncompose.json</c>)을 받아 둘을 합쳐 연다.
    /// </summary>
    public static CompositionLoadResult Load(string path)
    {
        string full = Path.GetFullPath(path);
        string manuscriptPath = full.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
            ? ManuscriptOf(full)
            : full;
        string composePath = PathFor(manuscriptPath);

        StoryProject loaded = ProjectStore.Load(manuscriptPath).Project;
        PresentationLayer embedded = PresentationLayer.Extract(loaded);

        PresentationLayer layer;
        CompositionSource source;

        if (File.Exists(composePath))
        {
            // 이 툴의 파일이 있으면 그것이 연출이다 — 원고 안에 옛 연출이 남아 있어도 읽지 않는다.
            layer = ReadLayer(composePath);
            source = CompositionSource.ComposeFile;
        }
        else if (!embedded.IsEmpty)
        {
            layer = embedded;
            source = CompositionSource.EmbeddedInManuscript;
        }
        else if (BackupManifestFor(manuscriptPath) is { } backup)
        {
            layer = PresentationLayer.Extract(ProjectStore.Load(backup).Project);
            source = layer.IsEmpty ? CompositionSource.None : CompositionSource.PresentationOriginal;
        }
        else
        {
            layer = PresentationLayer.Empty();
            source = CompositionSource.None;
        }

        StoryProject merged = PresentationLayer.Merge(loaded, layer, out int droppedLinks);
        JsonSupport.ValidateProject(merged);

        return new CompositionLoadResult(merged, manuscriptPath, composePath, source, droppedLinks);
    }

    /// <summary>연출 층만 떼어 이 파일(과 발행 결과 파일)에 쓴다. 원고 파일은 건드리지 않는다.</summary>
    public static void Save(string composePath, string manuscriptPath, StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        string fullCompose = Path.GetFullPath(composePath);
        string directory = Path.GetDirectoryName(fullCompose)!;
        Directory.CreateDirectory(directory);

        PresentationLayer layer = PresentationLayer.Extract(project);

        // 발행 결과를 먼저 쓴다 — 이 파일이 아직 쓰이지 않은 결과 파일을 가리키는 상태를 만들지 않는다.
        if (!layer.Results.IsEmpty)
        {
            JsonSupport.WriteAtomic(
                Path.Combine(directory, ResultStoreJson.DefaultFileName),
                ResultStoreJson.Write(layer.Results));
        }

        JsonSupport.WriteAtomic(fullCompose, WriteLayer(layer, directory, manuscriptPath));
    }

    private static string ManuscriptOf(string composePath)
    {
        JsonObject root = JsonSupport.ParseObject(
            File.ReadAllText(composePath, new UTF8Encoding(false)), "연출 층 파일");
        string relative = (string?)root["manuscript"]
            ?? throw new InvalidDataException("연출 층 파일에 원고(manuscript) 자리가 없습니다.");

        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(composePath)!, relative));
    }

    /// <summary>director가 남긴 옛 원본의 manifest — 같은 이름으로 <c>presentation-original/</c>에 있다.</summary>
    private static string? BackupManifestFor(string manuscriptPath)
    {
        string candidate = Path.Combine(
            Path.GetDirectoryName(manuscriptPath)!, PresentationBackupFolder, Path.GetFileName(manuscriptPath));

        return File.Exists(candidate) ? candidate : null;
    }

    private static string WriteLayer(PresentationLayer layer, string directory, string manuscriptPath)
    {
        var nodes = new JsonArray();

        foreach (PresentationLayer.PlacedNode placed in layer.Nodes)
        {
            JsonObject node = StoryNodeJson.Write(placed.Node);
            node["board"] = placed.BoardId;
            nodes.Add(node);
        }

        var root = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["manuscript"] = Path.GetRelativePath(directory, Path.GetFullPath(manuscriptPath)).Replace('\\', '/'),
            ["nodes"] = nodes
        };

        if (layer.Links.Count > 0)
        {
            root["links"] = new JsonArray(layer.Links.Select(link => (JsonNode)ProjectManifestJson.WriteLink(link)).ToArray());
        }

        if (layer.Compositions.Count > 0)
        {
            root["compositions"] = new JsonArray(
                layer.Compositions.Select(item => (JsonNode)ProjectManifestJson.WriteComposition(item)).ToArray());
        }

        if (ProjectManifestJson.WriteEaseCurves(layer.EaseCurves) is { } curves)
        {
            root["easeCurves"] = curves;
        }

        if (ProjectManifestJson.WriteQuickCommands(layer.QuickCommands) is { } quick)
        {
            root["quickCommands"] = quick;
        }

        if (layer.RecentCommandIds.Count > 0)
        {
            root["recentCommands"] = new JsonArray(layer.RecentCommandIds.Select(id => (JsonNode)id).ToArray());
        }

        if (ProjectManifestJson.WriteAssetRoots(layer.AssetRoots) is { } assetRoots)
        {
            root["assetRoots"] = assetRoots;
        }

        if (ProjectManifestJson.WriteExportFormats(layer.ExportFormats) is { } formats)
        {
            root["exportFormats"] = formats;
        }

        if (AssetRootSettings.NormalizePath(layer.OutputPath) is { } outputPath)
        {
            root["outputPath"] = outputPath;
        }

        if (!layer.Results.IsEmpty)
        {
            root["results"] = ResultStoreJson.DefaultFileName;
        }

        return JsonSupport.ToDeterministicText(root);
    }

    private static PresentationLayer ReadLayer(string composePath)
    {
        JsonObject root = JsonSupport.ParseObject(
            File.ReadAllText(composePath, new UTF8Encoding(false)), "연출 층 파일");
        int version = (int?)root["formatVersion"] ?? 0;

        if (version != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"연출 층 파일 형식 버전 {version}은 지원하지 않습니다. 현재 버전은 {CurrentFormatVersion}입니다.");
        }

        PresentationLayer layer = PresentationLayer.Empty();

        foreach (JsonObject node in root["nodes"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            string board = (string?)node["board"] ?? string.Empty;
            layer.Nodes.Add(new PresentationLayer.PlacedNode(board, StoryNodeJson.Read(node)));
        }

        foreach (JsonObject link in root["links"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            layer.Links.Add(ProjectManifestJson.ReadLink(link));
        }

        foreach (JsonObject composition in root["compositions"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            layer.Compositions.Add(ProjectManifestJson.ReadComposition(composition));
        }

        layer.EaseCurves.AddRange(ProjectManifestJson.ReadEaseCurves(root["easeCurves"]));
        layer.QuickCommands = ProjectManifestJson.ReadQuickCommands(root["quickCommands"]);
        layer.RecentCommandIds.AddRange(ProjectManifestJson.ReadRecentCommands(root["recentCommands"]));
        layer.AssetRoots = ProjectManifestJson.ReadAssetRoots(root["assetRoots"]);
        layer.ExportFormats = ProjectManifestJson.ReadExportFormats(root["exportFormats"]);
        layer.OutputPath = AssetRootSettings.NormalizePath((string?)root["outputPath"]);

        if ((string?)root["results"] is { Length: > 0 } results)
        {
            string resultsPath = Path.Combine(
                Path.GetDirectoryName(composePath)!,
                ProjectStore.NormalizeRelativePath(results, ResultStoreJson.FileExtension).Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(resultsPath))
            {
                throw new FileNotFoundException(
                    $"연출 층 파일이 참조하는 발행 결과 파일을 찾을 수 없습니다: {results}", resultsPath);
            }

            ResultRepository repository = ResultStoreJson.Read(File.ReadAllText(resultsPath, new UTF8Encoding(false)));

            foreach (DialogueResult result in repository.DialogueResults)
            {
                layer.Results.Add(result);
            }

            foreach (PresentationResult result in repository.PresentationResults)
            {
                layer.Results.Add(result);
            }
        }

        return layer;
    }
}

/// <summary>열었을 때 연출이 어디서 왔는가.</summary>
public enum CompositionSource
{
    /// <summary>연출이 없다 — 이 원고는 아직 아무도 연출하지 않았다.</summary>
    None,

    /// <summary>이 툴의 파일(<c>.vncompose.json</c>)에서.</summary>
    ComposeFile,

    /// <summary>원고 파일 안에서 — 연출이 아직 원고에 있던 옛 프로젝트. 첫 저장이 이 툴의 파일로 옮긴다.</summary>
    EmbeddedInManuscript,

    /// <summary>director가 남긴 옛 원본(<c>presentation-original/</c>)에서.</summary>
    PresentationOriginal
}

/// <param name="ManuscriptPath">원고 manifest — 읽기만 한다.</param>
/// <param name="ComposePath">이 툴의 파일 — 아직 없을 수 있다(첫 저장이 만든다).</param>
/// <param name="DroppedLinks">원고에서 사라진 노드를 가리켜 버린 연출 배선 수.</param>
public sealed record CompositionLoadResult(
    StoryProject Project,
    string ManuscriptPath,
    string ComposePath,
    CompositionSource Source,
    int DroppedLinks);

/// <summary>
/// 한 <see cref="StoryProject"/>에서 <b>연출 층</b>만 떼거나, 원고에 다시 얹는다.
/// 원고 = 챕터 · 판 · 대사 · 설정 노드 · 대본 · 조건 공급 배선 · 작가 화자. 그 밖이 연출 층이다.
/// </summary>
public sealed class PresentationLayer
{
    /// <summary>연출 층의 노드 하나와 그것이 서 있던 판.</summary>
    public sealed record PlacedNode(string BoardId, StoryNode Node);

    public List<PlacedNode> Nodes { get; } = new();

    public List<NodeLink> Links { get; } = new();

    public List<RuntimeComposition> Compositions { get; } = new();

    public ResultRepository Results { get; } = new();

    public List<EaseCurve> EaseCurves { get; } = new();

    public List<StageQuickCommand>? QuickCommands { get; set; }

    public List<string> RecentCommandIds { get; } = new();

    public AssetRootSettings AssetRoots { get; set; } = new();

    public ExportFormatSelection ExportFormats { get; set; } = new();

    public string? OutputPath { get; set; }

    /// <summary>연출이 하나도 없다 — 노드 · 배선 · 결과 · 곡선 · 빠른 명령 · 설정 모두.</summary>
    public bool IsEmpty =>
        Nodes.Count == 0 && Links.Count == 0 && Compositions.Count == 0 && Results.IsEmpty &&
        EaseCurves.Count == 0 && QuickCommands is null && RecentCommandIds.Count == 0 &&
        AssetRoots.IsEmpty && ExportFormats.IsDefault && OutputPath is null;

    public static PresentationLayer Empty() => new();

    public static bool IsPresentationNode(StoryNode node) => node is PresentationNode or CommandSupplyNode;

    public static bool IsPresentationLink(NodeLink link) =>
        link.Kind is NodeLinkKind.CommandSupply or NodeLinkKind.PresentationSupply;

    /// <summary>연출 층을 복제해 떼어 낸다. 받은 프로젝트는 바꾸지 않는다.</summary>
    public static PresentationLayer Extract(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var layer = new PresentationLayer
        {
            QuickCommands = project.QuickCommands?.Select(chip => chip.Copy()).ToList(),
            AssetRoots = project.AssetRoots.Clone(),
            ExportFormats = project.ExportFormats.Clone(),
            OutputPath = project.OutputPath
        };

        foreach (StoryFile file in project.Files)
        {
            foreach (StoryNode node in file.Nodes.Where(IsPresentationNode))
            {
                layer.Nodes.Add(new PlacedNode(file.Id, node.Clone()));
            }
        }

        layer.Links.AddRange(project.Links.Where(IsPresentationLink).Select(link => link.Clone()));
        layer.Compositions.AddRange(project.Compositions.Select(item => item.Clone()));
        layer.EaseCurves.AddRange(project.EaseCurves.Select(curve => curve.Clone()));
        layer.RecentCommandIds.AddRange(project.RecentCommandIds);

        foreach (DialogueResult result in project.Results.DialogueResults)
        {
            layer.Results.Add(result);
        }

        foreach (PresentationResult result in project.Results.PresentationResults)
        {
            layer.Results.Add(result);
        }

        return layer;
    }

    /// <summary>
    /// 원고(받은 프로젝트의 연출 층은 버린다)에 연출 층을 얹은 새 프로젝트.
    ///
    /// ⚠ <b>판이 없어졌어도 연출을 버리지 않는다</b> — director가 판을 옮기거나 이름을 바꿨을 수 있다. 연출 노드는
    /// 공급하는 대사 노드가 사는 판에, 명령 공급 노드는 공급받는 연출 노드의 판에 다시 선다. 그래도 자리가 없으면
    /// 첫 판에 선다. 끝이 원고에서 사라진 배선만 버린다(<paramref name="droppedLinks"/>).
    /// </summary>
    public static StoryProject Merge(StoryProject manuscript, PresentationLayer layer, out int droppedLinks)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        ArgumentNullException.ThrowIfNull(layer);

        var merged = new StoryProject
        {
            FormatVersion = manuscript.FormatVersion,
            Title = manuscript.Title,
            StartNodeId = manuscript.StartNodeId,
            AssetRoots = layer.AssetRoots.Clone(),
            RecentCommandIds = new List<string>(layer.RecentCommandIds),
            QuickCommands = layer.QuickCommands?.Select(chip => chip.Copy()).ToList(),
            ExportFormats = layer.ExportFormats.Clone(),
            OutputPath = layer.OutputPath,
            Scripts = manuscript.Scripts.Select(script => script.Clone()).ToList(),
            Files = manuscript.Files.Select(file =>
            {
                StoryFile copy = file.Clone();
                copy.Nodes.RemoveAll(IsPresentationNode);
                return copy;
            }).ToList(),
            Links = manuscript.Links.Where(link => !IsPresentationLink(link)).Select(link => link.Clone()).ToList(),
            WriterSpeakers = manuscript.WriterSpeakers.Select(speaker => speaker.Clone()).ToList(),
            EaseCurves = layer.EaseCurves.Select(curve => curve.Clone()).ToList(),
            Results = layer.Results.Clone(),
            Compositions = layer.Compositions.Select(item => item.Clone()).ToList()
        };

        merged.Chapters.AddRange(manuscript.Chapters);

        // 연출 노드 먼저(대사 노드의 판을 따라), 명령 공급 노드는 그 뒤(연출 노드의 판을 따라).
        foreach (PlacedNode placed in layer.Nodes.OrderBy(item => item.Node is CommandSupplyNode))
        {
            if (merged.FindNode(placed.Node.Id) is not null)
            {
                continue;   // 같은 Id가 원고에 있다 — 원고가 이긴다
            }

            StoryFile? board = merged.FindFile(placed.BoardId) ?? BoardByLinks(merged, layer, placed.Node.Id)
                ?? merged.Files.FirstOrDefault();

            board?.Nodes.Add(placed.Node.Clone());
        }

        droppedLinks = 0;

        foreach (NodeLink link in layer.Links)
        {
            if (merged.FindNode(link.SourceNodeId) is null || merged.FindNode(link.TargetNodeId) is null)
            {
                droppedLinks++;
                continue;
            }

            merged.Links.Add(link.Clone());
        }

        return merged;
    }

    private static StoryFile? BoardByLinks(StoryProject merged, PresentationLayer layer, string nodeId)
    {
        foreach (NodeLink link in layer.Links.Where(link =>
                     string.Equals(link.SourceNodeId, nodeId, StringComparison.Ordinal)))
        {
            if (merged.FindFileContainingNode(link.TargetNodeId) is { } board)
            {
                return board;
            }
        }

        return null;
    }
}
