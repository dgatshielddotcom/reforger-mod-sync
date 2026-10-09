using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using ReforgerModSync;

public static class ServerTests
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
    private static string fixture;
    private static int passed;

    public static int Main(string[] args)
    {
        try
        {
            fixture = File.ReadAllText(args.Length > 0 ? args[0] : Path.Combine("tests", "fixtures", "server-detail.json"));
            Dictionary<string, object> good = Fresh();
            ServerSnapshot parsed = ServerSource.Parse(Json.Serialize(good));
            Check(parsed.Mods.Count == 193, "complete observed 193-mod fixture");
            Check(parsed.Name.StartsWith("JSOTF |"), "preserved server name");
            Check(parsed.Source == ServerSource.ServerUrl, "verified source address");
            Check(parsed.Id == ServerSource.ServerId && parsed.Host == ServerSource.ExpectedHost && parsed.Port == ServerSource.ExpectedPort, "snapshot records selected server identity and address");
            Check(Math.Abs((DateTime.UtcNow - parsed.FetchedUtc).TotalSeconds) < 5, "snapshot records fetch time");
            Check(parsed.Mods[0].Id == "65AD7D0D9941A380" && parsed.Mods[0].Version == "1.5.39", "correct mod identity and version");

            RejectText("invalid JSON", "{broken");
            RejectText("empty response", "");
            RejectText("unexpected root", "[]");
            Reject("failed service status", delegate(Dictionary<string, object> root) { root["status"] = "error"; });
            Reject("stale index", delegate(Dictionary<string, object> root) { Dataset(root)["stale"] = true; });
            Reject("warming index", delegate(Dictionary<string, object> root) { Dataset(root)["warming"] = true; });
            Reject("old collection", delegate(Dictionary<string, object> root) { Dataset(root)["lastCollectionAt"] = Stamp(DateTime.UtcNow.AddMinutes(-16)); });
            Reject("future collection", delegate(Dictionary<string, object> root) { Dataset(root)["lastCollectionAt"] = Stamp(DateTime.UtcNow.AddMinutes(5)); });
            Reject("missing collection timestamp", delegate(Dictionary<string, object> root) { Dataset(root).Remove("lastCollectionAt"); });
            Reject("ambiguous timestamp timezone", delegate(Dictionary<string, object> root) { Dataset(root)["lastCollectionAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"); });
            Reject("old snapshot age", delegate(Dictionary<string, object> root) { Dataset(root)["snapshotAgeSeconds"] = 901; });
            Reject("negative snapshot age", delegate(Dictionary<string, object> root) { Dataset(root)["snapshotAgeSeconds"] = -1; });
            Reject("string freshness flag", delegate(Dictionary<string, object> root) { Dataset(root)["stale"] = "false"; });
            Reject("wrong room identity", delegate(Dictionary<string, object> root) { Server(root)["id"] = Guid.NewGuid().ToString(); });
            Reject("wrong server host", delegate(Dictionary<string, object> root) { Server(root)["host"] = "127.0.0.1"; });
            Reject("wrong server port", delegate(Dictionary<string, object> root) { Server(root)["port"] = 10501; });
            Dictionary<string, object> renamed = Fresh();
            Server(renamed)["name"] = "Renamed server";
            Check(ServerSource.Parse(Json.Serialize(renamed)).Name == "Renamed server", "name changes allowed when UUID and expected address match");
            Reject("offline server", delegate(Dictionary<string, object> root) { Server(root)["online"] = false; });
            Reject("old server observation", delegate(Dictionary<string, object> root) { Server(root)["lastSeen"] = Stamp(DateTime.UtcNow.AddMinutes(-16)); });
            Reject("empty reported mods", delegate(Dictionary<string, object> root) { Server(root)["mods"] = new object[0]; Server(root)["modCount"] = 0; Summary(root)["count"] = 0; });
            Reject("reported count mismatch", delegate(Dictionary<string, object> root) { Server(root)["modCount"] = 192; });
            Reject("summary count mismatch", delegate(Dictionary<string, object> root) { Summary(root)["count"] = 192; });
            Reject("fractional count", delegate(Dictionary<string, object> root) { Server(root)["modCount"] = 193.5; });
            Reject("missing mod ID", delegate(Dictionary<string, object> root) { FirstMod(root).Remove("id"); });
            Reject("invalid mod ID", delegate(Dictionary<string, object> root) { FirstMod(root)["id"] = "../../dangerous"; });
            Reject("trailing newline in mod ID", delegate(Dictionary<string, object> root) { FirstMod(root)["id"] = "65AD7D0D9941A380\n"; });
            Reject("duplicate mod ID", delegate(Dictionary<string, object> root) { SecondMod(root)["id"] = FirstMod(root)["id"]; });
            Reject("case-insensitive duplicate ID", delegate(Dictionary<string, object> root) { SecondMod(root)["id"] = ((string)FirstMod(root)["id"]).ToLowerInvariant(); });
            AcceptOptional("missing mod version", delegate(Dictionary<string, object> root) { FirstMod(root).Remove("version"); }, "");
            AcceptOptional("empty mod version", delegate(Dictionary<string, object> root) { FirstMod(root)["version"] = " "; }, "");
            AcceptOptional("non-text mod version", delegate(Dictionary<string, object> root) { FirstMod(root)["version"] = 123; }, "");
            AcceptOptional("malformed display mod version", delegate(Dictionary<string, object> root) { FirstMod(root)["version"] = "\ninvalid"; }, "");
            AcceptOptional("changed version still permits GUID cleanup", delegate(Dictionary<string, object> root) { FirstMod(root)["version"] = "999.0"; }, "999.0");
            Dictionary<string, object> withoutNames = Fresh();
            FirstMod(withoutNames).Remove("name");
            Check(ServerSource.Parse(Json.Serialize(withoutNames)).Mods[0].Name == "65AD7D0D9941A380", "missing name falls back to GUID");
            Reject("invalid mod object", delegate(Dictionary<string, object> root) { Mods(root)[0] = "not a mod"; });
            Reject("malformed dependency list", delegate(Dictionary<string, object> root) { FirstMod(root)["dependencies"] = "FFFFFFFFFFFFFFFF"; });
            Reject("invalid dependency ID", delegate(Dictionary<string, object> root) { FirstMod(root)["dependencies"] = new object[] { "INVALID" }; });
            Reject("missing reported dependency", delegate(Dictionary<string, object> root) { FirstMod(root)["dependencies"] = new object[] { "FFFFFFFFFFFFFFFF" }; });

            string otherId = "dcfa282d-53cd-4c82-afd1-013007ad9bea";
            Dictionary<string, object> generic = Fresh();
            Server(generic)["id"] = otherId;
            Server(generic)["name"] = "Another community server";
            Server(generic)["host"] = "203.0.113.12";
            Server(generic)["port"] = 2001;
            ServerCandidate other = new ServerCandidate { Id = otherId, Name = "Old name", Host = "203.0.113.12", Port = 2001, Online = true };
            ServerSnapshot genericParsed = ServerSource.Parse(Json.Serialize(generic), other);
            Check(genericParsed.Id == otherId && genericParsed.Host == "203.0.113.12" && genericParsed.Port == 2001 && genericParsed.Mods.Count == 193, "generic selected server has no JSOTF requirement");
            ServerCandidate preview = new ServerCandidate { Id = otherId, Name = "Server preview" };
            Check(ServerSource.Parse(Json.Serialize(generic), preview).Host == "203.0.113.12", "preview without address resolves verified UUID detail");
            Dictionary<string, object> hiddenAddress = Fresh();
            Server(hiddenAddress).Remove("host");
            Server(hiddenAddress).Remove("port");
            Check(ServerSource.Parse(Json.Serialize(hiddenAddress), new ServerCandidate { Id = ServerSource.ServerId }).Host == "", "address omitted by index remains unknown");
            RejectAction("wrong selected UUID", delegate { ServerSource.Parse(Json.Serialize(generic), new ServerCandidate { Id = ServerSource.ServerId }); });
            RejectAction("wrong selected address", delegate { ServerSource.Parse(Json.Serialize(generic), new ServerCandidate { Id = otherId, Host = "203.0.113.13", Port = 2001 }); });
            RejectAction("invalid selected UUID", delegate { ServerSource.Parse(Json.Serialize(generic), new ServerCandidate { Id = "../wrong" }); });
            Reject("invalid returned host", delegate(Dictionary<string, object> root) { Server(root)["host"] = "https://unknown/path"; });
            Reject("invalid returned port", delegate(Dictionary<string, object> root) { Server(root)["port"] = 65536; });
            Check(ServerSource.CanonicalUrl(other) == "https://reforgermods.net/servers/" + otherId, "canonical server link uses only validated UUID");
            RejectAction("invalid canonical link ID", delegate { ServerSource.CanonicalUrl(new ServerCandidate { Id = "https://evil.invalid" }); });

            Dictionary<string, object> searchRoot = SearchFixture();
            List<ServerCandidate> candidates = ServerSource.ParseSearch(Json.Serialize(searchRoot));
            Check(candidates.Count == 1 && candidates[0].Id == ServerSource.ServerId && candidates[0].Host == "" && candidates[0].Port == 0, "search parses preview without address");
            RejectSearch("duplicate search IDs", delegate(Dictionary<string, object> root) { object first = ((object[])root["data"])[0]; root["data"] = new object[] { first, first }; ((Dictionary<string, object>)root["meta"])["shownServers"] = 2; ((Dictionary<string, object>)root["meta"])["totalServers"] = 2; });
            RejectSearch("inconsistent search count", delegate(Dictionary<string, object> root) { ((Dictionary<string, object>)root["meta"])["shownServers"] = 2; });
            RejectSearch("stale search index", delegate(Dictionary<string, object> root) { Dataset(root)["stale"] = true; });
            RejectSearch("invalid search server UUID", delegate(Dictionary<string, object> root) { ((Dictionary<string, object>)((object[])root["data"])[0])["id"] = "invalid"; });
            Dictionary<string, object> emptySearch = SearchFixture();
            emptySearch["data"] = new object[0];
            ((Dictionary<string, object>)emptySearch["meta"])["shownServers"] = 0;
            ((Dictionary<string, object>)emptySearch["meta"])["totalServers"] = 0;
            Check(ServerSource.ParseSearch(Json.Serialize(emptySearch)).Count == 0, "empty search returns no candidates without creating an empty mod snapshot");
            RejectAction("arbitrary website input", delegate { ServerSource.Search("https://example.com/servers/" + otherId); });
            RejectAction("lookalike provider hostname", delegate { ServerSource.Search("https://reforgermods.net.evil.invalid/servers/" + otherId); });
            RejectAction("URL credentials input", delegate { ServerSource.Search("https://user:password@reforgermods.net/servers/" + otherId); });
            RejectAction("unsupported IP lookup", delegate { ServerSource.Search("108.171.199.178:10500"); });
            RejectAction("unsupported arbitrary BattleMetrics link", delegate { ServerSource.Search("https://www.battlemetrics.com/servers/reforger/123456"); });
            if (args.Length > 1 && args[1] == "--live")
            {
                ServerSnapshot live = ServerSource.Fetch();
                Check(live.Mods.Count > 0, "live fetch passes all identity and freshness checks");
                Console.WriteLine("Live server: " + live.Name + "; " + live.Mods.Count + " mods.");
                List<ServerCandidate> liveCandidates = ServerSource.Search("JSOTF");
                Check(liveCandidates.Count > 1, "live name search returns selectable candidates");
                ServerCandidate second = liveCandidates.Find(delegate(ServerCandidate item) { return item.Id == otherId; });
                Check(second != null, "live search includes second distinct server");
                ServerSnapshot secondLive = ServerSource.Fetch(second);
                Check(secondLive.Id == otherId && secondLive.Mods.Count > 0, "live generic fetch reads selected different server");
                Check(ServerSource.Search("https://reforgermods.net/servers/" + otherId)[0].Id == otherId, "live server URL resolves selected UUID");
                Console.WriteLine("Other live server: " + secondLive.Name + "; " + secondLive.Mods.Count + " mods.");
            }
            Console.WriteLine("Server provider: " + passed + " checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, object> Fresh()
    {
        Dictionary<string, object> root = (Dictionary<string, object>)Json.DeserializeObject(fixture);
        Dataset(root)["lastCollectionAt"] = Stamp(DateTime.UtcNow);
        Dataset(root)["snapshotAgeSeconds"] = 0;
        Server(root)["lastSeen"] = Stamp(DateTime.UtcNow);
        return root;
    }

    private static string Stamp(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ssZ"); }
    private static Dictionary<string, object> Dataset(Dictionary<string, object> root) { return (Dictionary<string, object>)root["dataset"]; }
    private static Dictionary<string, object> Server(Dictionary<string, object> root) { return (Dictionary<string, object>)root["server"]; }
    private static Dictionary<string, object> Summary(Dictionary<string, object> root) { return (Dictionary<string, object>)Server(root)["modSummary"]; }
    private static object[] Mods(Dictionary<string, object> root) { return (object[])Server(root)["mods"]; }
    private static Dictionary<string, object> FirstMod(Dictionary<string, object> root) { return (Dictionary<string, object>)Mods(root)[0]; }
    private static Dictionary<string, object> SecondMod(Dictionary<string, object> root) { return (Dictionary<string, object>)Mods(root)[1]; }

    private static void Reject(string name, Action<Dictionary<string, object>> change)
    {
        Dictionary<string, object> root = Fresh();
        change(root);
        RejectText(name, Json.Serialize(root));
    }

    private static Dictionary<string, object> SearchFixture()
    {
        Dictionary<string, object> root = Fresh();
        Dictionary<string, object> row = Server(root);
        row.Remove("host");
        row.Remove("port");
        row.Remove("mods");
        root.Remove("server");
        root["data"] = new object[] { row };
        root["meta"] = new Dictionary<string, object> { { "shownServers", 1 }, { "totalServers", 1 } };
        return root;
    }

    private static void RejectSearch(string name, Action<Dictionary<string, object>> change)
    {
        Dictionary<string, object> root = SearchFixture();
        change(root);
        RejectAction(name, delegate { ServerSource.ParseSearch(Json.Serialize(root)); });
    }

    private static void RejectAction(string name, Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidOperationException ex) { rejected = !String.IsNullOrWhiteSpace(ex.Message); }
        Check(rejected, "reject " + name);
    }

    private static void AcceptOptional(string name, Action<Dictionary<string, object>> change, string expectedVersion)
    {
        Dictionary<string, object> root = Fresh();
        change(root);
        ServerSnapshot parsed = ServerSource.Parse(Json.Serialize(root));
        Check(parsed.Mods.Count == 193 && parsed.Mods[0].Version == expectedVersion, "accept " + name);
    }

    private static void RejectText(string name, string json)
    {
        bool rejected = false;
        try { ServerSource.Parse(json); }
        catch (InvalidOperationException ex) { rejected = !String.IsNullOrWhiteSpace(ex.Message); }
        Check(rejected, "reject " + name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
    }
}
