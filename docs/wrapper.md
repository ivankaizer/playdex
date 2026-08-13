# Windows wrapper

## Single-file build

Publish a self-contained Windows x64 executable with the `SingleFile` profile:

```powershell
dotnet publish src/Wrapper/Wrapper.csproj -p:PublishProfile=SingleFile
```

The executable is written to `artifacts/wrapper-single-file/Wrapper.exe`. It includes the .NET runtime and does not require a separate .NET installation on the target machine.

Use a normal build when a bundled executable is not needed:

```powershell
dotnet build src/Wrapper/Wrapper.csproj
```
