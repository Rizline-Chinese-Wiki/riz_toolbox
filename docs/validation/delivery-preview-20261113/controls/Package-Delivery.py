"""Draft delivery command. Run only after final C integration and a fresh accepted six-component graph.
Uses the unchanged Pack-Sdk.ps1, builds two small SDK-only examples offline, and packages fresh runtime files.
No native compilation, matrix, game installation or game launch is performed.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox').resolve()
S = Path(__file__).resolve().parent
OUTPUT = ROOT/'artifacts/delivery-20261113'
CACHE = Path('D:/riz-build-cache-next/playback-lower-time-r1-20261104/s3-sdk-repository-managed-build-r1/build/packages')
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PWSH = Path('C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')
NAMES = ['RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Hosting','RizToolbox.Loader']
NATIVE_PATHS = {'winhttp.dll','BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll',
 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll',
 'BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip',
 'BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt'}
INSTALL_SHA = 'ad7c65ec58e99decef283afab0d237b6ad6acb73a5af8dc3365942b09191eb67'
INSTALL_PATHS = (NATIVE_PATHS | {'.doorstop_version',
 'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'}
 | {'BepInEx/plugins/RizToolbox/'+name+'.dll' for name in NAMES}
 | {'BepInEx/RizToolbox/mods/'+plugin+'/'+name
    for plugin in ('example.panel','example.events','example.color-low','example.color-high','example.scores')
    for name in ('plugin.json','RizToolbox.Examples.dll')})
FRAMEWORKS = {
 'microsoft.netcore.app.ref':'f4b66056820517ca8dc94bbc91d26b6062eeb4c17f70bd8af361cddb2c30971f',
 'microsoft.aspnetcore.app.ref':'f630e459b8f0fe777ccaa7735536a00aeaabea8c09fc350c8b80655194f88565',
 'microsoft.windowsdesktop.app.ref':'2bc97651082b10e26deb0dd0e09cbb749957008d601baa3ed8d1018261bc746e'}


def require(value,message):
    if not value:
        raise ValueError(message)


def fact(path):
    path=Path(path).resolve()
    data=path.read_bytes()
    return dict(path=str(path),bytes=len(data),sha256=hashlib.sha256(data).hexdigest())


def read(path):
    return json.loads(Path(path).read_text('utf-8-sig'))


def pin(row):
    actual=fact(row['path'])
    require(actual=={key:row[key] for key in ('path','bytes','sha256')},'Changed input: '+row['path'])
    return actual


def copy(source,target):
    source,target=Path(source),Path(target)
    require(not target.exists(),'Preserve existing staging file: '+str(target))
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(source,target)
    require(fact(source)['sha256']==fact(target)['sha256'],'Copied bytes differ.')
    return fact(target)


def text(path,value):
    path=Path(path)
    require(not path.exists(),'Preserve existing staging artifact.')
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(value,encoding='utf-8',newline='\n')


def run(argv,cwd,env,name):
    with (S/'work'/(name+'.stdout.log')).open('xb') as out,(S/'work'/(name+'.stderr.log')).open('xb') as err:
        completed=subprocess.run([str(value) for value in argv],cwd=cwd,env=env,stdout=out,stderr=err,
                                 creationflags=subprocess.CREATE_NO_WINDOW)
    require(completed.returncode==0,'Delivery command failed; preserve logs: '+name)
    return dict(command=[str(value) for value in argv],osExitCode=completed.returncode,
                stdout=fact(S/'work'/(name+'.stdout.log')),stderr=fact(S/'work'/(name+'.stderr.log')))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--six-review',required=True)
    parser.add_argument('--six-review-sha',required=True)
    parser.add_argument('--native-manifest',required=True)
    parser.add_argument('--native-manifest-sha',required=True)
    parser.add_argument('--native-abi-review',required=True)
    parser.add_argument('--native-abi-review-sha',required=True)
    parser.add_argument('--execute',action='store_true')
    args=parser.parse_args()
    require(S==Path('D:/riz-build-cache-next/delivery-20261113-r1').resolve(),'Wrong delivery work scope.')
    require(args.execute,'This is a prepared draft. Root runs --execute after both actual final inputs are available.')
    require(fact(ROOT/'scripts/Install.ps1')['sha256']==INSTALL_SHA and len(INSTALL_PATHS)==23,
            'The reviewed original installer whitelist changed.')
    review_fact=fact(args.six_review)
    native_fact=fact(args.native_manifest)
    abi_fact=fact(args.native_abi_review)
    require(review_fact['sha256']==args.six_review_sha and native_fact['sha256']==args.native_manifest_sha
            and abi_fact['sha256']==args.native_abi_review_sha,
            'Exact actual delivery input SHA is required.')
    review,native,abi=read(args.six_review),read(args.native_manifest),read(args.native_abi_review)
    require(review.get('kind')=='delivery-final-six-review-v1' and review.get('passed') is True
            and review.get('sixFreshComponentsAccepted') is True
            and review.get('finalRepositorySourceClosure') is True
            and review.get('compilerArgumentsAndPdbVerified') is True
            and review.get('fullLegacy300RunOnFinal881Bytes') is False,
            'Fresh current six-component acceptance required.')
    require(abi.get('kind')=='delivery-native-abi-reuse-v1' and abi.get('passed') is True
            and abi.get('reuseAccepted') is True and abi.get('nativeManifest')==native_fact,
            'Final managed/native ABI reuse proof must bind these exact native bytes.')
    graph_fact=pin(review['graphMetadata'])
    require(abi.get('finalManagedGraph')==graph_fact and review.get('graphMetadata')==graph_fact,
            'Final native ABI proof and six review must bind the same exact graph bytes.')
    pin(review['endpointReceipt']);pin(review['focusedResult'])
    graph=read(graph_fact['path'])
    totals=graph['totals']
    require(graph['passed'] is True and graph.get('metadataOnly') is True
            and graph.get('allCscFrozenPdbCompleteAndSha256Equal') is True
            and graph.get('historicalProductReferences')==0
            and review.get('totals')==totals
            and totals.get('projects')==6 and 500<=totals.get('authored',-1)<=700
            and totals.get('generated')==18
            and totals.get('pdbDocuments')==totals['authored']+18
            and totals.get('freshProductReferenceEdges')==11
            and totals.get('copyLocalPePdbFiles')==22,
            'Final six-product raw-metadata graph totals or provenance differ.')
    graph_scope=Path(graph_fact['path']).parents[2]
    source_root=Path(graph.get('sourceRoot',str(graph_scope/'source'))).resolve()
    require(source_root==ROOT,'Final graph must identify actual C compiler source root.')
    projects={row['projectName']:row for row in graph['projects']}
    require(set(projects)==set(NAMES),'Exactly six runtime projects required.')
    require(sum(row['authoredSources'] for row in projects.values())==totals['authored']
            and sum(row['generatedSources'] for row in projects.values())==18
            and sum(row['pdbDocumentCount'] for row in projects.values())==totals['pdbDocuments'],
            'Final graph project totals differ.')
    for project in projects.values():
        name=project['projectName']
        current_sources={path.resolve() for path in (ROOT/'src'/name).rglob('*.cs')
                         if not {'bin','obj'}.intersection(path.relative_to(ROOT/'src'/name).parts)}
        authored={Path(row['path']).resolve().relative_to(source_root)
                  for row in project['sources'] if not row['generated']}
        require(len(authored)==project['authoredSources'] and len(current_sources)==len(authored)
                and {ROOT/relative for relative in authored}==current_sources
                and project['generatedSources']==3
                and project['compilerSourceCount']==project['pdbDocumentCount']==len(project['sources'])
                and project['compilerSourceCount']==len(authored)+3,
                'Final authored source enumeration or PDB document closure differs: '+name)
        for field in ('dll','pdb'):
            pin(project[field])
        for source in project['sources']:
            pin(source);pin(source['frozen'])
            if not source['generated']:
                relative=Path(source['path']).relative_to(source_root)
                current=fact(ROOT/relative)
                require((current['bytes'],current['sha256'])==(source['bytes'],source['sha256']),
                        'Final graph does not match the current repository: '+str(relative))
    for relative in ('src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointLocalTarget.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekJudgeStateView.cs',
        'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.JudgeStateView.cs',
        'src/RizToolbox.Runtime/NativeRuntime.NoteJudgeReadContainers.cs'):
        require((ROOT/relative).is_file(),'Final Seek candidate source must be integrated before delivery.')
    require(native['passed'] is True and native['doorstopVersion']=='4.5.0-riz-preinit.2'
            and len(native['files'])==5 and {row['relativePath'] for row in native['files']}==NATIVE_PATHS,
            'Actual production-native five-file manifest required.')
    for row in native['files']:
        pin(row['source'])
    lock=read(ROOT/'dependencies.lock.json')
    bepinex=fact(ROOT/'.tools/bepinex-789.zip')
    require(bepinex['sha256']==lock['bepinex']['sha256'],'Pinned offline runtime bundle changed.')
    packs=[]
    for name,digest in FRAMEWORKS.items():
        row=fact(CACHE/name/'6.0.36'/(name+'.6.0.36.nupkg'))
        require(row['sha256']==digest,'Pinned offline framework package changed.')
        packs.append(row)
    sdk=projects['RizToolbox.Sdk']
    sdk_ref=next(row for row in projects['RizToolbox.Runtime']['dependencies']['references']
                 if Path(row['path']).stem=='RizToolbox.Sdk')
    pin(sdk_ref)
    sdk_xml=Path(sdk['dll']['path']).with_suffix('.xml')
    require(sdk_xml.is_file(),'Fresh SDK XML docs required.')
    work=S/'work'
    require(not work.exists(),'Preserve an existing delivery attempt; use its logs.')
    require(shutil.disk_usage(S).free>=801304349+512*1024*1024,'D reserve/headroom unavailable.')
    work.mkdir()
    copy(ROOT/'Directory.Build.props',work/'Directory.Build.props')
    workspace=work/'sdk-workspace'
    payload=work/'RizToolbox-20261113'
    feed=payload/'sdk/feed'
    for relative in ('scripts/Pack-Sdk.ps1','Directory.Build.props','src/RizToolbox.Sdk/RizToolbox.Sdk.csproj',
                     'packaging/sdk/README.md'):
        copy(ROOT/relative,workspace/relative)
    sdk_project=workspace/'src/RizToolbox.Sdk'
    for field in ('dll','pdb'):
        copy(sdk[field]['path'],sdk_project/'bin/Release/net6.0'/Path(sdk[field]['path']).name)
    copy(sdk_xml,sdk_project/'bin/Release/net6.0/RizToolbox.Sdk.xml')
    sdk_obj=Path(sdk['actualCscArguments']['objDll']).parent
    for source in sdk_obj.rglob('*'):
        if source.is_file():
            copy(source,sdk_project/'obj/Release/net6.0'/source.relative_to(sdk_obj))
    for source in sdk_obj.parent.iterdir():
        if source.is_file():
            copy(source,sdk_project/'obj'/source.name)
    require(fact(sdk_project/'obj/Release/net6.0/ref/RizToolbox.Sdk.dll')['sha256']==sdk_ref['sha256'],
            'Pack workspace uses the wrong SDK reference assembly.')
    for row in packs:
        copy(row['path'],feed/Path(row['path']).name)
    text(S/'work/global.json','{"sdk":{"version":"8.0.418","rollForward":"disable"}}\n')
    text(workspace/'global.json','{"sdk":{"version":"8.0.418","rollForward":"disable"}}\n')
    configuration='<configuration><packageSources><clear/><add key="offline" value="'+str(feed)+'"/></packageSources></configuration>\n'
    text(workspace/'NuGet.Config',configuration)
    text(payload/'NuGet.Config','<configuration><packageSources><clear/><add key="offline" value="sdk/feed"/></packageSources><config><add key="globalPackagesFolder" value=".nuget/packages"/></config></configuration>\n')
    for source in (ROOT/'templates/riz-plugin').rglob('*'):
        if source.is_file():
            copy(source,payload/'examples/RizModProject'/source.relative_to(ROOT/'templates/riz-plugin'))
    examples=work/'examples-build'
    for source in (ROOT/'examples/RizToolbox.Examples').rglob('*.cs'):
        if 'bin' not in source.parts and 'obj' not in source.parts:
            copy(source,examples/source.relative_to(ROOT/'examples/RizToolbox.Examples'))
    text(examples/'RizToolbox.Examples.csproj','<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net6.0</TargetFramework><LangVersion>10.0</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><CheckEolTargetFramework>false</CheckEolTargetFramework><GenerateDependencyFile>false</GenerateDependencyFile></PropertyGroup><ItemGroup><PackageReference Include="RizToolbox.Sdk" Version="1.2.0" IncludeAssets="compile" PrivateAssets="all"/></ItemGroup></Project>\n')
    env=os.environ.copy()
    for name,directory in {'TEMP':'temp','TMP':'temp','NUGET_PACKAGES':'packages',
        'NUGET_HTTP_CACHE_PATH':'http-cache','NUGET_PLUGINS_CACHE_PATH':'plugin-cache',
        'NUGET_MIGRATION_CACHE_PATH':'migrations','DOTNET_BUNDLE_EXTRACT_BASE_DIR':'bundle'}.items():
        path=work/'environment'/directory
        path.mkdir(parents=True,exist_ok=True)
        env[name]=str(path)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
               DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',DOTNET_NOLOGO='1',
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
               DOTNET_CLI_USE_MSBUILD_SERVER='0',MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1',
               PATH=str(DOTNET.parent)+os.pathsep+env.get('PATH',''))
    commands=[run([PWSH,'-NoProfile','-File',workspace/'scripts/Pack-Sdk.ps1','-SkipBuild'],workspace,env,'pack-sdk')]
    package=workspace/'artifacts/sdk/RizToolbox.Sdk.1.2.0.nupkg'
    with zipfile.ZipFile(package) as archive:
        require(not any(name.startswith('lib/') for name in archive.namelist()),'SDK must remain reference-only.')
        require(hashlib.sha256(archive.read('ref/net6.0/RizToolbox.Sdk.dll')).hexdigest()==sdk_ref['sha256'],
                'Packed SDK reference differs from the accepted fresh graph.')
    copy(package,feed/package.name)
    copy(workspace/'artifacts/sdk/sdk-manifest.json',payload/'sdk/sdk-manifest.json')
    for label,project in [('template',payload/'examples/RizModProject/RizModProject.csproj'),
                          ('legacy-examples',examples/'RizToolbox.Examples.csproj')]:
        commands.append(run([DOTNET,'restore',project,'--configfile',payload/'NuGet.Config','--packages',
                             work/'environment/packages','-p:NuGetAudit=false','--verbosity','minimal'],work,env,label+'-restore'))
        commands.append(run([DOTNET,'build',project,'-c','Release','--no-restore','--verbosity','minimal'],work,env,label+'-build'))
        require(not (project.parent/'bin/Release/net6.0/RizToolbox.Sdk.dll').exists(),'A plugin copied a host SDK implementation.')
    for source in examples.rglob('*'):
        relative=source.relative_to(examples)
        if source.is_file() and not {'bin','obj'}.intersection(relative.parts):
            copy(source,payload/'examples/RizToolbox.Examples'/relative)
    copy(work/'Directory.Build.props',payload/'examples/RizToolbox.Examples/Directory.Build.props')
    runtime=payload/'runtime'
    package_root=runtime/'artifacts/package'
    package_paths=[]
    for name in NAMES:
        relative='BepInEx/plugins/RizToolbox/'+name+'.dll'
        copy(projects[name]['dll']['path'],package_root/relative)
        copy(projects[name]['pdb']['path'],runtime/'symbols'/(name+'.pdb'))
        package_paths.append(relative)
    for row in native['files']:
        copy(row['source']['path'],package_root/row['relativePath'])
        package_paths.append(row['relativePath'])
    text(package_root/'.doorstop_version',native['doorstopVersion'])
    package_paths.append('.doorstop_version')
    for profile in (ROOT/'src/RizToolbox.Adapter/profiles').glob('*.json'):
        relative='BepInEx/plugins/RizToolbox/profiles/'+profile.name
        copy(profile,package_root/relative)
        package_paths.append(relative)
    example_dll=examples/'bin/Release/net6.0/RizToolbox.Examples.dll'
    for folder in sorted((ROOT/'examples/packages').iterdir()):
        if folder.is_dir():
            relative='BepInEx/RizToolbox/mods/'+folder.name
            copy(folder/'plugin.json',package_root/relative/'plugin.json')
            copy(example_dll,package_root/relative/'RizToolbox.Examples.dll')
            package_paths.extend([relative+'/plugin.json',relative+'/RizToolbox.Examples.dll'])
    require(len(package_paths)==23 and set(package_paths)==INSTALL_PATHS,'Original Install.ps1 exact package whitelist differs.')
    manifest=dict(schemaVersion=2,version='0.3.0',loader='Doorstop modular compatibility host',
                  files=[dict(path=path,sha256=fact(package_root/path)['sha256']) for path in sorted(package_paths)])
    text(runtime/'artifacts/package-manifest.json',json.dumps(manifest,indent=2)+'\n')
    for relative in ('scripts/Install.ps1','scripts/Install.Common.ps1','scripts/Prepare-Dependencies.ps1',
                     'scripts/Uninstall.ps1','dependencies.lock.json'):
        copy(ROOT/relative,runtime/relative)
    copy(bepinex['path'],runtime/'.tools/bepinex-789.zip')
    template=payload/'examples/RizModProject'
    copy(template/'bin/Release/net6.0/RizModProject.dll',payload/'examples/compiled/wiki.hello/RizModProject.dll')
    copy(template/'plugin.json',payload/'examples/compiled/wiki.hello/plugin.json')
    copy(ROOT/'docs/SDK.md',payload/'sdk/SDK.md')
    for relative in ('LICENSE','THIRD_PARTY_NOTICES.md'):
        copy(ROOT/relative,payload/relative)
    text(payload/'QUICKSTART.txt','RizToolbox 开发预览：SDK 1.2.0 / Runtime 0.3.0\n\n本候选包含新的 Seek 线/环目标数值、来源与画布局部端点投影代码，以及只读 JudgeView 快照。端点投影是内部纯局部模型；不驱动 native writer、世界坐标目标或完整 Seek。离线已验证最终六组件编译与引用闭合、SDK 引用包及下列公开接口的示例编译：\n  wiki.hello：IGameStateService，读取场景并订阅 GameSceneChanged。\n  example.panel：IUiService，创建作用域面板。\n  example.events：IExtensionService、IGameStateService，观察扩展点与场景。\n  example.color-low / example.color-high：IGameResourceService，颜色覆盖层。\n  example.scores：ICatalogService、IScoreService，读取目录和成绩。\n服务实际可用性仍须通过能力与 GetService 查询；离线编译不证明游戏内效果。\n\n解压后在本目录编译只读模板（需要 .NET SDK 8）：\n  dotnet restore examples/RizModProject/RizModProject.csproj --configfile NuGet.Config\n  dotnet build examples/RizModProject/RizModProject.csproj -c Release --no-restore\n本地 feed 包含 SDK 与三个 .NET 6.0.36 引用包。插件不得携带宿主 DLL。\n\n修改并离线重编原五个示例：\n  dotnet restore examples/RizToolbox.Examples/RizToolbox.Examples.csproj --configfile NuGet.Config\n  dotnet build examples/RizToolbox.Examples/RizToolbox.Examples.csproj -c Release --no-restore\n现成只读示例位于 examples/compiled/wiki.hello/；原五个示例随 Runtime 提供。\n\n安装（先关闭游戏，需要 PowerShell 7.2+）：\n  pwsh -File runtime/scripts/Install.ps1 -GameDirectory <Rizline-directory>\n安装器核对受支持的 GameAssembly 指纹并保留既有安装状态。本包制作期间未执行安装或启动游戏。\n\n本批尚未做游戏实测；完整 Speed / Seek 与生产准入保持关闭。JudgeView 不是新增公共 SDK API。许可证和第三方说明见根目录 LICENSE、THIRD_PARTY_NOTICES.md。\n')
    excluded={'bin','obj','.nuget'}
    files=[path for path in payload.rglob('*') if path.is_file() and not excluded.intersection(path.relative_to(payload).parts)]
    delivery=dict(kind='riz-toolbox-sdk-runtime-delivery-v1',createdUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        sixProductAcceptance=review_fact,graphMetadata=graph_fact,nativeManifest=native_fact,nativeAbiReview=abi_fact,
        sdkReference=sdk_ref,sdkPackage=fact(feed/package.name),sdkContractVersion='1.2.0',runtimeVersion='0.3.0',
        commands=commands,exampleCompiles=True,nativeRecompiledByThisCommand=False,networkUsed=False,
        fullLegacy300RunOnFinal881Bytes=False,
        completeSpeedEnabled=False,completeSeekEnabled=False,judgeStateViewPublicSdk=False,
        productionAdmission=False,runtimeGameVerified=False,gameExecuted=False,
        files=[dict(path=path.relative_to(payload).as_posix(),bytes=path.stat().st_size,
                    sha256=fact(path)['sha256']) for path in sorted(files)])
    text(payload/'delivery-manifest.json',json.dumps(delivery,indent=2)+'\n')
    files.append(payload/'delivery-manifest.json')
    zip_path=work/'RizToolbox-0.3.0-sdk-runtime-20261113.zip'
    with zipfile.ZipFile(zip_path,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for path in sorted(files):
            item=zipfile.ZipInfo(path.relative_to(payload).as_posix(),(2000,1,1,0,0,0))
            item.compress_type=zipfile.ZIP_DEFLATED
            archive.writestr(item,path.read_bytes())
    with zipfile.ZipFile(zip_path) as archive:
        require(archive.testzip() is None,'Delivery ZIP CRC verification failed.')
        for relative in ('LICENSE','THIRD_PARTY_NOTICES.md'):
            require(archive.read(relative)==(ROOT/relative).read_bytes(),'ZIP root license bytes differ.')
    OUTPUT.mkdir(parents=True,exist_ok=True)
    copied=copy(zip_path,OUTPUT/zip_path.name)
    copy(payload/'delivery-manifest.json',OUTPUT/'delivery-manifest.json')
    text(OUTPUT/'SHA256SUMS.txt',copied['sha256']+'  '+zip_path.name+'\n')
    print(json.dumps(dict(delivered=True,zip=copied,manifest=fact(OUTPUT/'delivery-manifest.json')),indent=2))


if __name__=='__main__':
    main()
