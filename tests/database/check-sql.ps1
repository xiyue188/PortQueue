# Parse SQL Server 2019 syntax using ScriptDom from the installed VS Code extension.
# No database connection or SQL execution. ASCII keeps Windows PowerShell 5 compatible.
param([string]$ParserAssembly)
$ErrorActionPreference = 'Stop'
if (-not $ParserAssembly) {
    $ParserAssembly = Get-ChildItem -Path "$env:USERPROFILE/.vscode/extensions/ms-mssql.mssql-*/BuildDirectory/Microsoft.SqlServer.TransactSql.ScriptDom.dll" |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ParserAssembly) { throw 'Install the SQL Server VS Code extension or supply -ParserAssembly.' }
$sqlDirectory = Join-Path $PSScriptRoot '../../database'
$sqlProjectFile = Join-Path $PSScriptRoot 'SqlSyntaxCheck/SqlSyntaxCheck.csproj'
$sqlAssemblyFile = Join-Path $PSScriptRoot 'SqlSyntaxCheck/bin/Release/net8.0/SqlSyntaxCheck.dll'
& dotnet build $sqlProjectFile --configuration Release "-p:SqlParserAssembly=$ParserAssembly"
if ($LASTEXITCODE -ne 0) { throw 'SQL parser build failed.' }
& dotnet $sqlAssemblyFile $sqlDirectory
if ($LASTEXITCODE -ne 0) { throw 'SQL static parsing failed.' }
