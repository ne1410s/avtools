$suffix = ''
$config = 'Debug'

# purge temporary packages (forces a rebuild)
rm nupkgs -r -EA ig

# wipe matching packages in global cache with suffix
rm "$env:USERPROFILE/.nuget/packages/securemedia.*/*${$suffix}" -r

# pack all at current versions, with suffix
if ($suffix) { dotnet pack SecureMedia.sln --include-symbols -c $config -o nupkgs --version-suffix $suffix }
else { dotnet pack SecureMedia.sln --include-symbols -c $config -o nupkgs }

# push (matching) packages for symbols
nuget push "nupkgs\*${suffix}.symbols.nupkg"
