namespace IncrementalFramework.Core
{
    /// <summary>Explicit, versioned module data; never deserialize arbitrary runtime types.</summary>
    public interface ISnapshotSection
    {
        string Id { get; }
        int Version { get; }
        string Capture();
        void Validate(int version, string data);
        void Restore(int version, string data);
    }
}
