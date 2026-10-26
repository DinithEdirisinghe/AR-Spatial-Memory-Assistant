using System;
using System.IO;

namespace SpatialSense.Persistence
{
    public static class SavedObjectCatalog
    {
        public static readonly System.Collections.Generic.IReadOnlyList<string> Names =
            Array.AsReadOnly(new[] { "My keys", "My wallet", "My remote" });

        public static string FileNameFor(string objectName)
        {
            switch (objectName)
            {
                case "My keys": return "spatialsense-my-keys-v1.json";
                case "My wallet": return "spatialsense-my-wallet-v1.json";
                case "My remote": return "spatialsense-my-remote-v1.json";
                default: throw new ArgumentException("Unknown object slot.", nameof(objectName));
            }
        }

        public static bool IsKnownName(string name)
        {
            foreach (var candidate in Names) if (candidate == name) return true;
            return false;
        }
    }

    [Serializable]
    public sealed class SavedObjectRecord
    {
        public int version;
        public string objectName;
        public string anchorUuid;
        public string roomUuid;
        public string savedAtUtc;
        public bool forgotten;
        public string observationId;
        public string observationOrigin;
        public string observedAtUtc;
        public float identityConfidence;
        public float positionConfidence;

        public bool IsValid()
        {
            return version == 1 && SavedObjectCatalog.IsKnownName(objectName)
                && DateTimeOffset.TryParse(savedAtUtc, out _)
                && (forgotten || (Guid.TryParse(anchorUuid, out var anchor) && anchor != Guid.Empty
                && Guid.TryParse(roomUuid, out var room) && room != Guid.Empty
                ));
        }
    }

    public static class AtomicAnchorRecordStore
    {
        public static void Write(string path, string contents)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(contents))
                throw new ArgumentException("A path and record contents are required.");
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
