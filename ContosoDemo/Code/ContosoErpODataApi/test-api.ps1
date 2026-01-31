try {
    $response = Invoke-WebRequest -Uri 'https://func-contoso-erp-api.azurewebsites.net/api/odata/VendorMaster' -UseBasicParsing
    Write-Host "Status: $($response.StatusCode)"
    Write-Host $response.Content
} catch {
    Write-Host "Error: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $stream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        Write-Host "Response Body:"
        Write-Host $reader.ReadToEnd()
    }
}
