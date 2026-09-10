personal tool for streaming

- Windows 10 (1809+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or build from source with the .NET 8 SDK)
- Administrator Privilege

## Build

```powershell
dotnet build CenterConsole.sln
```

Run tests:

```powershell
dotnet test tests\CenterConsole.Tests\CenterConsole.Tests.csproj
```

Publish a self-contained executable:

```powershell
dotnet publish src\CenterConsole.App\CenterConsole.App.csproj -c Release -r win-x64 --self-contained
```
