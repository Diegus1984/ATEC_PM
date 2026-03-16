using System;
using System.Windows.Forms;

namespace ATEC.PM.Client.Controls;

/// <summary>
/// Wrapper AxHost per il controllo ActiveX eDrawings.
/// Richiede eDrawings Viewer installato sul PC.
/// GUID version-independent: 22945A69-1191-4DCF-9E6F-409BDE94D101
/// </summary>
public class EDrawingHost : AxHost
{
    public event Action<object>? ControlLoaded;
    private bool _isLoaded;

    public EDrawingHost() : base("22945A69-1191-4DCF-9E6F-409BDE94D101")
    {
        _isLoaded = false;
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        if (!_isLoaded)
        {
            _isLoaded = true;
            var ctrl = GetOcx();
            if (ctrl != null)
                ControlLoaded?.Invoke(ctrl);
        }
    }
}
