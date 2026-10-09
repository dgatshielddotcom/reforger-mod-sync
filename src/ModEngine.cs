using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ReforgerModSync
{
    // Operate only on recognized immediate addon directories. Never follow links or overwrite content.
    public static class ModEngine
    {
        private const string BaseGameId = "58D0FB3206B6F859";
        private const string VaultName = "addons-disabled-by-ReforgerSync";
        private static readonly Regex IdPattern = new Regex(@"\A[0-9A-Fa-f]{16}\z");
        private static readonly Regex GuidPattern = new Regex(@"\bGUID\s+""([0-9A-Fa-f]{16})""", RegexOptions.IgnoreCase);
        private static readonly Regex TitlePattern = new Regex(@"\bTITLE\s+""([^""]*)""", RegexOptions.IgnoreCase);
        private static readonly Regex DependencyPattern = new Regex(@"\bDependencies\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex HexStringPattern = new Regex(@"""([0-9A-Fa-f]{16})""");
        private static readonly Regex FolderIdPattern = new Regex(@"_([0-9A-Fa-f]{16})\z");

        public static SyncPlan BuildPlan(string root, ServerSnapshot snapshot)
        {
            string addons = ValidateRoot(root);
            string vault = Path.Combine(Path.GetDirectoryName(addons), VaultName);
            ValidateAncestors(vault);
            if (File.Exists(vault)) throw new IOException("The disabled-mods location is a file: " + vault);
            var plan = new SyncPlan { AddonsRoot = addons, VaultRoot = vault, Snapshot = CopySnapshot(snapshot) };
            plan.Active = Scan(addons, false);
            plan.Disabled = Directory.Exists(vault) ? Scan(vault, true) : new List<LocalMod>();
            var all = plan.Active.Concat(plan.Disabled).ToList();
            MarkDuplicates(all);
            var required = new HashSet<string>(plan.Snapshot.Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            // The authoritative server list already contains its dependencies. Installed older
            // revisions may have obsolete dependencies, so only server IDs determine membership.
            var keep = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);
            var server = plan.Snapshot.Mods.ToDictionary(m => m.Id);
            var satisfied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalMod local in all.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Folder, StringComparer.OrdinalIgnoreCase))
            {
                ServerMod remote;
                server.TryGetValue(local.Id ?? "", out remote);
                var row = new PlanRow { Local = local, Id = local.Id ?? "", Name = local.Name, LocalVersion = local.Version ?? "", ServerVersion = remote == null ? "" : remote.Version ?? "", Folder = local.Folder, Action = "None" };
                if (!String.IsNullOrEmpty(local.Warning))
                {
                    row.Status = "Unknown";
                    row.Action = "Leave unchanged: " + local.Warning;
                    plan.UnknownCount++;
                }
                else if (keep.Contains(local.Id))
                {
                    satisfied.Add(local.Id);
                    if (local.Disabled)
                    {
                        row.Status = "Restore";
                        row.Action = "Restore to addons";
                        plan.EnableCount++;
                    }
                    else
                    {
                        row.Status = "Keep";
                        row.Action = "On server; version ignored";
                    }
                }
                else if (!local.Disabled)
                {
                    row.Status = "Disable";
                    row.Action = "Remove extra addon (selected cleanup mode)";
                    plan.DisableCount++;
                }
                else
                {
                    row.Status = "Disabled";
                    row.Action = "Leave in disabled folder";
                }
                plan.Rows.Add(row);
            }
            foreach (ServerMod remote in plan.Snapshot.Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                if (!satisfied.Contains(remote.Id))
                {
                    plan.Rows.Add(new PlanRow { Status = "Missing", Action = "Download through the game", Id = remote.Id, Name = remote.Name, LocalVersion = "", ServerVersion = remote.Version ?? "", Folder = "" });
                    plan.MissingCount++;
                }
            plan.ScanSignature = ScanSignature(plan);
            plan.RowsSignature = Hash(Json(plan.Rows));
            plan.SnapshotSignature = Hash(Json(plan.Snapshot));
            return plan;
        }

        public static string Apply(SyncPlan plan)
        {
            return Apply(plan, "disable");
        }

        public static string Apply(SyncPlan plan, string mode)
        {
            if (mode != "disable" && mode != "recycle" && mode != "delete") throw new ArgumentException("Unknown cleanup mode.");
            if (plan == null || String.IsNullOrEmpty(plan.ScanSignature)) throw new ArgumentException("Preview a new plan before applying it.");
            string root = ValidateRoot(plan.AddonsRoot);
            using (Mutex mutex = AcquireMutex(root))
            {
                try
                {
                    EnsureGameClosed();
                    DateTime fetched = plan.Snapshot == null ? DateTime.MinValue : plan.Snapshot.FetchedUtc;
                    if (fetched.Kind == DateTimeKind.Local) fetched = fetched.ToUniversalTime();
                    if (fetched < DateTime.UtcNow.AddMinutes(-15) || fetched > DateTime.UtcNow.AddMinutes(5))
                        throw new InvalidOperationException("The server mod list is older than 15 minutes or has an invalid timestamp. Refresh the server list and preview again.");
                    if (Hash(Json(plan.Snapshot)) != plan.SnapshotSignature || Hash(Json(plan.Rows)) != plan.RowsSignature)
                        throw new InvalidOperationException("The preview was changed. Scan again before applying.");
                    SyncPlan fresh = BuildPlan(root, plan.Snapshot);
                    if (!SamePath(fresh.VaultRoot, plan.VaultRoot) || fresh.ScanSignature != plan.ScanSignature || fresh.RowsSignature != plan.RowsSignature)
                        throw new InvalidOperationException("Addon folders or metadata changed after the preview. Scan again before applying.");
                    var moves = fresh.Rows.Where(r => r.Status == "Restore" || r.Status == "Disable").OrderBy(r => r.Status == "Restore" ? 0 : 1).ToList();
                    foreach (PlanRow row in moves)
                    {
                        string fromRoot = row.Local.Disabled ? fresh.VaultRoot : fresh.AddonsRoot;
                        string toRoot = row.Local.Disabled ? fresh.AddonsRoot : fresh.VaultRoot;
                        if (row.Local.Disabled || mode == "disable") ValidateMove(row.Folder, fromRoot, Path.Combine(toRoot, Path.GetFileName(row.Folder)), toRoot);
                        else ValidateSource(row.Folder, fromRoot);
                    }
                    if (moves.Count == 0) return Summary(fresh, 0, 0, mode);
                    Directory.CreateDirectory(fresh.VaultRoot);
                    ValidateAncestors(fresh.VaultRoot);
                    string journal = Path.Combine(fresh.VaultRoot, "sync-journal.jsonl");
                    int disabled = 0, restored = 0;
                    using (FileStream log = OpenJournal(journal))
                    {
                        try
                        {
                            foreach (PlanRow row in moves)
                            {
                                EnsureGameClosed();
                                string fromRoot = row.Local.Disabled ? fresh.VaultRoot : fresh.AddonsRoot;
                                string toRoot = row.Local.Disabled ? fresh.AddonsRoot : fresh.VaultRoot;
                                bool move = row.Local.Disabled || mode == "disable";
                                string destination = move ? Path.Combine(toRoot, Path.GetFileName(row.Folder)) : "";
                                if (move) ValidateMove(row.Folder, fromRoot, destination, toRoot);
                                else ValidateSource(row.Folder, fromRoot);
                                if (ReadLocal(row.Folder, row.Local.Disabled).MetadataSignature != row.Local.MetadataSignature)
                                    throw new IOException("Metadata changed during synchronization: " + row.Folder);
                                string operation = Guid.NewGuid().ToString("N");
                                string action = move ? "move" : mode;
                                Journal(log, operation, "pending", row.Folder, destination, row.Id, action);
                                if (move) Directory.Move(row.Folder, destination);
                                else if (mode == "delete") Directory.Delete(row.Folder, true);
                                else RecycleDirectory(row.Folder);
                                if (row.Local.Disabled) restored++; else disabled++;
                                Journal(log, operation, "done", row.Folder, destination, row.Id, action);
                            }
                        }
                        catch (Exception ex)
                        {
                            string recovery = mode == "disable" ? "Completed moves are recoverable with Restore all." : mode == "recycle" ? "Check Windows Recycle Bin to recover recycled folders." : "Permanent deletions cannot be undone by this app; a failed deletion may have removed part of the current folder.";
                            throw new IOException("Synchronization stopped after removing " + disabled + " and restoring " + restored + " folder(s). " + recovery + " Journal: " + journal + ". " + ex.Message, ex);
                        }
                    }
                    return Summary(fresh, disabled, restored, mode);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static string RestoreAll(string root)
        {
            string addons = ValidateRoot(root);
            string vault = Path.Combine(Path.GetDirectoryName(addons), VaultName);
            using (Mutex mutex = AcquireMutex(addons))
            {
                try
                {
                    EnsureGameClosed();
                    ValidateAncestors(vault);
                    if (!Directory.Exists(vault)) return "No disabled-mods folder exists; nothing to restore.";
                    List<LocalMod> active = Scan(addons, false);
                    List<LocalMod> disabled = Scan(vault, true);
                    MarkDuplicates(active.Concat(disabled).ToList());
                    var activeIds = new HashSet<string>(active.Where(m => !String.IsNullOrEmpty(m.Id)).Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                    int restored = 0, skipped = 0;
                    using (FileStream log = OpenJournal(Path.Combine(vault, "sync-journal.jsonl")))
                    {
                        foreach (LocalMod local in disabled)
                        {
                            string destination = Path.Combine(addons, Path.GetFileName(local.Folder));
                            if (!String.IsNullOrEmpty(local.Warning) || activeIds.Contains(local.Id) || Directory.Exists(destination) || File.Exists(destination)) { skipped++; continue; }
                            EnsureGameClosed();
                            ValidateMove(local.Folder, vault, destination, addons);
                            if (ReadLocal(local.Folder, true).MetadataSignature != local.MetadataSignature)
                                throw new IOException("Metadata changed during restoration: " + local.Folder);
                            string operation = Guid.NewGuid().ToString("N");
                            Journal(log, operation, "pending", local.Folder, destination, local.Id);
                            Directory.Move(local.Folder, destination);
                            restored++;
                            Journal(log, operation, "done", local.Folder, destination, local.Id);
                            activeIds.Add(local.Id);
                        }
                    }
                    return "Restored " + restored + " folder(s). Left " + skipped + " unknown or conflicting folder(s) unchanged.";
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static void EnsureGameClosed()
        {
            foreach (Process process in Process.GetProcesses())
                using (process)
                {
                    string name;
                    try { name = process.ProcessName; }
                    catch (InvalidOperationException) { continue; }
                    if (name.StartsWith("ArmaReforger", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Close Arma Reforger, its server, and Workbench before moving addons. Running process: " + name);
                }
        }

        private static List<LocalMod> Scan(string root, bool disabled)
        {
            ValidateAncestors(root);
            return Directory.GetDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => ReadLocal(p, disabled)).ToList();
        }

        private static LocalMod ReadLocal(string folder, bool disabled)
        {
            var local = new LocalMod { Folder = Path.GetFullPath(folder), Name = Path.GetFileName(folder), Disabled = disabled, Version = "" };
            var stamp = new StringBuilder();
            try
            {
                EnsureNoReparseTree(folder);
                string[] projects = Directory.GetFiles(folder, "*.gproj", SearchOption.TopDirectoryOnly);
                if (projects.Length != 1) throw new InvalidDataException("Expected exactly one top-level .gproj file");
                string project = ReadMetadata(projects[0], stamp);
                MatchCollection guids = GuidPattern.Matches(project);
                if (guids.Count != 1) throw new InvalidDataException("Missing or ambiguous project GUID");
                local.Id = NormalizeId(guids[0].Groups[1].Value);
                Match title = TitlePattern.Match(project);
                if (title.Success && !String.IsNullOrWhiteSpace(title.Groups[1].Value)) local.Name = title.Groups[1].Value;
                foreach (Match block in DependencyPattern.Matches(project))
                    foreach (Match match in HexStringPattern.Matches(block.Groups[1].Value)) AddDependency(local, match.Groups[1].Value);
                Match suffix = FolderIdPattern.Match(Path.GetFileName(folder));
                if (suffix.Success && NormalizeId(suffix.Groups[1].Value) != local.Id) throw new InvalidDataException("Folder suffix disagrees with project GUID");
                string serverFile = Path.Combine(folder, "ServerData.json");
                if (File.Exists(serverFile))
                {
                    IDictionary<string, object> data = ObjectMap(ParseJson(ReadMetadata(serverFile, stamp)));
                    CheckId(data, local.Id, "ServerData");
                    string name = Value(data, "name");
                    if (!String.IsNullOrWhiteSpace(name)) local.Name = name;
                    object revision;
                    if (data.TryGetValue("revision", out revision))
                    {
                        IDictionary<string, object> rev = ObjectMap(revision);
                        local.Version = Value(rev, "version");
                        ReadDependencies(local, rev);
                    }
                }
                string metaFile = Path.Combine(folder, "meta");
                if (File.Exists(metaFile))
                {
                    IDictionary<string, object> outer = ObjectMap(ParseJson(ReadMetadata(metaFile, stamp)));
                    object metaObject;
                    IDictionary<string, object> meta = outer.TryGetValue("meta", out metaObject) ? ObjectMap(metaObject) : outer;
                    CheckId(meta, local.Id, "meta");
                    if (String.IsNullOrEmpty(local.Version)) ReadMetaRevision(local, meta);
                }
                local.Dependencies = local.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id).ToList();
                stamp.Append("\nID=").Append(local.Id).Append(";VERSION=").Append(local.Version).Append(";DEPS=").Append(String.Join(",", local.Dependencies));
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)) throw;
                local.Warning = ex.Message;
                stamp.Append("\nUNKNOWN=").Append(ex.Message);
            }
            // Changes in folder identity or its top-level files also invalidate a preview.
            try
            {
                stamp.Append("\nDIR=").Append(new DirectoryInfo(folder).CreationTimeUtc.Ticks);
                foreach (FileInfo file in new DirectoryInfo(folder).GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    stamp.Append("\nFILE=").Append(file.Name).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks).Append(':').Append((int)file.Attributes);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                local.Warning = ex.Message;
                stamp.Append("\nERROR=").Append(ex.Message);
            }
            local.MetadataSignature = Hash(stamp.ToString());
            return local;
        }

        private static void ReadMetaRevision(LocalMod local, IDictionary<string, object> meta)
        {
            object versionsObject;
            if (!meta.TryGetValue("versions", out versionsObject)) return;
            object[] versions = AsArray(versionsObject);
            int selected = 0;
            object selectedObject;
            if (meta.TryGetValue("selectedRev", out selectedObject) && !Int32.TryParse(Convert.ToString(selectedObject), out selected)) throw new InvalidDataException("Invalid selectedRev in meta");
            if (selected < 0 || selected >= versions.Length) throw new InvalidDataException("Selected meta revision is unavailable");
            IDictionary<string, object> revision = ObjectMap(versions[selected]);
            local.Version = Value(revision, "version");
            ReadDependencies(local, revision);
        }

        private static void ReadDependencies(LocalMod local, IDictionary<string, object> revision)
        {
            object dependencies;
            if (!revision.TryGetValue("dependencies", out dependencies)) return;
            foreach (object dependency in AsArray(dependencies))
            {
                IDictionary<string, object> data = ObjectMap(dependency);
                string id = Value(data, "assetId");
                if (String.IsNullOrEmpty(id)) id = Value(data, "id");
                if (String.IsNullOrEmpty(id)) throw new InvalidDataException("Dependency metadata has no assetId");
                AddDependency(local, id);
            }
        }

        private static void AddDependency(LocalMod local, string id)
        {
            string normalized = NormalizeId(id);
            if (normalized != BaseGameId) local.Dependencies.Add(normalized);
        }

        private static void CheckId(IDictionary<string, object> data, string id, string source)
        {
            string candidate = Value(data, "id");
            if (!String.IsNullOrEmpty(candidate) && NormalizeId(candidate) != id) throw new InvalidDataException(source + " ID disagrees with project GUID");
        }

        private static void MarkDuplicates(List<LocalMod> mods)
        {
            foreach (var group in mods.Where(m => !String.IsNullOrEmpty(m.Id)).GroupBy(m => m.Id))
                if (group.Count() > 1)
                    foreach (LocalMod mod in group) mod.Warning = "Duplicate addon ID in active/disabled folders" + (String.IsNullOrEmpty(mod.Warning) ? "" : "; " + mod.Warning);
        }

        private static ServerSnapshot CopySnapshot(ServerSnapshot source)
        {
            if (source == null || source.Mods == null) throw new ArgumentException("The server mod list is unavailable. No synchronization can be planned.");
            if (source.Mods.Count == 0 && !source.ConfirmedEmpty) throw new ArgumentException("The server mod list is empty. Confirm an explicitly imported empty list before planning cleanup.");
            var copy = new ServerSnapshot { Id = source.Id, Host = source.Host, Port = source.Port, Name = source.Name, Source = source.Source, FetchedUtc = source.FetchedUtc, ConfirmedEmpty = source.ConfirmedEmpty };
            var ids = new HashSet<string>();
            foreach (ServerMod mod in source.Mods)
            {
                if (mod == null) throw new ArgumentException("The server list contains an empty entry.");
                string id = NormalizeId(mod.Id);
                if (id == BaseGameId) continue;
                if (!ids.Add(id)) throw new ArgumentException("The server list contains duplicate addon ID " + id + ".");
                copy.Mods.Add(new ServerMod { Id = id, Name = String.IsNullOrWhiteSpace(mod.Name) ? id : mod.Name, Version = mod.Version ?? "" });
            }
            if (copy.Mods.Count == 0 && !copy.ConfirmedEmpty) throw new ArgumentException("The server mod list contains no addon IDs. An empty list requires explicit confirmation.");
            copy.Mods = copy.Mods.OrderBy(m => m.Id).ToList();
            return copy;
        }

        private static string ScanSignature(SyncPlan plan)
        {
            var text = new StringBuilder(plan.AddonsRoot.ToUpperInvariant()).Append('|').Append(plan.VaultRoot.ToUpperInvariant());
            foreach (LocalMod local in plan.Active.Concat(plan.Disabled).OrderBy(m => m.Folder, StringComparer.OrdinalIgnoreCase))
                text.Append('\n').Append(local.Folder.ToUpperInvariant()).Append('|').Append(local.Disabled).Append('|').Append(local.MetadataSignature).Append('|').Append(local.Warning);
            return Hash(text.ToString());
        }

        private static string ValidateRoot(string root)
        {
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new ArgumentException("Choose an absolute addons folder path.");
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!String.Equals(Path.GetFileName(full), "addons", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Select the Arma Reforger addons folder itself (folder name must be addons).");
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Addons folder does not exist: " + full);
            ValidateAncestors(full);
            return full;
        }

        private static void ValidateAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links or junctions are not supported: " + current);
                string parent = Path.GetDirectoryName(current);
                if (String.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent;
            }
        }

        private static void EnsureNoReparseTree(string folder)
        {
            ValidateAncestors(folder);
            var folders = new Stack<string>();
            folders.Push(folder);
            while (folders.Count > 0)
            {
                string directory = folders.Pop();
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Folder contains a link or junction: " + entry);
                    if ((attributes & FileAttributes.Directory) != 0) folders.Push(entry);
                }
            }
        }

        private static void ValidateMove(string source, string sourceRoot, string destination, string destinationRoot)
        {
            ValidateSource(source, sourceRoot);
            ValidateAncestors(destinationRoot);
            string fullSource = Path.GetFullPath(source), fullDestination = Path.GetFullPath(destination);
            if (!SamePath(Path.GetDirectoryName(fullSource), sourceRoot) || !SamePath(Path.GetDirectoryName(fullDestination), destinationRoot) || !String.Equals(Path.GetFileName(fullSource), Path.GetFileName(fullDestination), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Move is outside the immediate addon folders.");
            ValidateAncestors(fullDestination);
            if (Directory.Exists(fullDestination) || File.Exists(fullDestination)) throw new IOException("Destination already exists; nothing will be overwritten: " + fullDestination);
        }

        private static void ValidateSource(string source, string sourceRoot)
        {
            ValidateAncestors(sourceRoot);
            string full = Path.GetFullPath(source);
            if (!SamePath(Path.GetDirectoryName(full), sourceRoot)) throw new IOException("Source is outside the immediate addons folder.");
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Source addon no longer exists: " + full);
            EnsureNoReparseTree(full);
        }

        private static FileStream OpenJournal(string path)
        {
            ValidateAncestors(path);
            FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            stream.Seek(0, SeekOrigin.End);
            return stream;
        }

        private static void Journal(FileStream stream, string operation, string status, string source, string destination, string id, string action = "move")
        {
            string line = Json(new Dictionary<string, object> { { "utc", DateTime.UtcNow.ToString("o") }, { "operation", operation }, { "action", action }, { "status", status }, { "source", source }, { "destination", destination }, { "id", id } }) + "\r\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private static Mutex AcquireMutex(string root)
        {
            Mutex mutex = new Mutex(false, @"Local\ReforgerModSync_" + Hash(root.ToUpperInvariant()));
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { mutex.Dispose(); throw new IOException("Another synchronization or restoration is running for this addons folder."); }
            return mutex;
        }

        private static string ReadMetadata(string path, StringBuilder stamp)
        {
            var info = new FileInfo(path);
            if (info.Length > 8 * 1024 * 1024) throw new InvalidDataException("Metadata file is unexpectedly large: " + info.Name);
            string value = File.ReadAllText(path);
            stamp.Append('\n').Append(info.Name).Append('=').Append(Hash(value));
            return value;
        }

        private static bool SamePath(string left, string right) { return String.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        private static string NormalizeId(string id) { if (id == null || !IdPattern.IsMatch(id.Trim())) throw new ArgumentException("Invalid addon ID: " + (id ?? "(missing)")); return id.Trim().ToUpperInvariant(); }
        private static string Value(IDictionary<string, object> data, string key) { object value; return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : ""; }
        private static IDictionary<string, object> ObjectMap(object value) { var data = value as IDictionary<string, object>; if (data == null) throw new InvalidDataException("Expected a JSON object in addon metadata"); return data; }
        private static object[] AsArray(object value) { var array = value as object[]; if (array != null) return array; var list = value as ArrayList; if (list != null) return list.ToArray(); throw new InvalidDataException("Expected an array in addon metadata"); }
        private static object ParseJson(string text) { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(text); }
        private static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(value); }
        private static string Hash(string value) { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        private static string Summary(SyncPlan plan, int removed, int restored, string mode)
        {
            string verb = mode == "disable" ? "Disabled" : mode == "recycle" ? "Recycled" : "Permanently deleted";
            string recovery = mode == "disable" ? " Disabled folders: " + plan.VaultRoot : mode == "recycle" ? " Recover recycled folders through Windows Recycle Bin." : " Permanent deletions cannot be undone by this app.";
            return verb + " " + removed + " extra folder(s); restored " + restored + ". Missing server addons: " + plan.MissingCount + ". Versions were ignored. Join the server in Arma Reforger to download missing addons." + recovery;
        }

        // Modern Shell recycling explicitly requests recycle rather than permanent deletion.
        // There is no permanent-delete fallback; an error or aborted operation stops Apply.
        private static void RecycleDirectory(string path)
        {
            Version windows = Environment.OSVersion.Version;
            if (windows.Major < 6 || (windows.Major == 6 && windows.Minor < 2))
                throw new NotSupportedException("Safe recycling requires Windows 8 or later. Select reversible disable instead.");
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) { RecycleDirectorySta(path); return; }
            Exception error = null;
            Thread worker = new Thread(delegate() { try { RecycleDirectorySta(path); } catch (Exception ex) { error = ex; } });
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join();
            if (error != null) throw new IOException("Windows recycling failed without a permanent-delete fallback: " + error.Message, error);
        }

        private static void RecycleDirectorySta(string path)
        {
            IFileOperation operation = null;
            IShellItem item = null;
            try
            {
                Guid itemId = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out item));
                operation = (IFileOperation)new FileOperation();
                const uint flags = 0x00080000 /* FOFX_RECYCLEONDELETE */ | 0x00100000 /* FOFX_EARLYFAILURE */ | 0x20000000 /* FOFX_ADDUNDORECORD */ | 0x0400 /* FOF_NOERRORUI */ | 0x0004 /* FOF_SILENT */ | 0x0010 /* FOF_NOCONFIRMATION */;
                Marshal.ThrowExceptionForHR(operation.SetOperationFlags(flags));
                Marshal.ThrowExceptionForHR(operation.DeleteItem(item, IntPtr.Zero));
                Marshal.ThrowExceptionForHR(operation.PerformOperations());
                bool aborted;
                Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out aborted));
                if (aborted || Directory.Exists(path)) throw new IOException("Windows did not recycle the addon; the operation was stopped: " + path);
            }
            finally
            {
                if (operation != null) Marshal.FinalReleaseComObject(operation);
                if (item != null) Marshal.FinalReleaseComObject(item);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
        private class FileOperation { }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
            [PreserveSig] int GetParent(out IShellItem parent);
            [PreserveSig] int GetDisplayName(uint flags, out IntPtr name);
            [PreserveSig] int GetAttributes(uint mask, out uint attributes);
            [PreserveSig] int Compare(IShellItem other, uint hints, out int order);
        }

        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            [PreserveSig] int Advise(IntPtr sink, out uint cookie);
            [PreserveSig] int Unadvise(uint cookie);
            [PreserveSig] int SetOperationFlags(uint flags);
            [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            [PreserveSig] int SetProgressDialog(IntPtr dialog);
            [PreserveSig] int SetProperties(IntPtr properties);
            [PreserveSig] int SetOwnerWindow(IntPtr window);
            [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
            [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
            [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
            [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
            [PreserveSig] int DeleteItem(IShellItem item, IntPtr sink);
            [PreserveSig] int DeleteItems(IntPtr items);
            [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
            [PreserveSig] int PerformOperations();
            [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }
    }
}
