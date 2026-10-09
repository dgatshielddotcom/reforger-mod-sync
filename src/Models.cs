using System;
using System.Collections.Generic;

namespace ReforgerModSync
{
    public class ServerCandidate
    {
        public string Id, Name, Host;
        public int Port, Players, ModCount;
        public bool Online;
    }

    public class ServerMod
    {
        public string Id, Name, Version;
    }

    public class ServerSnapshot
    {
        public string Id, Host, Name, Source;
        public int Port;
        public bool ConfirmedEmpty;
        public DateTime FetchedUtc;
        public List<ServerMod> Mods = new List<ServerMod>();
    }

    public class LocalMod
    {
        public string Id, Name, Version, Folder, Warning;
        public bool Disabled;
        public List<string> Dependencies = new List<string>();
        internal string MetadataSignature;
    }

    public class PlanRow
    {
        public string Status, Action, Name, Id, LocalVersion, ServerVersion, Folder;
        public LocalMod Local;
    }

    public class SyncPlan
    {
        public string AddonsRoot, VaultRoot;
        public ServerSnapshot Snapshot;
        public List<PlanRow> Rows = new List<PlanRow>();
        public List<LocalMod> Active = new List<LocalMod>();
        public List<LocalMod> Disabled = new List<LocalMod>();
        public int DisableCount, EnableCount, MissingCount, VersionCount, UnknownCount;
        internal string ScanSignature, RowsSignature, SnapshotSignature;
    }
}
