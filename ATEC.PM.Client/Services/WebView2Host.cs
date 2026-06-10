using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Ambiente WebView2 condiviso — creato una sola volta al primo editor aperto.
/// </summary>
public static class WebView2Host
{
    private static CoreWebView2Environment? _environment;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment != null)
            return _environment;

        await InitLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_environment == null)
            {
                string folder = Path.Combine(Path.GetTempPath(), "ATEC_PM_WebView2");
                _environment = await CoreWebView2Environment
                    .CreateAsync(userDataFolder: folder)
                    .ConfigureAwait(false);
            }

            return _environment;
        }
        finally
        {
            InitLock.Release();
        }
    }
}
