# configure default push
nuget config -set DefaultPushSource="$env:USERPROFILE/.nuget-tool/packages"

# add repo source to package manager
nuget sources add -name localrepo -source "$env:USERPROFILE/.nuget-tool/packages"