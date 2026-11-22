# Setup SQL access for Function App managed identity
#
# Usage:
#   ./setup-sql-access.ps1 -SqlServerName <server-name> [-DatabaseName ContosoERP] [-FunctionAppName func-contoso-erp-api]
param(
    # Azure SQL logical server name, without the .database.windows.net suffix
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$SqlServerName,

    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$DatabaseName = "ContosoERP",

    # Function App whose managed identity is granted read access
    [ValidatePattern('^[A-Za-z0-9-]+$')]
    [string]$FunctionAppName = "func-contoso-erp-api"
)

$token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv

$connectionString = "Server=tcp:${SqlServerName}.database.windows.net,1433;Database=${DatabaseName};Encrypt=True;TrustServerCertificate=False;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString
$connection.AccessToken = $token

try {
    $connection.Open()
    Write-Host "Connected to SQL Database"

    # Create user for managed identity
    $sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '${FunctionAppName}')
BEGIN
    CREATE USER [${FunctionAppName}] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_datareader ADD MEMBER [${FunctionAppName}];
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
