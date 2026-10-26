using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Project.Core.Integration
{
    public sealed class ApplicationDiagnostics : MonoBehaviour
    {
        [Serializable] private sealed class Entry
        {
            public string utc;
            public string session;
            public string eventName;
            public string objectId;
            public string detail;
        }
        private readonly List<string> pending = new List<string>();
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        private float nextFlush;
        private int writtenCharacters;
        public string LogFilePath { get; private set; }

        private void Awake()
        {
            try
            {
                var folder = Path.Combine(Application.persistentDataPath, "diagnostics");
                Directory.CreateDirectory(folder);
                LogFilePath = Path.Combine(folder, "session-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + sessionId + ".jsonl");
                var files = new List<FileInfo>();
                foreach (var path in Directory.GetFiles(folder, "session-*.jsonl")) files.Add(new FileInfo(path));
                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (var i = 4; i < files.Count; i++)
                    if (Path.GetDirectoryName(files[i].FullName) == Path.GetFullPath(folder)) files[i].Delete();
            }
            catch (Exception error) { Debug.LogWarning("[SpatialSense Diagnostics] " + error.Message); enabled = false; }
        }

        public void Record(string eventName, string objectId, string detail)
        {
            if (!enabled || writtenCharacters > 1000000 || pending.Count >= 256) return;
            pending.Add(JsonUtility.ToJson(new Entry
            {
                utc = DateTime.UtcNow.ToString("O"), session = sessionId, eventName = eventName,
                objectId = objectId, detail = detail
            }));
        }

        private void Update()
        {
            if (Time.unscaledTime < nextFlush) return;
            nextFlush = Time.unscaledTime + 1f;
            Flush();
        }

        private void Flush()
        {
            if (pending.Count == 0 || string.IsNullOrEmpty(LogFilePath)) return;
            try
            {
                var text = string.Join("\n", pending) + "\n";
                File.AppendAllText(LogFilePath, text);
                writtenCharacters += text.Length;
            }
            catch (Exception error) { Debug.LogWarning("[SpatialSense Diagnostics] " + error.Message); enabled = false; }
            pending.Clear();
        }

        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnDestroy() => Flush();
    }
}
