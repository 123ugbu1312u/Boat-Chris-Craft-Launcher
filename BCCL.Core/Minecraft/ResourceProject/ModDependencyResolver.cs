using System;
using System.Collections.Generic;
using System.Linq;

namespace BCCL.Core.Minecraft.ResourceProject;

public sealed record class ModDependencyReference
{
    public string ProjectId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? VersionId { get; init; }
    public bool IsRequired { get; init; } = true;
}

public sealed record class ModDependencyRequest
{
    public string TargetMinecraftVersion { get; init; } = string.Empty;
    public List<string> TargetLoaders { get; init; } = [];
    public List<ModDependencyReference> RequiredDependencies { get; init; } = [];
    public List<InstalledModIdentity> InstalledMods { get; init; } = [];
    public Func<string, string, IEnumerable<InstalledModIdentity>>? FindInstalledMods { get; init; }
    public ModDependencyReference? MainFile { get; init; }
    public Func<string, string, ModDependencyProject?> ProjectResolver { get; init; } = (_, _) => null;
}

public sealed record class ModDependencyProject
{
    public string ProjectId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? ProjectName { get; init; }
    public List<ModDependencyFile> Files { get; init; } = [];
}

public sealed record class ModDependencyFile
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Version { get; init; }
    public List<string> GameVersions { get; init; } = [];
    public List<string> Loaders { get; init; } = [];
    public int ReleaseType { get; init; }
    public DateTime ReleaseDate { get; init; }
    public IEnumerable<ModDependencyReference> RequiredDependencies { get; init; } = [];
}

public sealed record class InstalledModIdentity
{
    public string? SourceProjectId { get; init; }
    public string? Source { get; init; }
    public string? ModId { get; init; }
    public string? FileId { get; init; }
    public string? LocalPath { get; init; }
    public string? Hash { get; init; }
    public List<string> GameVersions { get; init; } = [];
    public List<string> Loaders { get; init; } = [];
}

public sealed record class ModDependencyResolutionResult
{
    public List<ResolvedDependencyInstall> ToInstall { get; } = [];
    public List<UnresolvedDependency> Unresolved { get; } = [];
    public List<IgnoredDependency> Satisfied { get; } = [];
    public List<InstalledModIdentity> Mismatched { get; } = [];
}

public sealed record class ResolvedDependencyInstall
{
    public string ProjectId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? ProjectName { get; init; }
    public ModDependencyFile File { get; init; } = new();
}

public sealed record class UnresolvedDependency
{
    public string ProjectId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record class IgnoredDependency
{
    public string ProjectId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class ModDependencyResolver
{
    private const int MaxDepth = 32;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public ModDependencyResolutionResult Resolve(ModDependencyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ProjectResolver);

        var context = new ResolutionContext(request);
        foreach (var dependency in request.RequiredDependencies.OrderByDescending(dep => dep.VersionId is not null))
        {
            ResolveDependency(context, dependency, 0);
        }

        return context.Result;
    }

    private static void ResolveDependency(ResolutionContext context, ModDependencyReference dependency, int depth)
    {
        if (string.IsNullOrWhiteSpace(dependency.ProjectId) || string.IsNullOrWhiteSpace(dependency.Source))
        {
            return;
        }

        if (!dependency.IsRequired)
        {
            context.AddSatisfied(dependency.ProjectId, dependency.Source, "Optional dependency ignored.");
            return;
        }

        if (depth > MaxDepth)
        {
            context.AddUnresolved(dependency.ProjectId, dependency.Source, "Maximum dependency depth exceeded.");
            return;
        }

        var key = context.ProjectKey(dependency.Source, dependency.ProjectId);
        if (context.Selection.TryGetValue(key, out var selectedVersion))
        {
            if (dependency.VersionId is not null && !Comparer.Equals(selectedVersion, dependency.VersionId))
                context.AddUnresolved(dependency.ProjectId, dependency.Source, "Conflicting required dependency versions.");
            return;
        }

        var installed = (context.Request.FindInstalledMods?.Invoke(dependency.Source, dependency.ProjectId)
            ?? context.Request.InstalledMods).Where(mod =>
            Comparer.Equals(mod.SourceProjectId, dependency.ProjectId) && Comparer.Equals(mod.Source, dependency.Source)).ToList();
        var compatible = installed.FirstOrDefault(mod => context.IsInstalledCompatible(mod, dependency.VersionId));
        if (compatible is not null)
        {
            context.Selection[key] = compatible.FileId;
            context.AddSatisfied(dependency.ProjectId, dependency.Source, "Already installed and compatible.");
            return;
        }

        foreach (var mod in installed)
            if (!context.Result.Mismatched.Contains(mod))
                context.Result.Mismatched.Add(mod);

        var project = context.Request.ProjectResolver(dependency.Source, dependency.ProjectId);
        if (project is null)
        {
            context.AddUnresolved(dependency.ProjectId, dependency.Source, "Dependency project was not found.");
            return;
        }

        var selectedFile = SelectBestFile(project.Files, dependency.VersionId, context.TargetMinecraftVersion, context.TargetLoaders);
        if (selectedFile is null)
        {
            context.AddUnresolved(project.ProjectId, project.Source, "No compatible file was found.");
            return;
        }

        context.Selection[key] = selectedFile.Id;
        context.AddInstall(project, selectedFile);

        foreach (var nestedDependency in selectedFile.RequiredDependencies.OrderByDescending(dep => dep.VersionId is not null))
        {
            ResolveDependency(context, nestedDependency, depth + 1);
        }
    }

    private static ModDependencyFile? SelectBestFile(
        IEnumerable<ModDependencyFile> files,
        string? versionId,
        string targetMinecraftVersion,
        HashSet<string> targetLoaders)
    {
        return files
            .Where(file => (versionId is null || Comparer.Equals(file.Id, versionId))
                           && IsCompatibleFile(file, targetMinecraftVersion, targetLoaders))
            .OrderByDescending(file => HasLoaderMatch(file, targetLoaders))
            .ThenBy(file => NormalizeReleaseType(file.ReleaseType))
            .ThenByDescending(file => file.ReleaseDate)
            .FirstOrDefault();
    }

    private static bool IsCompatibleFile(ModDependencyFile file, string targetMinecraftVersion, HashSet<string> targetLoaders)
    {
        if (!HasExactGameVersionMatch(file, targetMinecraftVersion))
        {
            return false;
        }

        if (targetLoaders.Count == 0)
        {
            return true;
        }

        if (file.Loaders.Count == 0)
        {
            return true;
        }

        return file.Loaders.Any(loader => targetLoaders.Contains(loader));
    }

    private static bool HasExactGameVersionMatch(ModDependencyFile file, string targetMinecraftVersion)
    {
        return file.GameVersions.Any(version => Comparer.Equals(version, targetMinecraftVersion));
    }

    private static bool HasLoaderMatch(ModDependencyFile file, HashSet<string> targetLoaders)
    {
        if (targetLoaders.Count == 0)
        {
            return true;
        }

        return file.Loaders.Any(loader => targetLoaders.Contains(loader));
    }

    private static int NormalizeReleaseType(int releaseType)
    {
        return releaseType switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            _ => int.MaxValue,
        };
    }

    private sealed class ResolutionContext
    {
        private readonly HashSet<string> _installDedupe = new(Comparer);
        private readonly HashSet<string> _unresolvedDedupe = new(Comparer);
        private readonly HashSet<string> _satisfiedDedupe = new(Comparer);

        public ResolutionContext(ModDependencyRequest request)
        {
            Request = request;
            Result = new ModDependencyResolutionResult();
            if (request.MainFile is { } main)
                Selection[ProjectKey(main.Source, main.ProjectId)] = main.VersionId;
            TargetMinecraftVersion = request.TargetMinecraftVersion ?? string.Empty;
            TargetLoaders = new HashSet<string>(
                request.TargetLoaders.Where(static loader => !string.IsNullOrWhiteSpace(loader)),
                Comparer);
        }

        public ModDependencyRequest Request { get; }
        public ModDependencyResolutionResult Result { get; }
        public Dictionary<(string Source, string Project), string?> Selection { get; } = new();
        public string TargetMinecraftVersion { get; }
        public HashSet<string> TargetLoaders { get; }

        public (string Source, string Project) ProjectKey(string source, string projectId)
            => (source.ToUpperInvariant(), projectId.ToUpperInvariant());

        public bool IsInstalledCompatible(InstalledModIdentity installed, string? versionId)
        {
            return (versionId is null || Comparer.Equals(installed.FileId, versionId))
                && installed.GameVersions.Any(version => Comparer.Equals(version, TargetMinecraftVersion))
                && LoadersCompatible(installed.Loaders);
        }

        public void AddInstall(ModDependencyProject project, ModDependencyFile file)
        {
            var dedupeKey = GetProjectKey(project.ProjectId, project.Source);
            if (!_installDedupe.Add(dedupeKey))
            {
                return;
            }

            Result.ToInstall.Add(new ResolvedDependencyInstall
            {
                ProjectId = project.ProjectId,
                Source = project.Source,
                ProjectName = project.ProjectName,
                File = file,
            });
        }

        public void AddUnresolved(string projectId, string source, string reason)
        {
            var dedupeKey = GetProjectKey(projectId, source);
            if (!_unresolvedDedupe.Add(dedupeKey))
            {
                return;
            }

            Result.Unresolved.Add(new UnresolvedDependency
            {
                ProjectId = projectId,
                Source = source,
                Reason = reason,
            });
        }

        public void AddSatisfied(string projectId, string source, string reason)
        {
            var dedupeKey = GetProjectKey(projectId, source);
            if (!_satisfiedDedupe.Add(dedupeKey))
            {
                return;
            }

            Result.Satisfied.Add(new IgnoredDependency
            {
                ProjectId = projectId,
                Source = source,
                Reason = reason,
            });
        }

        private bool LoadersCompatible(List<string> installedLoaders)
        {
            if (TargetLoaders.Count == 0 || installedLoaders.Count == 0)
            {
                return true;
            }

            return installedLoaders.Any(loader => TargetLoaders.Contains(loader));
        }

        private static string GetProjectKey(string projectId, string source)
        {
            return $"{source}:{projectId}";
        }
    }
}
