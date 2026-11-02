using Microsoft.SqlServer.TransactSql.ScriptDom;

// 仅静态解析 SQL Server 2019 语法，不连接 SQL Server，也不执行脚本。
var parser = new TSql150Parser(initialQuotedIdentifiers: true);
var failures = 0;
foreach (var file in Directory.GetFiles(args[0], "*.sql").Order())
{
    using var reader = File.OpenText(file);
    parser.Parse(reader, out var errors);
    foreach (var error in errors)
    {
        Console.Error.WriteLine($"{Path.GetFileName(file)}:{error.Line}:{error.Column} {error.Message}");
        failures++;
    }
    if (errors.Count == 0) Console.WriteLine($"PASS {Path.GetFileName(file)}");
}
return failures == 0 ? 0 : 1;
