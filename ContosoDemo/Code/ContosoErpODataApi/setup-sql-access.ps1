# Setup SQL access for Function App managed identity
$token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv

$connectionString = "Server=tcp:sql-contoso-demo-abc123.database.windows.net,1433;Database=ContosoERP;Encrypt=True;TrustServerCertificate=False;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString
$connection.AccessToken = $token

try {
    $connection.Open()
    Write-Host "Connected to SQL Database"

    # Create user for managed identity
    $sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'func-contoso-erp-api')
BEGIN
    CREATE USER [func-contoso-erp-api] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_datareader ADD MEMBER [func-contoso-erp-api];
    PRINT 'User created and granted db_datareader role';
END
ELSE
BEGIN
    PRINT 'User already exists';
END
"@

    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $command.ExecuteNonQuery()

    Write-Host "Managed identity user setup complete!"
}
catch {
    Write-Host "Error: $($_.Exception.Message)"
}
finally {
    $connection.Close()
}
