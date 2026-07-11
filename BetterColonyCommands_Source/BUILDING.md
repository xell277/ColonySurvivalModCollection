# Building

This source builds against the Colony Survival server assemblies.

Default Windows build:

```powershell
dotnet build .\BetterColonyCommands.sln -c Release
```

If Colony Survival is installed somewhere else, pass the install directory:

```powershell
dotnet build .\BetterColonyCommands.sln -c Release -p:ColonySurvivalDir="D:\SteamLibrary\steamapps\common\Colony Survival"
```

The compiled mod DLL is `ColonyCommands.dll`. Release ZIPs should include the DLL, `modInfo.json`, `README.md`, `LICENSE`, `preview.png`, and the example JSON config files.
