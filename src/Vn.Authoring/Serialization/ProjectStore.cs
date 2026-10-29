using System.Text;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Serialization;

/// <summary>
/// manifest·StoryFile·발행 결과를 물리 파일로 읽고 쓴다.
/// StoryProject 자체는 경로 해석이나 파일 IO를 모른다.
///
/// 디스크 배치(원고 형식 4):
/// <code>
/// project.vnproject.json      목차와 파일을 넘나드는 관계
/// story/&lt;fileId&gt;.vnstory.json       판 — 카드와 그 대본(줄 정체성 + locale별 본문)
/// results.vnresults.json      발행된 불변 결과
/// </code>
///
/// ⛔ 원고 형식 3까지는 대본이 <c>script/&lt;scriptId&gt;.vnscript.json</c>에 따로 있었다. 그런 원고도 읽는다 — 카드마다
/// 가리키던 대본을 꽂는다(<see cref="ScriptTable"/>). 옮겨 쓰는 것은 원고의 주인인 director다.
///
/// ⚠ 원고를 쓰는 것은 director다. 여기 <see cref="Save"/>는 테스트가 원고를 꾸밀 때만 쓴다(형식 4로 쓴다).
/// </summary>
public static class ProjectStore
{
    public static ProjectLoadResult Load(string path)
    {
        string openedPath = Path.GetFullPath(path);
        string json = File.ReadAllText(openedPath, new UTF8Encoding(false));
        JsonObject root = JsonSupport.ParseObject(json, "VnTool 프로젝트");

        if (LooksLikeStandaloneStoryFile(root))
        {
            throw new InvalidDataException(
                "이 파일은 프로젝트가 소유하는 StoryFile입니다. " +
                $"개별 {StoryFileJson.FileExtension} 대신 {ProjectManifestJson.FileExtension} manifest를 여세요.");
        }

        if (LooksLikeStandaloneScript(root))
        {
            throw new InvalidDataException(
                "이 파일은 프로젝트가 소유하는 대본입니다. " +
                $"개별 {ScriptDocumentJson.LegacyFileExtension} 대신 {ProjectManifestJson.FileExtension} manifest를 여세요.");
        }

        StoryProject project = LoadManifest(openedPath, ProjectManifestJson.Read(json));
        return new ProjectLoadResult(project, openedPath);
    }

    public static void Save(string manifestPath, StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSupport.ValidateProject(project);

        string fullManifestPath = Path.GetFullPath(manifestPath);
        string rootDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("프로젝트 manifest 디렉터리를 찾을 수 없습니다.");
        Directory.CreateDirectory(rootDirectory);

        // 직렬화와 경로 검증은 디스크를 건드리기 전에 모두 끝낸다.
        // 한 파일의 데이터가 잘못됐다는 이유로 앞 파일만 새 내용으로 바뀌는 일을 줄인다.
        var writes = new List<PendingWrite>();

        foreach (StoryFile file in project.Files)
        {
            writes.Add(new PendingWrite(
                Resolve(rootDirectory, file.RelativePath, StoryFileJson.FileExtension),
                StoryFileJson.Write(file)));
        }

        if (!project.Results.IsEmpty)
        {
            writes.Add(new PendingWrite(
                Path.Combine(rootDirectory, ResultStoreJson.DefaultFileName),
                ResultStoreJson.Write(project.Results)));
        }

        string manifestText = ProjectManifestJson.Write(project);

        // 부속 파일을 먼저 안전하게 교체한 뒤 manifest를 마지막에 교체한다.
        // 새 manifest가 아직 쓰이지 않은 파일을 가리키는 상태를 만들지 않기 위해서다.
        foreach (PendingWrite write in writes)
        {
            JsonSupport.WriteAtomic(write.Path, write.Text);
        }

        JsonSupport.WriteAtomic(fullManifestPath, manifestText);
    }

    public static string DefaultRelativePath(string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return $"story/{fileId}{StoryFileJson.FileExtension}";
    }

    public static string NormalizeRelativeStoryPath(string relativePath) =>
        NormalizeRelativePath(relativePath, StoryFileJson.FileExtension);

    /// <summary>
    /// 프로젝트 디렉터리 안의 상대 경로로 정규화한다.
    /// 프로젝트 밖을 가리킬 수 있는 경로는 읽지 않는다. manifest 하나로 임의의 파일을
    /// 덮어쓸 수 있게 되면 프로젝트를 여는 일 자체가 위험해진다.
    /// </summary>
    public static string NormalizeRelativePath(string relativePath, string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("부속 파일 상대 경로가 비어 있습니다.");
        }

        string normalized = relativePath.Replace('\\', '/').Trim();

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"부속 파일 경로 '{relativePath}'는 상대 경로여야 합니다.");
        }

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"부속 파일 경로 '{relativePath}'가 프로젝트 밖을 가리킬 수 있습니다.");
        }

        normalized = string.Join('/', parts);

        if (!normalized.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"부속 파일 경로 '{relativePath}'는 {expectedExtension}으로 끝나야 합니다.");
        }

        return normalized;
    }

    private static StoryProject LoadManifest(string manifestPath, ProjectManifest manifest)
    {
        string rootDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("프로젝트 manifest 디렉터리를 찾을 수 없습니다.");
        var project = new StoryProject
        {
            FormatVersion = StoryProject.CurrentFormatVersion,
            Title = manifest.Title,
            StartNodeId = manifest.StartNodeId,
            AssetRoots = manifest.AssetRoots.Clone(),
            RecentCommandIds = manifest.RecentCommandIds.ToList(),
            QuickCommands = manifest.QuickCommands?.Select(chip => chip.Copy()).ToList(),
            ExportFormats = manifest.ExportFormats.Clone(),
            OutputPath = manifest.OutputPath
        };

        // 원고 형식 3 — 따로 선 대본 파일을 읽어 카드에 꽂는다(형식 4는 대본이 판 파일 안에 있어 목록이 비었다).
        // 어느 카드도 안 가리키는 대본은 받아 가는 카드가 없으니 여기서 버려진다.
        var table = new ScriptTable();

        foreach (ScriptFileReference reference in manifest.LegacyScripts)
        {
            string scriptPath = Resolve(rootDirectory, reference.RelativePath, ScriptDocumentJson.LegacyFileExtension);

            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException(
                    $"프로젝트가 참조하는 대본을 찾을 수 없습니다: {reference.RelativePath}",
                    scriptPath);
            }

            (string id, ScriptDocument script) = ScriptDocumentJson.ReadLegacyFile(
                File.ReadAllText(scriptPath, new UTF8Encoding(false)));

            if (!string.Equals(id, reference.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"manifest의 대본 Id '{reference.Id}'와 파일의 Id '{id}'가 다릅니다.");
            }

            table.Add(id, script);
        }

        foreach (ProjectStoryFileReference reference in manifest.Files)
        {
            string storyPath = Resolve(rootDirectory, reference.RelativePath, StoryFileJson.FileExtension);

            if (!File.Exists(storyPath))
            {
                throw new FileNotFoundException(
                    $"프로젝트가 참조하는 StoryFile을 찾을 수 없습니다: {reference.RelativePath}",
                    storyPath);
            }

            StoryFile file = StoryFileJson.Read(File.ReadAllText(storyPath, new UTF8Encoding(false)), table);

            if (!string.Equals(file.Id, reference.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"manifest의 StoryFile Id '{reference.Id}'와 파일의 Id '{file.Id}'가 다릅니다.");
            }

            if (!string.Equals(file.Name, reference.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"manifest의 StoryFile 이름 '{reference.Name}'과 파일의 이름 '{file.Name}'이 다릅니다.");
            }

            file.RelativePath = reference.RelativePath;
            project.Files.Add(file);
        }

        if (manifest.ResultsRelativePath is { } resultsRelative)
        {
            string resultsPath = Resolve(rootDirectory, resultsRelative, ResultStoreJson.FileExtension);

            if (!File.Exists(resultsPath))
            {
                throw new FileNotFoundException(
                    $"프로젝트가 참조하는 발행 결과 파일을 찾을 수 없습니다: {resultsRelative}",
                    resultsPath);
            }

            ResultRepository results = ResultStoreJson.Read(
                File.ReadAllText(resultsPath, new UTF8Encoding(false)));

            foreach (DialogueResult result in results.DialogueResults)
            {
                project.Results.Add(result);
            }

            foreach (PresentationResult result in results.PresentationResults)
            {
                project.Results.Add(result);
            }
        }

        project.Links.AddRange(manifest.Links.Select(link => link.Clone()));

        // ⚠ 복제하지 않는다 — 리더가 부를 때마다 새 객체를 만들고 매니페스트는 재사용되지
        //    않는다(WriterSpeaker는 가변이라 복제한다).
        project.Chapters.AddRange(manifest.Chapters);
        project.WriterSpeakers.AddRange(manifest.WriterSpeakers.Select(speaker => speaker.Clone()));
        project.EaseCurves.AddRange(manifest.EaseCurves.Select(curve => curve.Clone()));
        project.Compositions.AddRange(manifest.Compositions.Select(item => item.Clone()));

        JsonSupport.ValidateProject(project);
        return project;
    }

    private static bool LooksLikeStandaloneStoryFile(JsonObject root)
    {
        // 버전은 보지 않는다 — 형식 3의 판 파일(1)도 4의 것(2)도 manifest가 아니다. manifest에는 fileId가 없다.
        return root["fileId"] is not null &&
            root["nodes"] is JsonArray;
    }

    private static bool LooksLikeStandaloneScript(JsonObject root)
    {
        return root["scriptId"] is not null &&
            root["lines"] is JsonArray;
    }

    private sealed record PendingWrite(string Path, string Text);

    private static string Resolve(string rootDirectory, string relativePath, string expectedExtension)
    {
        string normalized = NormalizeRelativePath(relativePath, expectedExtension);
        string fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(
            fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"부속 파일 경로 '{relativePath}'가 프로젝트 디렉터리 밖을 가리킵니다.");
        }

        return combined;
    }
}

public sealed record ProjectLoadResult(StoryProject Project, string ManifestPath);
