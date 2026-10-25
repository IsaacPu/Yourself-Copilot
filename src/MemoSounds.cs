using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading;

namespace LocalDesktopNote
{
    // Optional feedback: disk loading and playback never run on the UI thread.
    // Keep one cached player and one in-flight sound for the process lifetime.
    internal static class MemoSounds
    {
        private static SoundPlayer dropPlayer;
        private static int pending;
        private static long lastStartedTicks;

        internal static void PlayDrop()
        {
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
            try
            {
                if (!ThreadPool.QueueUserWorkItem(PlayDropWorker))
                    Interlocked.Exchange(ref pending, 0);
            }
            catch { Interlocked.Exchange(ref pending, 0); }
        }

        private static void PlayDropWorker(object state)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (lastStartedTicks != 0 && now - lastStartedTicks < Stopwatch.Frequency * 350 / 1000) return;
                lastStartedTicks = now;
                if (dropPlayer == null)
                {
                    string path = Path.Combine(Storage.Folder, "sounds", "paper-drop.wav");
                    if (!File.Exists(path) || new FileInfo(path).Length > 512 * 1024) return;
                    var candidate = new SoundPlayer(path);
                    try { candidate.Load(); dropPlayer = candidate; }
                    catch { candidate.Dispose(); return; }
                }
                // Synchronous only on this background worker; the gate stays
                // held until the short sample finishes, preventing overlaps.
                dropPlayer.PlaySync();
            }
            catch
            {
                if (dropPlayer != null)
                {
                    try { dropPlayer.Dispose(); } catch { }
                    dropPlayer = null;
                }
                // Missing devices, invalid samples and muted systems must never
                // turn a successful memo save into an application error.
            }
            finally { Interlocked.Exchange(ref pending, 0); }
        }
    }
}
