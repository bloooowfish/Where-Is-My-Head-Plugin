@{
    Owner = 'bloooowfish'
    RepoName = 'Where-Is-My-Head-Plugin'
    ProjectPath = 'WhereIsMyHead\WhereIsMyHead.csproj'
    TestProjectPath = 'WhereIsMyHead.Tests\WhereIsMyHead.Tests.csproj'
    SolutionPath = 'WhereIsMyHead.sln'
    ReleaseAssetName = 'WhereIsMyHead-{0}.zip'
    ReleaseTitle = 'Where Is My Head {0}'
    ReleaseNotes = 'Where Is My Head {0}'
    ExpectedGitUserName = 'bloooowfish'
    ExpectedGitUserEmail = '285025450+bloooowfish@users.noreply.github.com'
    ExpectedRemotes = @(
        'github-bf:bloooowfish/Where-Is-My-Head-Plugin.git',
        'git@github-bf:bloooowfish/Where-Is-My-Head-Plugin.git'
    )
    ReleaseWorkflowFile = 'release.yml'
    MasterOwner = 'bloooowfish'
    MasterRepoName = 'MyPluginMaster'
    MasterWorkflowFile = 'update-repo.yml'
    GhConfigDir = '~\.config\gh-bloooowfish'
}
