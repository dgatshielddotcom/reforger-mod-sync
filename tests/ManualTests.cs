using System;
using System.IO;
using System.Linq;
using ReforgerModSync;

class ManualTests
{
    const string A = "1111111111111111", B = "2222222222222222";
    static int checks;
    static void Main()
    {
        Check(ManualModList.Parse("[{\"modId\":\"" + A + "\",\"name\":\"Alpha\",\"version\":\"old\"}]", "fixture.json", false).Mods.Single().Version == "", "versions ignored");
        Check(ManualModList.Parse("[\"" + A + "\",\"" + B + "\"]", "fixture.json", false).Mods.Count == 2, "plain ID array");
        Check(ManualModList.Parse("{\"mods\":[{\"id\":\"" + A + "\"}]}", "fixture.json", false).Mods.Count == 1, "mods object");
        var config = ManualModList.Parse("{\"game\":{\"name\":\"Other server\",\"mods\":[{\"modId\":\"" + A + "\"}]}}", "fixture.json", false);
        Check(config.Name == "Other server" && config.Mods.Count == 1, "server config");
        Check(ManualModList.Parse("{\"server\":{\"name\":\"Export\",\"mods\":[{\"id\":\"" + A + "\"}]}}", "fixture.json", false).Name == "Export", "API export");
        Check(!ManualModList.Parse("{\"mods\":[],\"ConfirmedEmpty\":true}", "fixture.json", false).ConfirmedEmpty, "imported confirmation ignored");
        Check(ManualModList.Parse("[]", "fixture.json", true).ConfirmedEmpty, "UI empty confirmation");
        Reject("{}", "missing array"); Reject("null", "null list"); Reject("{", "malformed JSON"); Reject("[\"wrong\"]", "invalid ID");
        Reject("[\"" + A + "\",\"" + A + "\"]", "duplicate ID");
        Reject("[{\"modId\":\"" + A + "\",\"id\":\"" + B + "\"}]", "conflicting IDs");
        Reject("{\"game\":[],\"mods\":[]}", "malformed config");
        string fixtureDir = Path.Combine(Path.GetTempPath(), "ReforgerModSync-manual-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            string file = Path.Combine(fixtureDir, "list.json");
            File.WriteAllText(file, "[\"" + A + "\"]");
            Check(ManualModList.Load(file, false).Mods[0].Id == A, "load selected file");
            File.WriteAllText(file, "[\"" + B + "\"]");
            Check(ManualModList.Load(file, false).Mods[0].Id == B, "reload changed file for cleanup");
        }
        finally { Directory.Delete(fixtureDir, true); }
        Console.WriteLine("Manual import: " + checks + " checks passed.");
    }
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Reject(string json, string label) { try { ManualModList.Parse(json, "fixture.json", false); } catch (InvalidDataException) { checks++; return; } throw new Exception("Expected rejection: " + label); }
}
