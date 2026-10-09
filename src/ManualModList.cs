using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ReforgerModSync
{
    public static class ManualModList
    {
        const int MaxChars = 8 * 1024 * 1024;
        static readonly Regex IdPattern = new Regex(@"\A[0-9a-fA-F]{16}\z", RegexOptions.CultureInvariant);

        public static ServerSnapshot Load(string path, bool confirmedEmpty)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new IOException("The imported mod-list file is no longer available. Import it again.");
            if (new FileInfo(path).Length > MaxChars) throw new IOException("The mod-list file is too large (maximum 8 MB).");
            return Parse(File.ReadAllText(path), Path.GetFullPath(path), confirmedEmpty);
        }

        public static ServerSnapshot Parse(string json, string source, bool confirmedEmpty)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxChars) throw new InvalidDataException("The mod-list file is empty or too large.");
            object value;
            try { value = new JavaScriptSerializer { MaxJsonLength = MaxChars }.DeserializeObject(json); }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException || ex is InvalidOperationException)) throw;
                throw new InvalidDataException("The file is not valid JSON. Import an exported mod list or server configuration.");
            }

            string name = Path.GetFileName(source);
            var root = value as IDictionary<string, object>;
            if (root != null)
            {
                object game;
                if (root.TryGetValue("game", out game))
                {
                    root = game as IDictionary<string, object>;
                    if (root == null) throw new InvalidDataException("The server configuration has an invalid game object.");
                }
                else if (root.TryGetValue("server", out game))
                {
                    root = game as IDictionary<string, object>;
                    if (root == null) throw new InvalidDataException("The mod list has an invalid server object.");
                }
                string serverName = Optional(root, "name");
                if (!string.IsNullOrWhiteSpace(serverName)) name = serverName;
                if (!root.TryGetValue("mods", out value)) throw new InvalidDataException("No mods array was found. Supported formats: a mod array, {mods:[...]}, or a Reforger server configuration with game.mods.");
            }
            var entries = value as IList;
            if (entries == null) throw new InvalidDataException("The mod list must be a JSON array.");
            var snapshot = new ServerSnapshot { Name = name, Source = source, FetchedUtc = DateTime.UtcNow, Mods = new List<ServerMod>(), ConfirmedEmpty = entries.Count == 0 && confirmedEmpty };
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object entry in entries)
            {
                var row = entry as IDictionary<string, object>;
                string id = entry as string;
                if (row != null)
                {
                    id = Optional(row, "modId");
                    if (string.IsNullOrEmpty(id)) id = Optional(row, "id");
                    string alternate = Optional(row, "id");
                    if (!string.IsNullOrEmpty(alternate) && !string.IsNullOrEmpty(Optional(row, "modId")) && !string.Equals(id, alternate, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Conflicting modId and id fields were found in the imported list.");
                }
                if (id == null || !IdPattern.IsMatch(id)) throw new InvalidDataException("Every imported mod must have a 16-character hexadecimal modId or id. Invalid entry " + (snapshot.Mods.Count + 1) + ".");
                if (!ids.Add(id)) throw new InvalidDataException("The imported list contains duplicate mod ID " + id + ".");
                string modName = row == null ? id : Optional(row, "name");
                snapshot.Mods.Add(new ServerMod { Id = id.ToUpperInvariant(), Name = string.IsNullOrWhiteSpace(modName) ? id.ToUpperInvariant() : modName, Version = "" });
            }
            // Confirmation comes only from the UI, never from an imported JSON property.
            return snapshot;
        }

        static string Optional(IDictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) ? value as string ?? "" : "";
        }
    }
}
