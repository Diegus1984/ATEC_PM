using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Serilog;

namespace ATEC.PM.Client.Views.MoM;

// Traccia eventi MoM su Serilog + Output di Visual Studio. Utile per freeze/crash su dettaglio verbale.
internal static class MomDebugTrace
{
    private static readonly object WindowGate = new();
    private static int _windowCount;
    private static DateTime _windowStartUtc = DateTime.UtcNow;

    // Verbose=false (default): silenzia INFO/EVT ad alta frequenza per non sporcare i log in produzione.
    // WARN/ERROR e il rilevamento LOOP restano SEMPRE attivi. Per ridebuggare: MomDebugTrace.Verbose = true.
    public static bool Verbose { get; set; }

    public static string LogFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATEC_PM", "Logs");

    public static void Info(string message, [CallerMemberName] string caller = "")
    {
        if (Verbose) Write("INFO", caller, message, null);
    }

    public static void Warn(string message, [CallerMemberName] string caller = "")
        => Write("WARN", caller, message, null);

    public static void Error(Exception ex, string message, [CallerMemberName] string caller = "")
        => Write("ERROR", caller, message, ex);

    public static void Event(string eventName, string detail, [CallerMemberName] string caller = "")
    {
        int count = BumpWindowCount();   // conteggio SEMPRE attivo (rilevamento loop)
        if (Verbose)
            Write("EVT", caller, $"{eventName} | {detail} | events3s=#{count}", null);
        if (count >= 25)
            Write("LOOP", caller, $"Possibile loop: {count} eventi MoM in 3s — ultimo={eventName} — {detail}", null);
    }

    private static int BumpWindowCount()
    {
        lock (WindowGate)
        {
            if ((DateTime.UtcNow - _windowStartUtc).TotalSeconds > 3)
            {
                _windowCount = 0;
                _windowStartUtc = DateTime.UtcNow;
            }
            _windowCount++;
            return _windowCount;
        }
    }

    private static void Write(string level, string caller, string message, Exception? ex)
    {
        string full = $"[MoM/{caller}] {message}";
        Debug.WriteLine(full);
        if (ex != null)
            Log.Error(ex, full);
        else if (level is "WARN" or "LOOP")
            Log.Warning(full);
        else
            Log.Debug(full);
    }
}
