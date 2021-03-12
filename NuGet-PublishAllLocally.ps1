$suffix = ''

# purge temporary packages (forces a rebuild)
rm nupkgs -r -EA ig

# wipe matching packages in global cache with suffix
rm "$env:USERPROFILE/.nuget/packages/av.*/*${$suffix}" -r

# pack all at current versions, with suffix
if ($suffix) { dotnet pack AV.sln --include-symbols -c Release -o nupkgs --version-suffix $suffix }
else { dotnet pack AV.sln --include-symbols -c Release -o nupkgs }

# push (matching) packages for symbols
nuget push "nupkgs\*${suffix}.symbols.nupkg"
