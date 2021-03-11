# configure default push
nuget config -set DefaultPushSource="$env:USERPROFILE/.nuget-tool/packages"

# add repo source to package manager
nuget sources add -name localrepo -source "$env:USERPROFILE/.nuget-tool/packages"

# AV.Extensions gets packed on build, so just run:
nuget push .\AV.Extensions\bin\Release\AV.Extensions.*.nupkg