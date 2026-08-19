<#
    Deploy ClaimsIntake to Azure Container Apps.

    Usage:
        .\deploy.ps1

    Secrets are read from deploy.local.json which must NOT be committed to git.
    A template for this file is in the comment at the end of the script.
#>

$ErrorActionPreference = "Stop"

# -----------------------------  Configuration  ----------------------------

$acr        = "claimsintakeacr"
$app        = "claims-intake"
$rg         = "rg-claims-ai"
$imageName  = "claimsintake"

$docIntelligenceEndpoint = "https://insuranceclaimsdocumentintelligence.cognitiveservices.azure.com/"
$openAiEndpoint          = "https://insuranceclaims-resource.openai.azure.com/"
$openAiDeployment        = "gpt-5-mini"
$embeddingEndpoint       = "https://insuranceclaims-resource.services.ai.azure.com"
$embeddingDeployment     = "text-embedding-3-small"
$langfuseBaseUrl         = "https://cloud.langfuse.com"

# --------------------------------------------------------------------------

$stamp    = Get-Date -Format "yyyyMMdd-HHmm"
$image    = "$acr.azurecr.io/${imageName}:$stamp"
$suffix   = $stamp.ToLower()

function Write-Step($text) {
    Write-Host ""
    Write-Host "-- $text " -ForegroundColor Cyan -NoNewline
    Write-Host ("-" * [Math]::Max(0, 60 - $text.Length)) -ForegroundColor DarkGray
}

# -- 1. Prerequisites check ------------------------------------------------

Write-Step "Checking environment"

docker info 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Docker is not running. Start Docker Desktop and wait until it is ready."
}
Write-Host "Docker is running." -ForegroundColor Green

$secretsPath = Join-Path $PSScriptRoot "deploy.local.json"
if (-not (Test-Path $secretsPath)) {
    throw "Missing deploy.local.json with keys. See the template in the comment at the end of this script."
}
$secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json

foreach ($key in @("embeddingKey", "docIntelligenceKey", "openAiKey", "appInsightsConnectionString", "appConfigConnectionString", "langfusePublicKey", "langfuseSecretKey")) {
    if ([string]::IsNullOrWhiteSpace($secrets.$key)) {
        throw "Key '$key' is missing or empty in deploy.local.json."
    }
}
Write-Host "Keys loaded." -ForegroundColor Green

# -- 2. Build and push image -----------------------------------------------

Write-Step "Logging into ACR"
az acr login --name $acr
if ($LASTEXITCODE -ne 0) { throw "ACR login failed." }

Write-Step "Building image $image"
docker build -t $image .
if ($LASTEXITCODE -ne 0) { throw "Image build failed." }

Write-Step "Pushing image to ACR"
docker push $image
if ($LASTEXITCODE -ne 0) { throw "Image push failed." }

# -- 3. Secrets ------------------------------------------------------------

Write-Step "Updating secrets"
az containerapp secret set -n $app -g $rg --secrets `
    embedding-key=$($secrets.embeddingKey) `
    docint-key=$($secrets.docIntelligenceKey) `
    openai-key=$($secrets.openAiKey) `
    appinsights-cs=$($secrets.appInsightsConnectionString) `
    appconfig-cs=$($secrets.appConfigConnectionString) `
    langfuse-public=$($secrets.langfusePublicKey) `
    langfuse-secret=$($secrets.langfuseSecretKey) `
    | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Setting secrets failed." }
Write-Host "Secrets updated." -ForegroundColor Green

# -- 4. Deploy new revision ------------------------------------------------

Write-Step "Deploying revision $suffix"
az containerapp update -n $app -g $rg `
    --image $image `
    --revision-suffix $suffix `
    --set-env-vars `
        DocIntelligence__Endpoint=$docIntelligenceEndpoint `
        DocIntelligence__Key=secretref:docint-key `
        AzureOpenAI__Endpoint=$openAiEndpoint `
        AzureOpenAI__Key=secretref:openai-key `
        AzureOpenAI__Deployment=$openAiDeployment `
        AzureEmbedding__Endpoint=$embeddingEndpoint `
        AzureEmbedding__Deployment=$embeddingDeployment `
        AzureEmbedding__Key=secretref:embedding-key `
        APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appinsights-cs `
        AppConfig__ConnectionString=secretref:appconfig-cs `
        Langfuse__PublicKey=secretref:langfuse-public `
        Langfuse__SecretKey=secretref:langfuse-secret `
        Langfuse__BaseUrl=$langfuseBaseUrl `
    | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Deployment failed." }

# -- 5. Verify the revision is running -------------------------------------

Write-Step "Verifying revision"

$revisionName = "$app--$suffix"
$deadline = (Get-Date).AddMinutes(4)
$state = ""

while ((Get-Date) -lt $deadline) {
    $state = az containerapp revision show -n $app -g $rg --revision $revisionName `
        --query "properties.runningState" -o tsv 2>$null

    if ($state -eq "Running") { break }
    if ($state -eq "Failed" -or $state -eq "ActivationFailed") { break }

    Write-Host "  state: $state -- waiting..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 10
}

if ($state -ne "Running") {
    Write-Host ""
    Write-Host "REVISION FAILED TO START (state: $state)." -ForegroundColor Red
    Write-Host "Previous version keeps running. Revision logs:" -ForegroundColor Yellow
    Write-Host ""
    az containerapp logs show -n $app -g $rg --revision $revisionName --tail 80
    exit 1
}

$url = az containerapp show -n $app -g $rg --query "properties.configuration.ingress.fqdn" -o tsv

Write-Host ""
Write-Host "Deployed." -ForegroundColor Green
Write-Host "  revision: $revisionName"
Write-Host "  image:    $image"
Write-Host "  URL:      https://$url"
Write-Host ""
Write-Host "-- Revision logs " -ForegroundColor Cyan -NoNewline
Write-Host ("-" * 44) -ForegroundColor DarkGray
az containerapp logs show -n $app -g $rg --revision $revisionName --tail 30
Write-Host ""

<#
    -- deploy.local.json -----------------------------------------------------
    Create in the same directory and ADD TO .gitignore:

    {
      "embeddingKey": "...",
      "docIntelligenceKey": "...",
      "openAiKey": "...",
      "appInsightsConnectionString": "...",
      "appConfigConnectionString": "...",
      "langfusePublicKey": "...",
      "langfuseSecretKey": "..."
    }
#>
