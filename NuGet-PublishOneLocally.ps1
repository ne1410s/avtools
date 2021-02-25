$project = 'SecureMedia.Abstractions'
$version = '1.0.0'

# purge temporary package (forces a rebuild)
rm "nupkgs/${project}.${version}*.nupkg" -EA ig

# wipe matching package in global cache with suffix
rm "$env:USERPROFILE/.nuget/packages/${project}.${version}" -r -EA ig

# pack at specified version
dotnet pack "${project}" --include-symbols -c Release -o nupkgs -p:PackageVersion="${version}"

# push (matching) packages for symbols
nuget push "nupkgs/${project}.${version}.symbols.nupkg"
