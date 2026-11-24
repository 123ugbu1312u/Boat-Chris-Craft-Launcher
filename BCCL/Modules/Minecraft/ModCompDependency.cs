using System.Collections.Concurrent;
using System.IO;
using BCCL.Core.App;
using BCCL.Core.App.Localization;
using BCCL.Core.Minecraft.ResourceProject;
using BCCL.Network;
using BCCL.Network.Loaders;
using static BCCL.ModComp;

namespace BCCL;

internal static class ModCompDependency
{
    private static readonly ConcurrentDictionary<string, byte> ActiveFolders = new(StringComparer.OrdinalIgnoreCase);

    public static bool Download(CompProject project, CompFile file, string target, McInstance? instance, bool extractWorld)
    {
        if (file.Type is not (CompType.Mod or CompType.ResourcePack) ||
            !Config.Download.Comp.AutoInstallDependencies ||
            !HasDependencies(file))
            return StartResourceDownload(file, target, extractWorld);

        var targetFolder = Path.GetDirectoryName(target)!;
        instance ??= FindTargetInstance(targetFolder);
        var modsFolder = instance is not null
            ? Path.Combine(instance.PathIndie, "mods")
            : file.Type == CompType.ResourcePack &&
              Path.GetFileName(targetFolder).Equals("resourcepacks", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Path.GetDirectoryName(targetFolder) ?? targetFolder, "mods")
                : targetFolder;
        if (instance?.Info.HasLabyMod == true)
            modsFolder = Path.Combine(instance.PathIndie, "labymod-neo", "fabric", instance.Info.VanillaName, "mods");
        modsFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modsFolder));

        if (!ActiveFolders.TryAdd(modsFolder, 0))
        {
            ShowMessage("Busy");
            return false;
        }

        var handedOff = false;
        var hasMismatch = false;
        try
        {
            var source = project.FromCurseForge ? "CurseForge" : "Modrinth";
            var catalog = new DependencyCatalog(project.FromCurseForge, modsFolder);
            var resolver = new ModDependencyResolver();
            var instanceLoaders = GetInstanceLoaders(instance);
            var gameVersion = instance?.Info.VanillaName ?? GetGameVersion(file);
            var loadersForTarget = instanceLoaders ?? (file.ModLoaders.Count > 0 ? file.ModLoaders : project.ModLoaders);
            var loaderNames = instanceLoaders is { Count: 0 } ? ["Minecraft"] : LoaderNames(loadersForTarget);

            ModDependencyResolutionResult Resolve(CompFile candidate)
            {
                return resolver.Resolve(new ModDependencyRequest
                {
                    TargetMinecraftVersion = gameVersion,
                    TargetLoaders = loaderNames,
                    MainFile = new ModDependencyReference { ProjectId = project.Id, Source = source, VersionId = candidate.Id },
                    RequiredDependencies = catalog.DependenciesOf(candidate).ToList(),
                    FindInstalledMods = (_, projectId) => catalog.Installed(projectId, target),
                    ProjectResolver = (_, projectId) => catalog.ResolveProject(projectId),
                });
            }

            var plan = Resolve(file);
            hasMismatch = plan.Mismatched.Count > 0;
            if (hasMismatch)
            {
                var choice = Ask("Mismatch", "Update", "Older");
                if (choice == 2)
                {
                    var older = CompFilesGet(project.Id, project.FromCurseForge, includeDependencyProjects: false)
                        .Where(candidate => candidate.Available && candidate.Type == file.Type
                            && candidate.ReleaseDate < file.ReleaseDate
                            && (candidate.RawGameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase) ||
                                candidate.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
                            && (loadersForTarget.Count == 0 || candidate.ModLoaders.Count == 0 ||
                                candidate.ModLoaders.Intersect(loadersForTarget).Any()))
                        .OrderByDescending(candidate => candidate.ReleaseDate)
                        .Select(candidate => (File: candidate, Plan: Resolve(candidate)))
                        .FirstOrDefault(candidate => candidate.Plan.Mismatched.Count == 0 && candidate.Plan.Unresolved.Count == 0);
                    if (older.File is null)
                    {
                        ShowMessage("NoOlder");
                        return false;
                    }

                    var olderTarget = Path.Combine(targetFolder, CompFileNameGet(project, older.File));
                    if (!string.Equals(target, olderTarget, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(olderTarget) &&
                        Ask("OverwriteOlder", "DownloadButton", "Skip", Path.GetFileName(olderTarget)) != 1)
                        return false;

                    file = older.File;
                    target = olderTarget;
                    plan = older.Plan;
                }
                else if (choice != 1)
                {
                    return false;
                }
            }

            var installDependencies = plan.Mismatched.Count > 0;
            if (!installDependencies && (plan.ToInstall.Count > 0 || plan.Unresolved.Count > 0))
                installDependencies = Ask("Missing", "DownloadButton", "Skip") == 1;

            if (installDependencies && plan.Unresolved.Count > 0)
            {
                var details = string.Join(Environment.NewLine, plan.Unresolved.Select(dep => $"{dep.ProjectId} ({dep.Source})"));
                if (plan.Mismatched.Count > 0)
                {
                    ShowMessage("NoUpdate", details);
                    return false;
                }

                if (Ask("Incomplete", "DownloadButton", "Skip", details) != 1)
                    return false;
                installDependencies = false;
            }

            if (!installDependencies || plan.ToInstall.Count == 0)
                return StartResourceDownload(file, target, extractWorld);

            var downloads = plan.ToInstall.Select(install =>
            {
                var dep = catalog.GetFiles(install.ProjectId).Single(candidate => candidate.Id == install.File.Id);
                var depProject = compProjectCache[install.ProjectId];
                return (File: dep, Target: Path.Combine(modsFolder, CompFileNameGet(depProject, dep)));
            }).ToList();
            downloads.Add((file, target));
            var staged = downloads.Select((item, index) => (
                item.Target,
                IsMain: index == downloads.Count - 1,
                Download: item.File.ToNetFile(
                    item.Target + "." + Guid.NewGuid().ToString("N") + ".bccl-download",
                    index == downloads.Count - 1 ? DownloadReason.Standalone : DownloadReason.Dependency)
            )).ToList();

            var commitLock = new object();
            var loaders = new List<ModLoader.LoaderBase>
            {
                new LoaderDownload(Lang.Text("Download.Comp.Detail.DownloadFile"),
                    staged.Select(item => item.Download).ToList()) { ProgressWeight = 6 },
                new ModLoader.LoaderTask<int, int>(Lang.Text("Instance.Resource.Update.Task.ReplaceFiles"), task =>
                {
                    lock (commitLock)
                        CommitDownloads(staged, plan.Mismatched, () => task.IsAborted);
                }) { ProgressWeight = 0.1 }
            };
            handedOff = StartResourceDownload(file, target, extractWorld, loaders, () =>
            {
                try
                {
                    lock (commitLock)
                        foreach (var item in staged)
                            TryDelete(item.Download.LocalPath);
                }
                finally
                {
                    ActiveFolders.TryRemove(modsFolder, out _);
                }
            });
            return handedOff;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[CompDeps] 解析前置失败");
            if (hasMismatch)
            {
                ShowMessage("CheckFailed");
                return false;
            }
            if (Ask("ResolveFailed", "DownloadButton", "Skip") != 1)
                return false;
            return StartResourceDownload(file, target, extractWorld);
        }
        finally
        {
            if (!handedOff)
                ActiveFolders.TryRemove(modsFolder, out _);
        }
    }

    private static bool StartResourceDownload(CompFile file, string target, bool extractWorld, List<ModLoader.LoaderBase>? loaders = null, Action? finished = null)
    {
        var desc = file.Type switch
        {
            CompType.ModPack => Lang.Text("Download.Comp.Type.Modpack"),
            CompType.Mod => Lang.Text("Download.Comp.Type.Mod"),
            CompType.ResourcePack => Lang.Text("Download.Comp.Type.ResourcePack"),
            CompType.Shader => Lang.Text("Download.Comp.Type.Shader"),
            CompType.DataPack => Lang.Text("Download.Comp.Type.DataPack"),
            CompType.World => Lang.Text("Download.Comp.Type.World"),
            _ => Lang.Text("Download.Comp.Type.Mod")
        };
        var loaderName = Lang.Text("Download.Comp.Detail.DownloadResource", desc,
            ModBase.GetFileNameWithoutExtentionFromPath(target));
        loaders ??= new List<ModLoader.LoaderBase>
        {
            new LoaderDownload(Lang.Text("Download.Comp.Detail.DownloadFile"),
                new List<DownloadFile>
                {
                    file.Type == CompType.Mod
                        ? file.ToNetFile(target)
                        : file.ToNetFile(target, DownloadReason.Standalone, null)
                })
            {
                ProgressWeight = 6,
                block = true
            }
        };
        if (extractWorld && file.Type == CompType.World)
        {
            var extractDir = Path.GetDirectoryName(target);
            loaders.Add(new ModLoader.LoaderTask<int, int>(
                Lang.Text("Download.Comp.Detail.InstallWorld"),
                _ => ModBase.ExtractFile(target, extractDir, System.Text.Encoding.UTF8))
            {
                ProgressWeight = 0.1d,
                block = true
            });
            loaders.Add(new ModLoader.LoaderTask<int, int>(
                Lang.Text("Download.Comp.Detail.CleanCache"),
                _ => System.IO.File.Delete(target)));
        }
        ModBase.RunInUiWait(() =>
        {
            var loader = new ModLoader.LoaderCombo<int>(loaderName, loaders);
            loader.OnStateChanged = task =>
            {
                try
                {
                    ModDownloadLib.LoaderStateChangedHintOnly(task);
                }
                finally
                {
                    if (task.State is ModBase.LoadState.Finished or ModBase.LoadState.Failed or ModBase.LoadState.Aborted)
                    {
                        var complete = Interlocked.Exchange(ref finished, null);
                        if (complete is not null)
                            ModBase.RunInThread(complete);
                    }
                }
            };
            loader.Start(1);
            ModLoader.LoaderTaskbarAdd(loader);
            ModMain.frmMain.BtnExtraDownload.ShowRefresh();
            ModMain.frmMain.BtnExtraDownload.Ribble();
        });
        return true;
    }

    private static bool HasDependencies(CompFile file) =>
        file.RawDependencies.Count > 0 || file.Dependencies.Count > 0 || file.RequiredDependencyVersions.Count > 0;

    private static string GetGameVersion(CompFile file) => file.RawGameVersions
        .FirstOrDefault(version => file.GameVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
        ?? file.RawGameVersions.FirstOrDefault() ?? file.GameVersions.FirstOrDefault() ?? "";

    private static McInstance? FindTargetInstance(string folder)
    {
        var folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        bool ContainsFolder(McInstance instance)
        {
            if (instance.state == McInstanceState.Error && instance.IsLoaded)
                return false;
            return folderPath.StartsWith(instance.PathIndie, StringComparison.OrdinalIgnoreCase);
        }

        var selected = ModInstanceList.McMcInstanceSelected;
        var instances = ModInstanceList.mcInstanceList.Values.SelectMany(list => list).OfType<McInstance>();
        if (selected is not null)
            instances = instances.Prepend(selected);
        return instances.Distinct().Where(ContainsFolder)
            .OrderByDescending(instance => instance.PathIndie.Length)
            .ThenByDescending(instance => instance == selected)
            .FirstOrDefault(instance => instance == selected ||
                !string.Equals(instance.PathIndie, ModFolder.mcFolderSelected, StringComparison.OrdinalIgnoreCase));
    }

    private static List<CompLoaderType>? GetInstanceLoaders(McInstance? instance)
    {
        if (instance is null)
            return null;
        if (!instance.IsLoaded)
            instance.Load();
        var loaders = new List<CompLoaderType>();
        if (instance.Info.HasForge || instance.Info.HasCleanroom)
            loaders.Add(CompLoaderType.Forge);
        if (instance.Info.HasFabric || instance.Info.HasLegacyFabric || instance.Info.HasLabyMod)
            loaders.Add(CompLoaderType.Fabric);
        if (instance.Info.HasQuilt)
            loaders.Add(CompLoaderType.Quilt);
        if (instance.Info.HasNeoForge)
            loaders.Add(CompLoaderType.NeoForge);
        if (instance.Info.HasLiteLoader)
            loaders.Add(CompLoaderType.LiteLoader);
        return loaders;
    }

    private static List<string> LoaderNames(List<CompLoaderType> loaders) =>
        loaders.Where(loader => loader != CompLoaderType.Any).Select(loader => loader.ToString()).ToList();

    private static int Ask(string message, string firstButton, string secondButton, params object[] args) =>
        ModBase.RunInUiWait(() => ModMain.MyMsgBox(
            Lang.Text("Download.Comp.Dependency." + message + ".Message", args),
            Lang.Text("Download.Comp.Dependency.Install.Title"),
            Lang.Text(firstButton == "DownloadButton" ? "Common.Action.Download" : "Download.Comp.Dependency." + firstButton),
            Lang.Text("Download.Comp.Dependency." + secondButton)));

    private static void ShowMessage(string message, params object[] args) =>
        ModBase.RunInUiWait(() => ModMain.MyMsgBox(
            Lang.Text("Download.Comp.Dependency." + message + ".Message", args),
            Lang.Text("Download.Comp.Dependency.Install.Title"), isWarn: true, forceWait: true));

    private static void CommitDownloads(
        List<(string Target, bool IsMain, DownloadFile Download)> staged,
        List<InstalledModIdentity> replacements,
        Func<bool> isAborted)
    {
        var oldFiles = replacements.Where(mod => mod.LocalPath is not null)
            .DistinctBy(mod => mod.LocalPath, StringComparer.OrdinalIgnoreCase).ToList();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in staged)
        {
            if (!paths.Add(item.Target) ||
                (!item.IsMain && File.Exists(item.Target) && !oldFiles.Any(old =>
                    string.Equals(old.LocalPath, item.Target, StringComparison.OrdinalIgnoreCase))))
                throw new IOException(Lang.Text("Download.Comp.Dependency.ConflictingFile.Message", item.Target));
        }

        var backupSuffix = "." + Guid.NewGuid().ToString("N") + ".old";
        var moved = new Stack<(string From, string To)>();
        void Move(string from, string to)
        {
            if (isAborted())
                throw new OperationCanceledException();
            File.Move(from, to);
            moved.Push((from, to));
        }

        var main = staged.Single(item => item.IsMain).Target;
        string? mainBackup = null;
        try
        {
            foreach (var old in oldFiles)
            {
                if (!string.Equals(FileHash(old.LocalPath!, old.Hash!.Length), old.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Lang.Text("Download.Comp.Dependency.ConflictingFile.Message", old.LocalPath));
                var backup = old.LocalPath + ".old";
                if (File.Exists(backup))
                    backup = Path.ChangeExtension(old.LocalPath, backupSuffix + Path.GetExtension(old.LocalPath) + ".old");
                Move(old.LocalPath!, backup);
            }

            if (File.Exists(main))
            {
                mainBackup = main + backupSuffix;
                Move(main, mainBackup);
            }

            foreach (var item in staged)
                Move(item.Download.LocalPath, item.Target);
        }
        catch
        {
            foreach (var (from, to) in moved)
            {
                try { File.Move(to, from); }
                catch (IOException ex) { ModBase.Log(ex, "[CompDeps] 恢复文件失败", ModBase.LogLevel.Hint); }
                catch (UnauthorizedAccessException ex) { ModBase.Log(ex, "[CompDeps] 恢复文件失败", ModBase.LogLevel.Hint); }
            }
            throw;
        }

        if (mainBackup is not null)
            TryDelete(mainBackup);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException ex) { ModBase.Log(ex, "[CompDeps] 清理临时文件失败"); }
        catch (UnauthorizedAccessException ex) { ModBase.Log(ex, "[CompDeps] 清理临时文件失败"); }
    }

    private static string FileHash(string path, int hashLength) =>
        hashLength == 32 ? ModBase.GetFileMD5(path) : ModBase.GetFileSHA1(path);

    private sealed class DependencyCatalog(bool fromCurseForge, string modsFolder)
    {
        private readonly string _source = fromCurseForge ? "CurseForge" : "Modrinth";
        private readonly Dictionary<string, List<CompFile>> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CompFile> _pinned = new(StringComparer.Ordinal);
        private readonly Dictionary<int, ILookup<string, string>> _localHashes = new();

        public IEnumerable<ModDependencyReference> DependenciesOf(CompFile file)
        {
            var pinnedProjects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (versionId, declaredProjectId) in file.RequiredDependencyVersions)
            {
                var projectId = declaredProjectId;
                if (projectId is null || !GetFiles(projectId).Any(candidate => candidate.Id == versionId))
                {
                    if (!_pinned.TryGetValue(versionId, out var pinnedFile))
                    {
                        var json = ModDownload.DlModRequest<JsonObject>(
                            "https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId));
                        _pinned[versionId] = pinnedFile = new CompFile(json, CompType.Mod);
                    }
                    projectId ??= pinnedFile.ProjectId;
                    var files = GetFiles(projectId);
                    if (pinnedFile.ProjectId == projectId && pinnedFile.Type == CompType.Mod && pinnedFile.Available &&
                        !files.Any(candidate => candidate.Id == versionId))
                        files.Add(pinnedFile);
                }
                pinnedProjects.Add(projectId);
                yield return new ModDependencyReference { ProjectId = projectId, Source = _source, VersionId = versionId };
            }

            foreach (var projectId in file.RawDependencies.Concat(file.Dependencies).Distinct(StringComparer.Ordinal))
                if (!pinnedProjects.Contains(projectId))
                    yield return new ModDependencyReference { ProjectId = projectId, Source = _source };
        }

        public List<CompFile> GetFiles(string projectId)
        {
            if (!_files.TryGetValue(projectId, out var files))
                _files[projectId] = files = CompFilesGet(projectId, fromCurseForge, includeDependencyProjects: false)
                    .Where(file => file.Type == CompType.Mod && file.FromCurseForge == fromCurseForge).ToList();
            return files;
        }

        public ModDependencyProject ResolveProject(string projectId) => new()
        {
            ProjectId = projectId,
            Source = _source,
            Files = GetFiles(projectId).Select(file => new ModDependencyFile
            {
                Id = file.Id,
                GameVersions = [.. file.RawGameVersions.Concat(file.GameVersions)],
                Loaders = LoaderNames(file.ModLoaders),
                ReleaseType = file.Status switch
                {
                    CompFileStatus.Release => 1,
                    CompFileStatus.Beta => 2,
                    _ => 3,
                },
                ReleaseDate = file.ReleaseDate,
                RequiredDependencies = DependenciesOf(file),
            }).ToList(),
        };

        public IEnumerable<InstalledModIdentity> Installed(string projectId, string mainTarget)
        {
            foreach (var file in GetFiles(projectId))
            {
                if (string.IsNullOrEmpty(file.Hash) || file.Hash.Length is not (32 or 40))
                    continue;
                if (!_localHashes.TryGetValue(file.Hash.Length, out var pathsByHash))
                {
                    var paths = Directory.Exists(modsFolder) ? Directory.EnumerateFiles(modsFolder) : [];
                    _localHashes[file.Hash.Length] = pathsByHash = paths
                        .Where(path => ModLocalComp.LocalCompFile.IsModFile(path) &&
                            !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) &&
                            !path.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                        .ToLookup(path => file.Hash.Length == 40
                            ? new ModLocalComp.LocalCompFile(path).ModrinthHash
                            : FileHash(path, 32), StringComparer.OrdinalIgnoreCase);
                }

                foreach (var path in pathsByHash[file.Hash])
                {
                    if (string.Equals(path, mainTarget, StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return new InstalledModIdentity
                    {
                        Source = _source,
                        SourceProjectId = projectId,
                        FileId = file.Id,
                        LocalPath = path,
                        Hash = file.Hash,
                        GameVersions = [.. file.RawGameVersions.Concat(file.GameVersions)],
                        Loaders = LoaderNames(file.ModLoaders),
                    };
                }
            }
        }
    }
}
