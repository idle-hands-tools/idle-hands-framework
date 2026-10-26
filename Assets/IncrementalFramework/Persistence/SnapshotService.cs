using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IncrementalFramework
{
    public sealed class SimulationSnapshot
    {
        public const int CurrentVersion = 2;
        public int Version { get; set; } = CurrentVersion;
        public DateTime SavedAtUtc { get; set; }
        public string ContentVersion { get; set; }
        public string ContentHash { get; set; }
        public SimulationState State { get; set; }
        public List<SnapshotSectionData> Sections { get; set; } = new List<SnapshotSectionData>();
    }

    public sealed class SnapshotSectionData
    {
        public string Id { get; set; }
        public int Version { get; set; }
        public string Data { get; set; }
    }

    public interface ISnapshotMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        JObject Migrate(JObject snapshot);
    }

    public sealed class VersionOneSnapshotMigration : ISnapshotMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;
        public JObject Migrate(JObject snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot["state"] == null && snapshot["simulation"] != null)
                snapshot["state"] = snapshot["simulation"].DeepClone();
            snapshot.Remove("simulation");
            snapshot["version"] = ToVersion;
            return snapshot;
        }
    }

    public static class SnapshotService
    {
        public const int MaximumSnapshotBytes = 16 * 1024 * 1024;
        internal static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Culture = CultureInfo.InvariantCulture,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            DateParseHandling = DateParseHandling.DateTime,
            FloatParseHandling = FloatParseHandling.Double,
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 64,
            Converters = new List<JsonConverter> { new BigDoubleJsonConverter() }
        };

        public static SimulationSnapshot Capture(Simulation simulation) => Capture(simulation, SystemClock.Instance);
        public static SimulationSnapshot Capture(Simulation simulation, IClock clock) =>
            Capture(simulation, (clock ?? throw new ArgumentNullException(nameof(clock))).UtcNow);
        public static SimulationSnapshot Capture(Simulation simulation, DateTime utcNow)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            RequireUtc(utcNow);
            var snapshot = new SimulationSnapshot
            {
                SavedAtUtc = utcNow, State = simulation.ExportState(),
                ContentVersion = ContentFingerprint.CurrentVersion, ContentHash = ContentFingerprint.Compute(simulation.Content)
            };
            foreach (var section in simulation.SnapshotSections)
            {
                string data = section.Capture();
                if (data == null || data.Length > MaximumSnapshotBytes)
                    throw new JsonSerializationException("Invalid or oversized module snapshot data.");
                section.Validate(section.Version, data);
                snapshot.Sections.Add(new SnapshotSectionData { Id = section.Id, Version = section.Version, Data = data });
            }
            Validate(snapshot);
            return snapshot;
        }
        public static Simulation Restore(ContentDatabase content, SimulationSnapshot snapshot, int eventBudget = 10000,
            ModuleBuilder modules = null)
        {
            Validate(snapshot);
            if (snapshot.ContentVersion != ContentFingerprint.CurrentVersion ||
                !string.Equals(snapshot.ContentHash, ContentFingerprint.Compute(content), StringComparison.Ordinal))
                throw new InvalidOperationException("Snapshot content differs; an explicit content migration is required.");
            var simulation = Simulation.FromState(content, snapshot.State, eventBudget);
            if (modules != null) modules.Attach(simulation);
            RestoreSections(simulation, snapshot.Sections);
            return simulation;
        }
        public static string Serialize(SimulationSnapshot snapshot)
        {
            Validate(snapshot);
            string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented, CreateSettings());
            if (Encoding.UTF8.GetByteCount(json) > MaximumSnapshotBytes) throw new JsonSerializationException("Snapshot exceeds size limit.");
            return json;
        }
        public static SimulationSnapshot Deserialize(string json, IEnumerable<ISnapshotMigration> migrations = null)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new JsonSerializationException("Empty snapshot.");
            if (json.Length > MaximumSnapshotBytes || Encoding.UTF8.GetByteCount(json) > MaximumSnapshotBytes)
                throw new JsonSerializationException("Snapshot exceeds size limit.");
            JObject root;
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonSerializationException("Trailing snapshot content.");
            }
            RejectMetadata(root);
            int version = ReadVersion(root);
            if (version < 1 || version > SimulationSnapshot.CurrentVersion) throw new JsonSerializationException("Unsupported save version.");
            var steps = new Dictionary<int, ISnapshotMigration>();
            if (migrations != null)
                foreach (var step in migrations)
                {
                    if (step == null || step.FromVersion < 1 || step.ToVersion <= step.FromVersion ||
                        step.ToVersion > SimulationSnapshot.CurrentVersion || steps.ContainsKey(step.FromVersion))
                        throw new ArgumentException("Invalid or duplicate migration.");
                    steps.Add(step.FromVersion, step);
                }
            while (version < SimulationSnapshot.CurrentVersion)
            {
                if (steps.TryGetValue(version, out var step))
                {
                    root = step.Migrate((JObject)root.DeepClone()) ?? throw new JsonSerializationException("Migration returned null.");
                    version = step.ToVersion;
                }
                else if (version == 1)
                {
                    // Version one called the state object "simulation"; the runtime DTO is otherwise unchanged.
                    var legacy = new VersionOneSnapshotMigration();
                    root = legacy.Migrate(root);
                    version = legacy.ToVersion;
                }
                else throw new JsonSerializationException("No migration is available.");
                root["version"] = version;
            }
            RejectMetadata(root);
            if (root["savedAtUtc"] == null || root["state"] == null)
                throw new JsonSerializationException("Snapshot is missing required fields.");
            var snapshot = root.ToObject<SimulationSnapshot>(JsonSerializer.Create(CreateSettings()));
            Validate(snapshot);
            return snapshot;
        }

        public static void Save(string path, Simulation simulation, IClock clock) =>
            Save(path, simulation, (clock ?? throw new ArgumentNullException(nameof(clock))).UtcNow);

        public static void Save(string path, Simulation simulation, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Save path is required.", nameof(path));
            string json = Serialize(Capture(simulation, utcNow));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string staging = fullPath + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(staging, fullPath, fullPath + ".bak");
                else File.Move(staging, fullPath);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        public static Simulation Load(string path, ContentDatabase content, DateTime utcNow,
            double offlineCapSeconds, int eventBudget = 10000, IEnumerable<ISnapshotMigration> migrations = null,
            ModuleBuilder modules = null)
        {
            RequireUtc(utcNow);
            if (double.IsNaN(offlineCapSeconds) || double.IsInfinity(offlineCapSeconds) || offlineCapSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(offlineCapSeconds));
            SimulationSnapshot snapshot;
            Simulation simulation;
            try
            {
                snapshot = Deserialize(ReadBounded(path), migrations);
                simulation = Restore(content, snapshot, eventBudget, modules);
            }
            catch (Exception error) when ((error is IOException || error is JsonException || error is ArgumentException || error is InvalidOperationException) && File.Exists(path + ".bak"))
            {
                snapshot = Deserialize(ReadBounded(path + ".bak"), migrations);
                simulation = Restore(content, snapshot, eventBudget, modules);
            }
            AdvanceOffline(simulation, snapshot, utcNow, offlineCapSeconds);
            return simulation;
        }

        public static Simulation Load(string path, ContentDatabase content, IClock clock,
            double offlineCapSeconds, int eventBudget = 10000, IEnumerable<ISnapshotMigration> migrations = null,
            ModuleBuilder modules = null) =>
            Load(path, content, (clock ?? throw new ArgumentNullException(nameof(clock))).UtcNow, offlineCapSeconds, eventBudget, migrations, modules);

        public static SimulationSnapshot ReadSnapshot(string path, IEnumerable<ISnapshotMigration> migrations = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Save path is required.", nameof(path));
            try { return Deserialize(ReadBounded(path), migrations); }
            catch (Exception error) when ((error is IOException || error is JsonException || error is ArgumentException ||
                error is InvalidOperationException) && File.Exists(path + ".bak"))
            {
                return Deserialize(ReadBounded(path + ".bak"), migrations);
            }
        }

        public static OfflineResumeResult Resume(ContentDatabase content, SimulationSnapshot snapshot, IClock clock,
            double offlineCapSeconds, int eventBudget = 10000, ModuleBuilder modules = null)
        {
            DateTime now = (clock ?? throw new ArgumentNullException(nameof(clock))).UtcNow;
            RequireUtc(now);
            ValidateCap(offlineCapSeconds);
            return AdvanceOffline(Restore(content, snapshot, eventBudget, modules), snapshot, now, offlineCapSeconds);
        }

        private static void RestoreSections(Simulation simulation, IReadOnlyList<SnapshotSectionData> saved)
        {
            var registered = new Dictionary<string, ISnapshotSection>(StringComparer.Ordinal);
            foreach (var section in simulation.SnapshotSections)
                registered.Add(section.Id, section);
            if (registered.Count != saved.Count)
                throw new InvalidOperationException("Snapshot module sections differ; all saved and registered sections are required.");
            foreach (var section in saved)
                if (!registered.ContainsKey(section.Id))
                    throw new InvalidOperationException("Unknown snapshot module section: " + section.Id);
            // No module is mutated until every section has passed its version/data validation.
            foreach (var section in saved) registered[section.Id].Validate(section.Version, section.Data);
            foreach (var section in saved) registered[section.Id].Restore(section.Version, section.Data);
        }

        private static OfflineResumeResult AdvanceOffline(Simulation simulation, SimulationSnapshot snapshot,
            DateTime utcNow, double offlineCapSeconds)
        {
            double seconds = Math.Min(offlineCapSeconds, Math.Max(0, (utcNow - snapshot.SavedAtUtc).TotalSeconds));
            // PendingTarget is already-earned but unprocessed time, independent of this new offline interval.
            double target = Math.Max(simulation.Time, simulation.PendingTarget ?? simulation.Time) + seconds;
            if (double.IsInfinity(target)) throw new InvalidOperationException("Offline target overflows.");
            return new OfflineResumeResult(simulation, seconds, target);
        }

        private static void ValidateCap(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        private static string ReadBounded(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumSnapshotBytes) throw new JsonSerializationException("Snapshot exceeds size limit.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096))
                {
                    var result = new StringBuilder();
                    var buffer = new char[4096];
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (result.Length + read > MaximumSnapshotBytes) throw new JsonSerializationException("Snapshot exceeds size limit.");
                        result.Append(buffer, 0, read);
                    }
                    return result.ToString();
                }
            }
        }

        private static int ReadVersion(JObject root)
        {
            var token = root["version"];
            if (token == null || token.Type != JTokenType.Integer) throw new JsonSerializationException("Missing or invalid save version.");
            try { return token.Value<int>(); }
            catch (Exception error) when (error is OverflowException || error is FormatException || error is InvalidCastException)
            { throw new JsonSerializationException("Invalid save version.", error); }
        }

        private static void RejectMetadata(JToken token)
        {
            if (token is JObject obj)
                foreach (var property in obj.Properties())
                {
                    if (property.Name.StartsWith("$", StringComparison.Ordinal))
                        throw new JsonSerializationException("Snapshot metadata is not permitted.");
                    RejectMetadata(property.Value);
                }
            else if (token is JArray array)
                foreach (var child in array) RejectMetadata(child);
        }

        private static void Validate(SimulationSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != SimulationSnapshot.CurrentVersion || snapshot.State == null)
                throw new JsonSerializationException("Invalid snapshot or unsupported version.");
            RequireUtc(snapshot.SavedAtUtc);
            if (string.IsNullOrWhiteSpace(snapshot.ContentVersion) || snapshot.ContentHash == null || snapshot.ContentHash.Length != 64)
                throw new JsonSerializationException("Snapshot has no valid content fingerprint.");
            foreach (char character in snapshot.ContentHash)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    throw new JsonSerializationException("Snapshot has an invalid content fingerprint.");
            if (snapshot.Sections == null) throw new JsonSerializationException("Snapshot sections cannot be null.");
            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in snapshot.Sections)
                if (section == null || string.IsNullOrWhiteSpace(section.Id) || section.Version < 1 ||
                    section.Data == null || section.Data.Length > MaximumSnapshotBytes || !sectionIds.Add(section.Id))
                    throw new JsonSerializationException("Invalid or duplicate snapshot section.");
        }
        private static void RequireUtc(DateTime value)
        {
            if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Wall-clock timestamps must be UTC.");
        }
    }
}
