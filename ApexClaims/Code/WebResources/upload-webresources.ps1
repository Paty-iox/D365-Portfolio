# Upload Web Resources to Dataverse
# Requires: PAC CLI authenticated

$ErrorActionPreference = "Stop"

# Get access token using PAC CLI
Write-Host "Getting access token..." -ForegroundColor Cyan
$tokenOutput = pac auth token --resource "https://org22b59a4a.crm.dynamics.com"
$token = ($tokenOutput | Select-String -Pattern "^[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+$").Matches.Value

if (-not $token) {
    # Try alternative parsing
    $token = $tokenOutput[-1].Trim()
}

Write-Host "Token obtained." -ForegroundColor Green

$baseUrl = "https://org22b59a4a.crm.dynamics.com/api/data/v9.2"
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# Read file contents and convert to base64
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path

$htmlPath = Join-Path $scriptPath "new_ClaimLocationMap.html"
$jsPath = Join-Path $scriptPath "new_ClaimFormScripts.js"

$htmlContent = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content $htmlPath -Raw)))
$jsContent = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content $jsPath -Raw)))

# Function to create or update web resource
function Set-WebResource {
    param (
        [string]$Name,
        [string]$DisplayName,
        [string]$Content,
        [int]$WebResourceType  # 1=HTML, 3=JScript
    )

    # Check if web resource already exists
    $existingUrl = "$baseUrl/webresourceset?`$filter=name eq '$Name'&`$select=webresourceid"

    try {
        $existing = Invoke-RestMethod -Uri $existingUrl -Headers $headers -Method Get

        if ($existing.value.Count -gt 0) {
            # Update existing
            $webResourceId = $existing.value[0].webresourceid
            Write-Host "Updating existing web resource: $Name" -ForegroundColor Yellow

            $updateBody = @{
                content = $Content
                displayname = $DisplayName
            } | ConvertTo-Json

            $updateUrl = "$baseUrl/webresourceset($webResourceId)"
            Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody

            Write-Host "Updated: $Name" -ForegroundColor Green
            return $webResourceId
        }
    } catch {
        # Doesn't exist, create new
    }

    # Create new web resource
    Write-Host "Creating web resource: $Name" -ForegroundColor Cyan

    $body = @{
        name = $Name
        displayname = $DisplayName
        content = $Content
        webresourcetype = $WebResourceType
    } | ConvertTo-Json

    $response = Invoke-RestMethod -Uri "$baseUrl/webresourceset" -Headers $headers -Method Post -Body $body

    # Get the created ID from the response header
    Write-Host "Created: $Name" -ForegroundColor Green
}

# Upload HTML web resource (type 1 = HTML)
Write-Host "`nUploading HTML web resource..." -ForegroundColor Cyan
Set-WebResource -Name "new_ClaimLocationMap" -DisplayName "Claim Location Map" -Content $htmlContent -WebResourceType 1

# Upload JavaScript web resource (type 3 = JScript)
Write-Host "`nUploading JavaScript web resource..." -ForegroundColor Cyan
Set-WebResource -Name "new_ClaimFormScripts" -DisplayName "Claim Form Scripts" -Content $jsContent -WebResourceType 3

Write-Host "`nAll web resources uploaded successfully!" -ForegroundColor Green
Write-Host "Now add them to your solution and configure the form." -ForegroundColor Cyan
