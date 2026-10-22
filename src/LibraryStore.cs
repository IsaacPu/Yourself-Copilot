using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace LocalDesktopNote
{
    [DataContract]
    public sealed class LibraryState
    {
        [DataMember] public int Version = 2;
        [DataMember] public List<Preferences> Items = new List<Preferences>();
        [DataMember] public List<Preferences> Archived = new List<Preferences>();
        [DataMember] public string SelectedId;
    }

    internal sealed class LibraryStore
    {
        internal readonly string Folder;
        internal readonly string StatePath;
        internal LibraryState State;
        internal string Notice;
        private bool preserveBackup;
        internal LibraryStore(string folder)
        {
            Folder = folder; StatePath = Path.Combine(folder, "library.json");
            Directory.CreateDirectory(Path.Combine(folder, "images"));
            State = Load();
            if (State.Items == null) State.Items = new List<Preferences>();
            if (State.Archived == null) State.Archived = new List<Preferences>();
        }
        private LibraryState Load()
        {
            if (File.Exists(StatePath))
            {
                try { return ReadLibrary(StatePath); }
                catch { Preserve(StatePath); preserveBackup = true; }
            }
            string backup = StatePath + ".bak";
            if (File.Exists(backup))
            {
                try { var recovered = ReadLibrary(backup); Notice = "已从上一次保存恢复图片列表；异常文件已另存保留。"; return recovered; }
                catch { Preserve(backup); preserveBackup = true; }
            }
            if (preserveBackup) Notice = "图片列表及备份暂时无法读取，未能加载原有列表；异常文件已另存为 library-recovery 文件，可用于恢复。";
            return new LibraryState();
        }
        private static LibraryState ReadLibrary(string path)
        {
            var loaded = Read<LibraryState>(path);
            if (loaded == null || loaded.Version != 2) throw new SerializationException("无法识别的图片列表格式");
            return loaded;
        }
        private void Preserve(string path)
        {
            string recovery = Path.Combine(Folder, "library-recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.Copy(path, recovery, false);
        }
        internal static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        internal static void Write<T>(string path, T value, bool preserveBackup = false)
        {
            string temporary = path + ".tmp";
            try
            {
                using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                if (File.Exists(path)) File.Replace(temporary, path, preserveBackup ? null : path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
        }
        internal void Save() { Write(StatePath, State, preserveBackup); preserveBackup = false; }
        internal bool Migrate(string legacyPath, string fallbackImage)
        {
            if (File.Exists(StatePath) || File.Exists(StatePath + ".bak") || preserveBackup) return false;
            Preferences previous = null;
            if (File.Exists(legacyPath))
                try { previous = Read<Preferences>(legacyPath); } catch { }
            string image = previous != null && File.Exists(previous.ImagePath) ? previous.ImagePath : fallbackImage;
            if (!File.Exists(image)) return false;
            Preferences imported = Import(image);
            if (previous != null)
            {
                imported.X = previous.X; imported.Y = previous.Y; imported.Width = previous.Width;
                imported.Positioned = previous.Positioned; imported.Locked = previous.Locked;
            }
            State.SelectedId = imported.Id; Save(); return true;
        }
        internal string Resolve(Preferences item)
        {
            return Path.IsPathRooted(item.ImagePath) ? item.ImagePath : Path.Combine(Folder, item.ImagePath);
        }
        internal static Size ValidateImage(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("找不到这张图片。", path);
            if (info.Length > 80L * 1024 * 1024) throw new InvalidOperationException("图片超过 80 MB，请选择较小的图片。");
            try
            {
                using (var stream = File.OpenRead(path))
                using (var image = Image.FromStream(stream, false, true))
                {
                    if ((long)image.Width * image.Height > 40000000) throw new InvalidOperationException("图片尺寸过大，请先缩小后再添加。");
                    return image.Size;
                }
            }
            catch (ArgumentException) { throw new InvalidOperationException("这份文件不是可读取的图片，请选择 PNG、JPG、BMP 或 GIF。"); }
        }
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        internal Preferences Import(string path)
        {
            Size size = ValidateImage(path);
            string hash = Hash(path);
            Preferences existing = State.Items.FirstOrDefault(p => p.ContentHash == hash);
            if (existing != null) { State.SelectedId = existing.Id; return existing; }
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp" && extension != ".gif") extension = ".img";
            var item = new Preferences { Id = Guid.NewGuid().ToString("N"), Title = Path.GetFileNameWithoutExtension(path), ContentHash = hash,
                PixelWidth = size.Width, PixelHeight = size.Height };
            item.ImagePath = Path.Combine("images", item.Id + extension);
            File.Copy(path, Resolve(item), false);
            State.Items.Add(item); State.SelectedId = item.Id;
            return item;
        }
        internal void Replace(Preferences item, string path)
        {
            Size size = ValidateImage(path);
            string target = Path.Combine("images", Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            File.Copy(path, Path.Combine(Folder, target), false);
            item.ImagePath = target; item.ContentHash = Hash(path); item.PixelWidth = size.Width; item.PixelHeight = size.Height;
        }
        internal void Archive(Preferences item)
        {
            State.Items.Remove(item); State.Archived.Add(item);
            if (State.SelectedId == item.Id) State.SelectedId = State.Items.Count == 0 ? null : State.Items[0].Id;
        }
        internal Preferences Undo()
        {
            if (State.Archived.Count == 0) return null;
            Preferences item = State.Archived[State.Archived.Count - 1];
            State.Archived.RemoveAt(State.Archived.Count - 1); State.Items.Add(item); State.SelectedId = item.Id;
            return item;
        }
    }
}
