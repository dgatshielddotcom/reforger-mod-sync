using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReforgerModSync;

class EngineTests
{
    const string A = "1111111111111111", B = "2222222222222222", C = "3333333333333333", D = "4444444444444444";
    static int assertions;
    static void Main(string[] args)
    {
        string fixtures = Path.Combine(Path.GetTempPath(), "ReforgerModSync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        try
        {
            string root = CreateRoot(fixtures, "roundtrip");
            string a = Addon(root, "required", A, B);
            string b = Addon(root, "dependency", B, C);
            string c = Addon(root, "transitive", C);
            string d = Addon(root, "excess", D);
            string saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, "save.txt"), "KEEP THIS");
            SyncPlan plan = ModEngine.BuildPlan(root, Snapshot(A, B, C));
            Check(plan.DisableCount == 1 && plan.MissingCount == 0 && plan.UnknownCount == 1, "Plan keeps server IDs and unknown folder");
            Check(plan.Rows.Single(r => r.Id == B).Status == "Keep", "Server dependency kept by server ID");
            Check(ModEngine.BuildPlan(root, Snapshot(A)).DisableCount == 3, "Obsolete local dependencies outside server list are extras");
            ModEngine.Apply(plan);
            Check(!Directory.Exists(d) && Directory.Exists(Path.Combine(plan.VaultRoot, Path.GetFileName(d))), "Excess moved to vault");
            Check(Directory.Exists(a) && Directory.Exists(b) && Directory.Exists(c) && File.ReadAllText(Path.Combine(saves, "save.txt")) == "KEEP THIS", "Kept content remains");
            string journal = File.ReadAllText(Path.Combine(plan.VaultRoot, "sync-journal.jsonl"));
            Check(journal.Contains("\"status\":\"pending\"") && journal.Contains("\"status\":\"done\""), "Durable move journal exists");
            SyncPlan restore = ModEngine.BuildPlan(root, Snapshot(D));
            Check(restore.EnableCount == 1 && restore.DisableCount == 3, "Required vaulted addon planned for restore");
            ModEngine.Apply(restore);
            Check(Directory.Exists(d) && !Directory.Exists(a), "Restore and disable in same operation");
            ModEngine.RestoreAll(root);
            Check(Directory.Exists(a) && Directory.Exists(b) && Directory.Exists(c) && Directory.Exists(d), "Restore all returns all known addons");
            Check(ModEngine.RestoreAll(root).Contains("Restored 0"), "Restore all is idempotent");

            root = CreateRoot(fixtures, "changed");
            a = Addon(root, "required", A); d = Addon(root, "excess", D);
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            File.WriteAllText(Path.Combine(d, "ServerData.json"), "{\"id\":\"" + D + "\",\"revision\":{\"version\":\"2.0\"}}");
            Throws(() => ModEngine.Apply(plan), "Changed metadata aborts preview");
            Check(Directory.Exists(d) && !Directory.Exists(plan.VaultRoot), "Changed preview performed no move");
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            Addon(root, "new", B);
            Throws(() => ModEngine.Apply(plan), "Changed candidate set aborts preview");
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            plan.Rows.First(r => r.Status == "Disable").Folder = a;
            Throws(() => ModEngine.Apply(plan), "Tampered plan aborts");

            root = CreateRoot(fixtures, "conflicts");
            a = Addon(root, "required", A); d = Addon(root, "excess", D);
            string duplicate = Addon(root, "duplicate", D);
            string mismatch = Addon(root, "mismatch", B);
            File.WriteAllText(Path.Combine(mismatch, "ServerData.json"), "{\"id\":\"" + C + "\",\"revision\":{\"version\":\"1.0\"}}");
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            Check(plan.UnknownCount == 3 && plan.DisableCount == 0, "Duplicate/conflicting IDs left unknown");
            ModEngine.Apply(plan);
            Check(Directory.Exists(d) && Directory.Exists(duplicate) && Directory.Exists(mismatch), "Unknown mods stay put");

            root = CreateRoot(fixtures, "collision");
            a = Addon(root, "required", A); d = Addon(root, "excess", D);
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            Directory.CreateDirectory(plan.VaultRoot);
            Directory.CreateDirectory(Path.Combine(plan.VaultRoot, Path.GetFileName(d)));
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            Throws(() => ModEngine.Apply(plan), "Destination collision aborts before moves");
            Check(Directory.Exists(d), "Collision never overwrites");

            root = CreateRoot(fixtures, "serverdata-dependency");
            a = Addon(root, "required", A); b = Addon(root, "dependency", B); d = Addon(root, "excess", D);
            File.WriteAllText(Path.Combine(a, "ServerData.json"), "{\"id\":\"" + A + "\",\"revision\":{\"version\":\"0.9\",\"dependencies\":[{\"assetId\":\"" + B + "\"}]}}");
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            Check(plan.DisableCount == 2 && plan.VersionCount == 0 && plan.Rows.Single(r => r.Id == A).Status == "Keep", "Versions ignored and local-only dependency treated as extra");
            ServerSnapshot old = Snapshot(A); old.FetchedUtc = DateTime.UtcNow.AddMinutes(-16);
            plan = ModEngine.BuildPlan(root, old);
            Throws(() => ModEngine.Apply(plan), "Stale snapshot cannot mutate");
            Throws(() => ModEngine.BuildPlan(root, new ServerSnapshot()), "Empty server list fails closed");
            Throws(() => ModEngine.BuildPlan(fixtures, Snapshot(A)), "Non-addon root rejected");
            Throws(() => ModEngine.BuildPlan("addons", Snapshot(A)), "Relative root rejected");
            Throws(() => ModEngine.BuildPlan(root, Snapshot("invalid")), "Invalid remote addon ID rejected");
            root = CreateRoot(fixtures, "permanent-delete");
            a = Addon(root, "required", A); d = Addon(root, "excess", D);
            string unknown = Path.Combine(root, "unknown"); Directory.CreateDirectory(unknown); File.WriteAllText(Path.Combine(unknown, "important.txt"), "keep");
            plan = ModEngine.BuildPlan(root, Snapshot(A));
            string deleted = ModEngine.Apply(plan, "delete");
            Check(Directory.Exists(a) && !Directory.Exists(d) && File.Exists(Path.Combine(unknown, "important.txt")), "Permanent delete removes only recognized extras");
            Check(deleted.Contains("Permanently deleted 1") && deleted.Contains("cannot be undone"), "Permanent result clearly states irreversibility");
            journal = File.ReadAllText(Path.Combine(plan.VaultRoot, "sync-journal.jsonl"));
            Check(journal.Contains("\"action\":\"delete\"") && journal.Contains("\"status\":\"done\""), "Permanent deletion journal recorded");
            if (args.Contains("--recycle"))
            {
                root = CreateRoot(fixtures, "recycle");
                a = Addon(root, "required", A); d = Addon(root, "excess", D);
                plan = ModEngine.BuildPlan(root, Snapshot(A));
                string recycled = ModEngine.Apply(plan, "recycle");
                Check(Directory.Exists(a) && !Directory.Exists(d) && recycled.Contains("Recycled 1"), "Recycle works from MTA caller and keeps required mod");
            }
            root = CreateRoot(fixtures, "confirmed-empty");
            a = Addon(root, "first", A); d = Addon(root, "second", D);
            unknown = Path.Combine(root, "saves"); Directory.CreateDirectory(unknown); File.WriteAllText(Path.Combine(unknown, "save.txt"), "keep");
            ServerSnapshot empty = Snapshot();
            empty.Id = "manual-fixture"; empty.Host = "192.0.2.44"; empty.Port = 2001;
            Throws(() => ModEngine.BuildPlan(root, empty), "Unconfirmed vanilla server cannot plan all-mod deletion");
            empty.ConfirmedEmpty = true;
            plan = ModEngine.BuildPlan(root, empty);
            Check(plan.DisableCount == 2 && plan.MissingCount == 0 && plan.UnknownCount == 1, "Confirmed empty plans recognized extras only");
            Check(plan.Snapshot.Id == empty.Id && plan.Snapshot.Host == empty.Host && plan.Snapshot.Port == empty.Port && plan.Snapshot.ConfirmedEmpty, "Snapshot clone preserves selected server identity and empty confirmation");
            empty.Host = "192.0.2.45";
            Check(plan.Snapshot.Host == "192.0.2.44", "Preview keeps independent server identity copy");
            plan.Snapshot.ConfirmedEmpty = false;
            Throws(() => ModEngine.Apply(plan, "delete"), "Editing empty confirmation invalidates preview");
            Check(Directory.Exists(a) && Directory.Exists(d), "Changed empty confirmation causes no deletion");
            plan = ModEngine.BuildPlan(root, empty);
            plan.Snapshot.Host = "192.0.2.46";
            Throws(() => ModEngine.Apply(plan, "delete"), "Changing selected server identity invalidates preview");
            plan = ModEngine.BuildPlan(root, empty);
            ModEngine.Apply(plan, "delete");
            Check(!Directory.Exists(a) && !Directory.Exists(d) && File.ReadAllText(Path.Combine(unknown, "save.txt")) == "keep", "Confirmed empty permanently removes recognized fixture mods and keeps saves");
            ServerSnapshot unavailable = Snapshot(); unavailable.Mods = null; unavailable.ConfirmedEmpty = true;
            Throws(() => ModEngine.BuildPlan(root, unavailable), "Confirmation cannot allow an unavailable mod list");
            TestJunction(fixtures);
            Console.WriteLine("PASS: " + assertions + " assertions; all mutations confined to " + fixtures);
        }
        finally
        {
            // Cleanup is restricted to the one random test directory created above; never real addons.
            string absolute = Path.GetFullPath(fixtures);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            if (absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("ReforgerModSync-tests-", StringComparison.Ordinal))
                Directory.Delete(absolute, true);
        }
    }

    static void TestJunction(string fixtures)
    {
        string root = CreateRoot(fixtures, "junction");
        string a = Addon(root, "required", A);
        string d = Addon(root, "excess", D);
        string target = Path.Combine(fixtures, "junction-target"); Directory.CreateDirectory(target);
        string link = Path.Combine(d, "linked-content");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = System.Diagnostics.Process.Start(start)) { process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Could not create fixture junction"); }
        try
        {
            SyncPlan plan = ModEngine.BuildPlan(root, Snapshot(A));
            Check(plan.UnknownCount == 1 && plan.DisableCount == 0, "Addon containing junction is protected");
            ModEngine.Apply(plan);
            Check(Directory.Exists(d) && Directory.Exists(target), "Junction addon not moved");
        }
        finally { Directory.Delete(link); }
        string linkedRoot = Path.Combine(fixtures, "linked-addons-parent"); Directory.CreateDirectory(linkedRoot);
        string rootLink = Path.Combine(linkedRoot, "addons");
        start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + rootLink + "\" \"" + root + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = System.Diagnostics.Process.Start(start)) { process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Could not create fixture root junction"); }
        try { Throws(() => ModEngine.BuildPlan(rootLink, Snapshot(A)), "Root junction rejected"); }
        finally { Directory.Delete(rootLink); }
    }

    static string CreateRoot(string fixtures, string name) { string root = Path.Combine(fixtures, name, "addons"); Directory.CreateDirectory(root); return root; }
    static string Addon(string root, string name, string id, params string[] dependencies)
    {
        string folder = Path.Combine(root, name + "_" + id); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "renamed.gproj"), "GameProject {\r\n GUID \"" + id + "\"\r\n TITLE \"" + name + "\"\r\n Dependencies { \"58D0FB3206B6F859\" " + String.Join(" ", dependencies.Select(x => "\"" + x + "\"")) + " }\r\n}");
        File.WriteAllText(Path.Combine(folder, "ServerData.json"), "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"revision\":{\"version\":\"1.0\",\"dependencies\":[]}}");
        File.WriteAllText(Path.Combine(folder, "data.pak"), "test fixture");
        return folder;
    }
    static ServerSnapshot Snapshot(params string[] ids) { return new ServerSnapshot { Name = "Fixture server", Source = "test", FetchedUtc = DateTime.UtcNow, Mods = ids.Select(id => new ServerMod { Id = id, Name = id, Version = "1.0" }).ToList() }; }
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); assertions++; }
    static void Throws(Action action, string label) { try { action(); } catch (Exception ex) { if (ex is IOException || ex is ArgumentException || ex is InvalidOperationException) { assertions++; return; } throw; } throw new Exception("FAIL: " + label + " did not throw"); }
}
