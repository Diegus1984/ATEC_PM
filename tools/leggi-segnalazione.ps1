[CmdletBinding(DefaultParameterSetName = 'PerId')]
param(
    [Parameter(Position = 0, ParameterSetName = 'PerId')]
    [int]$Id,

    [Parameter(ParameterSetName = 'Elenco')]
    [int]$Ultime = 15,

    [Parameter(ParameterSetName = 'Aperte')]
    [switch]$Aperte
)

$pyScript = Join-Path $PSScriptRoot 'segnalazioni.py'

if ($Aperte) {
    python $pyScript --aperte
} elseif ($PSCmdlet.ParameterSetName -eq 'Elenco') {
    python $pyScript -n $Ultime
} elseif ($Id -gt 0) {
    python $pyScript $Id
} else {
    python $pyScript -n 15
}
