<#
    Toggle the agent kill switch in Azure App Configuration.

    Usage:
        .\toggle-agent.ps1              toggles the current state
        .\toggle-agent.ps1 -Show        shows current state without changing it
        .\toggle-agent.ps1 -On          force enable
        .\toggle-agent.ps1 -Off         force disable
#>

param(
    [string]$Store   = "claims-appconfig",
    [string]$Feature = "AgentEnabled",
    [switch]$Show,
    [switch]$On,
    [switch]$Off
)

$ErrorActionPreference = "Stop"

function Read-State {
    $json = az appconfig feature show --name $Store --feature $Feature -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        throw "Feature '$Feature' not found in store '$Store'. Verify with: az appconfig feature list --name $Store -o table"
    }

    $obj = $json | ConvertFrom-Json

    if ($null -ne $obj.enabled) { return [bool]$obj.enabled }
    if ($null -ne $obj.state)   { return $obj.state -eq "on" }

    throw "Failed to read feature state. Raw response:`n$($obj | ConvertTo-Json -Depth 5)"
}

function Write-State($enabled, $label) {
    $text  = if ($enabled) { "ON   (agent processes claims)" } else { "OFF  (claims routed to manual processing)" }
    $color = if ($enabled) { "Green" } else { "Red" }

    Write-Host ""
    Write-Host ("{0,-12} " -f $label) -NoNewline
    Write-Host $text -ForegroundColor $color
}

# -- Read current state ----------------------------------------------------

Write-Host ""
Write-Host "Store:   $Store"
Write-Host "Feature: $Feature"

$current = Read-State
Write-State $current "Current:"

if ($Show) {
    Write-Host ""
    exit 0
}

# -- Determine target state ------------------------------------------------

if ($On -and $Off) { throw "Cannot specify both -On and -Off." }

$target = if ($On)  { $true }
          elseif ($Off) { $false }
          else { -not $current }

if ($target -eq $current) {
    Write-Host ""
    Write-Host "Feature is already in the desired state, no change needed." -ForegroundColor DarkGray
    Write-Host ""
    exit 0
}

# -- Toggle ----------------------------------------------------------------

if ($target) {
    az appconfig feature enable --name $Store --feature $Feature --yes | Out-Null
} else {
    az appconfig feature disable --name $Store --feature $Feature --yes | Out-Null
}

if ($LASTEXITCODE -ne 0) { throw "Feature toggle failed." }

$verified = Read-State
Write-State $verified "New:"

if ($verified -ne $target) {
    Write-Host ""
    Write-Host "WARNING: service reports a different state than expected." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "The app will pick up the change on the next configuration refresh" -ForegroundColor DarkGray
Write-Host "(based on the interval set in Program.cs, typically within 30 seconds)." -ForegroundColor DarkGray
Write-Host ""
