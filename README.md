### Pre-requisites
___
###### Entity Framework
```powershell
# install dotnet-ef (can also update)
dotnet tool install -g dotnet-ef
```
___
###### NuGet
Download latest nuget cli (https://www.nuget.org/downloads), pop it somewhere safe and add it to the PATH.

- Run `NuGet-Setup.ps1` to configure a local nuget package repo.
- Run `NuGet-PublishOneLocally.ps1` to force-publish a project