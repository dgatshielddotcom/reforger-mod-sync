using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ReforgerModSync
{
    public static class ServerSource
    {
        public const string ServerId = "18131282-bda6-46b9-b135-f654ee5507b1";
        public const string ServerUrl = "https://api.reforgermods.net/v2/servers/" + ServerId;
        public const string BattleMetricsUrl = "https://www.battlemetrics.com/servers/reforger/41533435";
        public const string ExpectedHost = "108.171.199.178";
        public const int ExpectedPort = 10500;
        private const string DefaultName = "JSOTF | PvE REALISM | Public Operation Server 1 | gg/ jsotf HOSTED BY KOVA";
        private const int MaxResponseChars = 8 * 1024 * 1024;
        private static readonly Regex ModIdPattern = new Regex("^[0-9A-Fa-f]{16}$", RegexOptions.CultureInvariant);

        public static ServerSnapshot Fetch()
        {
            return Fetch(DefaultCandidate());
        }

        public static ServerSnapshot Fetch(ServerCandidate target)
        {
            string id = TargetId(target);
            return Parse(Download(ApiUrl(id)), target);
        }

        public static string CanonicalUrl(ServerCandidate target)
        {
            return "https://reforgermods.net/servers/" + TargetId(target);
        }

        public static List<ServerCandidate> Search(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0 || query.Length > 256)
                throw Invalid("Enter a server name, ReforgerMods server link, or server UUID.");
            Guid uuid;
            if (Guid.TryParse(query, out uuid))
                return OneCandidate(uuid.ToString("D"), null);
            Uri link;
            if (Uri.TryCreate(query, UriKind.Absolute, out link) && query.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                if ((link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp) || !String.IsNullOrEmpty(link.UserInfo))
                    throw Invalid("Use a normal HTTPS ReforgerMods server link or search by server name.");
                string host = link.Host.ToLowerInvariant();
                string path = link.AbsolutePath.TrimEnd('/');
                string idText = null;
                if (host == "reforgermods.net" || host == "www.reforgermods.net")
                {
                    if (path.StartsWith("/servers/", StringComparison.Ordinal)) idText = path.Substring(9);
                }
                else if (host == "api.reforgermods.net")
                {
                    if (path.StartsWith("/v2/servers/", StringComparison.Ordinal)) idText = path.Substring(12);
                }
                else if (host == "battlemetrics.com" || host == "www.battlemetrics.com")
                {
                    if (path == "/servers/reforger/41533435") return OneCandidate(ServerId, DefaultCandidate());
                    throw Invalid("BattleMetrics blocks automatic access to its server API without a subscription. Search using the server's name, or paste its ReforgerMods server link instead.");
                }
                if (idText == null || !Guid.TryParse(idText, out uuid))
                    throw Invalid("That link is not a ReforgerMods server page. Search by server name or use https://reforgermods.net/servers/<server UUID>.");
                return OneCandidate(uuid.ToString("D"), null);
            }
            IPAddress address;
            if (IPAddress.TryParse(query, out address) || Regex.IsMatch(query, "^[0-9.]+:[0-9]+$", RegexOptions.CultureInvariant))
                throw Invalid("The public index searches server names, not IP addresses. Search by the server's name, then choose the matching server.");
            return ParseSearch(Download("https://api.reforgermods.net/v2/servers?perPage=100&includeOffline=false&sort=players&search=" + Uri.EscapeDataString(query)));
        }

        // Search results may omit addresses. Use the UUID to identify the server.
        public static List<ServerCandidate> ParseSearch(string json)
        {
            Dictionary<string, object> root = ReadRoot(json);
            ValidateDataset(root, DateTime.UtcNow);
            IList rows = Array(root, "data");
            Dictionary<string, object> meta = Object(root, "meta");
            if (rows.Count > 100 || Integer(meta, "shownServers") != rows.Count || Integer(meta, "totalServers") < rows.Count)
                throw Invalid("The server search response was incomplete. Try a more specific server name.");
            List<ServerCandidate> candidates = new List<ServerCandidate>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object value in rows)
            {
                Dictionary<string, object> row = value as Dictionary<string, object>;
                if (row == null) throw Invalid("The server search returned an invalid entry. Try scanning again.");
                ServerCandidate candidate = Candidate(row);
                if (!ids.Add(candidate.Id)) throw Invalid("The server search returned duplicate server IDs. Try scanning again.");
                candidates.Add(candidate);
            }
            return candidates;
        }

        private static List<ServerCandidate> OneCandidate(string id, ServerCandidate expected)
        {
            Dictionary<string, object> root = ReadRoot(Download(ApiUrl(id)));
            ValidateDataset(root, DateTime.UtcNow);
            Dictionary<string, object> server = Object(root, "server");
            ServerCandidate candidate = Candidate(server);
            if (!String.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
                throw Invalid("The server service returned a different server ID. No mod changes are allowed.");
            if (expected != null) MatchAddress(candidate, expected);
            return new List<ServerCandidate> { candidate };
        }

        private static string Download(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.UserAgent = "ReforgerModSync/1.0 (Windows; personal mod manager)";
                request.Accept = "application/json";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.AllowAutoRedirect = false;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                using (System.Threading.Timer networkDeadline = new System.Threading.Timer(
                    delegate(object state) { request.Abort(); }, null, 30000, System.Threading.Timeout.Infinite))
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw Invalid("The mod-list service returned HTTP " + (int)response.StatusCode + ". Try scanning again later.");
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        StringBuilder json = new StringBuilder();
                        char[] buffer = new char[8192];
                        int read;
                        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (json.Length + read > MaxResponseChars)
                                throw Invalid("The mod-list response is unexpectedly large. No mod changes are allowed.");
                            json.Append(buffer, 0, read);
                        }
                        return json.ToString();
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    int status = (int)response.StatusCode;
                    response.Dispose();
                    if (status == 429)
                        throw Invalid("The mod-list service is busy or rate limiting requests. Wait a minute, then scan again.");
                    if (status == 404)
                        throw Invalid("The tracked server could not be found. Its listing may have changed. No mod changes are allowed.");
                    throw Invalid("The mod-list service returned HTTP " + status + ". Check your connection and try scanning again later.");
                }
                if (ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.RequestCanceled)
                    throw Invalid("Reading the live server mod list timed out. Try scanning again.");
                throw Invalid("Could not reach the live mod-list service. Check your internet connection and try scanning again.");
            }
            catch (IOException)
            {
                throw Invalid("The live mod-list download was interrupted. Try scanning again.");
            }
        }

        public static ServerSnapshot Parse(string json)
        {
            return Parse(json, DefaultCandidate());
        }

        public static ServerSnapshot Parse(string json, ServerCandidate target)
        {
            string expectedId = TargetId(target);
            Dictionary<string, object> root = ReadRoot(json);
            DateTime fetched = DateTime.UtcNow;
            ValidateDataset(root, fetched);
            Dictionary<string, object> server = Object(root, "server");
            ServerCandidate actual = Candidate(server);
            if (!String.Equals(actual.Id, expectedId, StringComparison.OrdinalIgnoreCase))
                throw Invalid("The returned server ID does not match the selected server. No mod changes are allowed.");
            MatchAddress(actual, target);
            if (!actual.Online)
                throw Invalid("The selected server is offline in the live index. No mod changes are allowed until it is online.");
            RequireFresh(Text(server, "lastSeen"), fetched, "server listing");

            IList rows = Array(server, "mods");
            int count = actual.ModCount;
            int summaryCount = Integer(Object(server, "modSummary"), "count");
            if (count == 0)
                throw Invalid("The server reports no mods, which does not prove it is vanilla. Use an authoritative manual mod list for a server with no mods.");
            if (count != rows.Count || summaryCount != count)
                throw Invalid("The server mod list is incomplete: its reported counts disagree. No mod changes are allowed.");

            List<ServerMod> mods = new List<ServerMod>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dependencyIds = new List<string>();
            foreach (object value in rows)
            {
                Dictionary<string, object> row = value as Dictionary<string, object>;
                if (row == null)
                    throw Invalid("The server mod list contains an invalid entry. No mod changes are allowed.");
                string id = Text(row, "id");
                string modName = OptionalText(row, "name");
                string version = OptionalText(row, "version");
                if (id.Length != 16 || !ModIdPattern.IsMatch(id))
                    throw Invalid("The server mod list contains a missing or invalid mod ID. No mod changes are allowed.");
                if (String.IsNullOrWhiteSpace(modName)) modName = id.ToUpperInvariant();
                if (String.IsNullOrWhiteSpace(version) || version.Length > 100 || HasControlCharacters(version)) version = "";
                if (!ids.Add(id))
                    throw Invalid("The server mod list contains duplicate mod IDs. No mod changes are allowed.");
                mods.Add(new ServerMod { Id = id.ToUpperInvariant(), Name = modName, Version = version });
                object dependencies;
                if (row.TryGetValue("dependencies", out dependencies))
                {
                    IList entries = dependencies as IList;
                    if (entries == null)
                        throw Invalid("The server mod list contains invalid dependency data. No mod changes are allowed.");
                    foreach (object dependency in entries)
                    {
                        string dependencyId = dependency as string;
                        if (dependencyId == null || dependencyId.Length != 16 || !ModIdPattern.IsMatch(dependencyId))
                            throw Invalid("The server mod list contains an invalid dependency ID. No mod changes are allowed.");
                        dependencyIds.Add(dependencyId);
                    }
                }
            }
            foreach (string dependencyId in dependencyIds)
                if (!ids.Contains(dependencyId))
                    throw Invalid("The reported server list omits dependency " + dependencyId.ToUpperInvariant() + ". No mod changes are allowed.");

            return new ServerSnapshot { Id = actual.Id, Host = actual.Host, Port = actual.Port, Name = actual.Name,
                Source = ApiUrl(actual.Id), FetchedUtc = fetched, Mods = mods };
        }

        private static Dictionary<string, object> ReadRoot(string json)
        {
            if (String.IsNullOrWhiteSpace(json) || json.Length > MaxResponseChars)
                throw Invalid("The mod-list response was empty or unexpectedly large. No mod changes are allowed.");
            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = MaxResponseChars;
                serializer.RecursionLimit = 100;
                root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (ArgumentException)
            {
                throw Invalid("The mod-list service returned malformed JSON. No mod changes are allowed.");
            }
            catch (InvalidOperationException)
            {
                throw Invalid("The mod-list service returned malformed JSON. No mod changes are allowed.");
            }
            if (root == null || Text(root, "status") != "success")
                throw Invalid("The mod-list service did not report a successful response. No mod changes are allowed.");

            return root;
        }

        private static void ValidateDataset(Dictionary<string, object> root, DateTime fetched)
        {
            Dictionary<string, object> dataset = Object(root, "dataset");
            if (Boolean(dataset, "warming") || Boolean(dataset, "stale"))
                throw Invalid("The server index is warming up or stale. Wait a minute and scan again.");
            RequireFresh(Text(dataset, "lastCollectionAt"), fetched, "server index");
            double age = Number(dataset, "snapshotAgeSeconds");
            if (age < 0 || age > 900)
                throw Invalid("The server index is older than 15 minutes. Try scanning again later.");

        }

        private static ServerCandidate DefaultCandidate()
        {
            return new ServerCandidate { Id = ServerId, Name = DefaultName, Host = ExpectedHost, Port = ExpectedPort, Online = true };
        }

        private static string TargetId(ServerCandidate target)
        {
            Guid id;
            if (target == null || !Guid.TryParse(target.Id, out id) || target.Port < 0 || target.Port > 65535)
                throw Invalid("Choose a valid server from the search results before scanning.");
            if (!String.IsNullOrEmpty(target.Host) && Uri.CheckHostName(target.Host) == UriHostNameType.Unknown)
                throw Invalid("The selected server has an invalid address. Search for it again.");
            return id.ToString("D");
        }

        private static string ApiUrl(string id) { return "https://api.reforgermods.net/v2/servers/" + id; }

        private static ServerCandidate Candidate(Dictionary<string, object> server)
        {
            Guid id;
            if (!Guid.TryParse(Text(server, "id"), out id))
                throw Invalid("The server index contains an invalid server UUID. Try searching again.");
            string name = OptionalText(server, "name");
            string host = "";
            int port = 0;
            object field;
            if (server.TryGetValue("host", out field) && field != null)
            {
                host = field as string;
                if (host == null || (host.Length > 0 && Uri.CheckHostName(host) == UriHostNameType.Unknown))
                    throw Invalid("The server index contains an invalid host address. No mod changes are allowed.");
            }
            if (server.TryGetValue("port", out field) && field != null) port = Integer(server, "port");
            if (port > 65535 || (host.Length > 0 && port == 0))
                throw Invalid("The server index contains an invalid server port. No mod changes are allowed.");
            int players = 0;
            if (server.TryGetValue("players", out field) && field != null) players = Integer(server, "players");
            return new ServerCandidate { Id = id.ToString("D"), Name = String.IsNullOrWhiteSpace(name) ? id.ToString("D") : name,
                Host = host, Port = port, Players = players, ModCount = Integer(server, "modCount"), Online = Boolean(server, "online") };
        }

        private static void MatchAddress(ServerCandidate actual, ServerCandidate expected)
        {
            if ((!String.IsNullOrEmpty(expected.Host) && !String.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase)) ||
                (expected.Port != 0 && actual.Port != expected.Port))
                throw Invalid("The returned address does not match the selected server. Search for the server again before changing mods.");
        }

        private static bool HasControlCharacters(string value)
        {
            foreach (char character in value)
                if (Char.IsControl(character)) return true;
            return false;
        }

        private static void RequireFresh(string value, DateTime now, string label)
        {
            DateTimeOffset timestamp;
            if (!Regex.IsMatch(value, "(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant) ||
                !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
                throw Invalid("The " + label + " has no valid freshness timestamp. No mod changes are allowed.");
            TimeSpan age = now - timestamp.UtcDateTime;
            if (age.TotalMinutes > 15 || age.TotalMinutes < -2)
                throw Invalid("The " + label + " is older than 15 minutes or has an invalid future timestamp. Try scanning again.");
        }

        private static object Required(Dictionary<string, object> value, string key)
        {
            object field;
            if (value == null || !value.TryGetValue(key, out field) || field == null)
                throw Invalid("The mod-list response is missing '" + key + "'. No mod changes are allowed.");
            return field;
        }

        private static string Text(Dictionary<string, object> value, string key)
        {
            string text = Required(value, key) as string;
            if (text == null)
                throw Invalid("The mod-list response has an invalid '" + key + "' field. No mod changes are allowed.");
            return text;
        }

        private static string OptionalText(Dictionary<string, object> value, string key)
        {
            object field;
            if (!value.TryGetValue(key, out field)) return "";
            return field as string ?? "";
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> value, string key)
        {
            Dictionary<string, object> result = Required(value, key) as Dictionary<string, object>;
            if (result == null)
                throw Invalid("The mod-list response has an invalid '" + key + "' field. No mod changes are allowed.");
            return result;
        }

        private static IList Array(Dictionary<string, object> value, string key)
        {
            IList result = Required(value, key) as IList;
            if (result == null)
                throw Invalid("The mod-list response has an invalid '" + key + "' list. No mod changes are allowed.");
            return result;
        }

        private static bool Boolean(Dictionary<string, object> value, string key)
        {
            object result = Required(value, key);
            if (!(result is bool))
                throw Invalid("The mod-list response has an invalid '" + key + "' flag. No mod changes are allowed.");
            return (bool)result;
        }

        private static double Number(Dictionary<string, object> value, string key)
        {
            object result = Required(value, key);
            if (!(result is int) && !(result is long) && !(result is decimal) && !(result is double))
                throw Invalid("The mod-list response has an invalid '" + key + "' number. No mod changes are allowed.");
            double number = Convert.ToDouble(result, CultureInfo.InvariantCulture);
            if (Double.IsNaN(number) || Double.IsInfinity(number))
                throw Invalid("The mod-list response has an invalid '" + key + "' number. No mod changes are allowed.");
            return number;
        }

        private static int Integer(Dictionary<string, object> value, string key)
        {
            double number = Number(value, key);
            if (number != Math.Truncate(number) || number < 0 || number > Int32.MaxValue)
                throw Invalid("The mod-list response has an invalid '" + key + "' count. No mod changes are allowed.");
            return (int)number;
        }

        private static InvalidOperationException Invalid(string message)
        {
            return new InvalidOperationException(message);
        }
    }
}
