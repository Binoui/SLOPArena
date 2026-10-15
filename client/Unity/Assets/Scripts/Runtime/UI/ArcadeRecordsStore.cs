using System;
using System.IO;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.UI
{
    /// <summary>Separate local file; settings resets and account changes never erase Arcade records.</summary>
    public sealed class ArcadeRecordsStore
    {
        private static ArcadeRecordsStore _instance;
#if UNITY_EDITOR
        public static string VerificationDirectory { get; set; }
#endif
        public static ArcadeRecordsStore Instance
        {
            get
            {
                string directory = Application.persistentDataPath;
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(VerificationDirectory)) directory = VerificationDirectory;
#endif
                string path = System.IO.Path.Combine(directory, "arcade-records.json");
                if (_instance == null || _instance.Path != path) _instance = new ArcadeRecordsStore(path);
                return _instance;
            }
        }

        public string Path { get; }
        public ArcadeRecordBook Book { get; private set; }
        public string Feedback { get; private set; }
        private bool _readFailed;

        public ArcadeRecordsStore(string path)
        {
            Path = path; Book = new ArcadeRecordBook();
            try { if (File.Exists(path)) Book = ArcadeRecordBook.Deserialize(File.ReadAllText(path)); }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            { _readFailed = true; Feedback = "Previous records could not be read. They will be kept as a backup."; }
        }

        public ArcadeRecordUpdate Bank(ArcadeRecordKey key, ulong returns, ArcadeRecordProvenance provenance)
        {
            var update = Book.BankSlopball(key, returns, provenance, DateTime.UtcNow);
            Save();
            return update;
        }

        public ArcadeRecordUpdate BankCourse(ArcadeRecordKey key, CharacterCourseState result, ArcadeRecordProvenance provenance)
        {
            var update = Book.BankCourse(key, result, provenance, DateTime.UtcNow);
            Save();
            return update;
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                if (_readFailed && File.Exists(Path))
                {
                    File.Move(Path, Path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                    _readFailed = false;
                }
                string temporary = Path + ".tmp";
                File.WriteAllText(temporary, Book.Serialize());
                if (File.Exists(Path)) File.Replace(temporary, Path, Path + ".backup");
                else File.Move(temporary, Path);
                Feedback = null;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            { Feedback = "Your record is kept for this session, but could not be saved to disk."; }
        }
    }
}
