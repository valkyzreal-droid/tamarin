# Building Tamarin

Needs the .NET 8 SDK on Windows 10 or 11.

```
dotnet test                       # Core tests (also runs on Linux/macOS)
dotnet run --project src/Tamarin.App
dotnet publish src/Tamarin.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Portable mode: put an empty file named `portable.flag` next to `Tamarin.exe`; notes are then kept in a `data` folder beside it.


