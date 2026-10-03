param([string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe', [string]$ReplayPython='python')
# NRD is the standard backend. Historical backend comparison/timing scripts are archived.
& (Join-Path $PSScriptRoot 'Test-StochasticSSR.ps1') -Unity $Unity -ReplayPython $ReplayPython
