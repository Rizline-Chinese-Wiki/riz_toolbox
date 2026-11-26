using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using RizToolbox.Core;
using RizToolbox.Core.Services;
using RizToolbox.Hosting;
using RizToolbox.Sdk;

if (args.Length != 3) { Console.Error.WriteLine("Usage: Distribution.Tests <sdk.nupkg> <generated-project-root> <mods-root>"); return 2; }
string package=Path.GetFullPath(args[0]), projects=Path.GetFullPath(args[1]), mods=Path.GetFullPath(args[2]);
var tests = new (string Name, Action Run)[] {
    ("SDK package contains one reference DLL and no runtime assets", PackageLayout),
    ("NuGet identity contract version and license match the host SDK", PackageMetadata),
    ("reference DLL is metadata-only and keeps SDK 1.x assembly identity", ReferenceIdentity),
    ("both generated plugins restore only compile assets from the SDK", RestoreAssets),
    ("template output copies no SDK or host binary", OutputOwnership),
    ("generated plugin binaries reference SDK and no implementation projects", PluginReferences),
    ("two generated packages load through formal loader with distinct contexts", PackageLoading),
    ("template plugins read the selected chart and receive the same host scene event", HostCallbacks),
    ("disabling one template removes only its subscriptions", HostDisable),
    ("capability withdrawal stops templates and restoration reactivates cleanly", CapabilityRecovery),
    ("independent package services preserve declared and future error fields through the host proxy", PackagedErrors),
    ("independent package service views reject worker and expired scopes without provider calls", PackagedScopeErrors),
    ("both independent packages receive structured rejection for their private contracts", PackagedContractErrors),
    ("a packaged provider fault stops its owner and preserves the other package", PackagedFaultIsolation)
};
int failures=0;
foreach(var test in tests)try{test.Run();Console.WriteLine("PASS "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL "+test.Name+": "+e);}
Console.WriteLine($"SDK distribution checks: {tests.Length-failures}/{tests.Length} passed.");
if(failures==0)
{
    var sdk=typeof(IRizPlugin).Assembly;
    var inventory=new {schemaVersion=1,sdkVersion=SdkInfo.Version.ToString(),assemblyIdentity=sdk.GetName().FullName,
        runtimeAvailabilityMustBeQueried=true,implementationAcceptanceEvidence="See docs/IMPLEMENTATION-STATUS.md; API inventory is not proof of a game provider.",
        capabilities=sdk.GetExportedTypes().Where(t=>t.IsAbstract&&t.IsSealed&&(t.Name.EndsWith("CapabilityIds")||t==typeof(CapabilityIds))).OrderBy(t=>t.FullName,StringComparer.Ordinal)
            .SelectMany(t=>t.GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Where(f=>f.IsLiteral&&f.FieldType==typeof(string))
                .OrderBy(f=>f.Name,StringComparer.Ordinal).Select(f=>new{declaration=t.FullName+"."+f.Name,id=(string)f.GetRawConstantValue()!})).ToArray(),
        publicInterfaces=sdk.GetExportedTypes().Where(t=>t.IsInterface).Select(t=>t.FullName).OrderBy(x=>x,StringComparer.Ordinal).ToArray()};
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(package)!,"sdk-contracts.json"),JsonSerializer.Serialize(inventory,new JsonSerializerOptions{WriteIndented=true}));
}
return failures==0?0:1;
void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
ZipArchive Open()=>ZipFile.OpenRead(package);
void PackageLayout()
{
    using var zip=Open();var names=zip.Entries.Select(e=>e.FullName).ToArray();
    Check(names.Contains("ref/net6.0/RizToolbox.Sdk.dll")&&names.Contains("ref/net6.0/RizToolbox.Sdk.xml")&&names.Contains("README.md"),"Reference assets or author readme missing.");
    Check(names.Count(x=>x.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))==1&&!names.Any(x=>x.StartsWith("lib/")||x.StartsWith("runtimes/")||x.StartsWith("build/")),"Runtime/host assets leaked into SDK reference package.");
}
void PackageMetadata()
{
    using var zip=Open();using var input=zip.GetEntry("RizToolbox.Sdk.nuspec")!.Open();var xml=XDocument.Load(input);var ns=xml.Root!.Name.Namespace;var metadata=xml.Root.Element(ns+"metadata")!;
    Check((string?)metadata.Element(ns+"id")=="RizToolbox.Sdk"&&(string?)metadata.Element(ns+"version")==SdkInfo.Version.ToString(),"NuGet contract version/identity differs from SDK.");
    Check((string?)metadata.Element(ns+"license")=="MIT"&&!metadata.Descendants(ns+"dependency").Any(),"License or dependency ownership is incorrect.");
    using var readme = new StreamReader(zip.GetEntry("README.md")!.Open());
    Check(readme.ReadToEnd().Contains($"Version=\"{SdkInfo.Version}\"", StringComparison.Ordinal),
        "The packed SDK instructions name a different contract version.");
}
void ReferenceIdentity()
{
    using var zip=Open();using var input=zip.GetEntry("ref/net6.0/RizToolbox.Sdk.dll")!.Open();using var memory=new MemoryStream();input.CopyTo(memory);memory.Position=0;
    using var pe=new PEReader(memory);var reader=pe.GetMetadataReader();var assembly=reader.GetAssemblyDefinition();
    Check(reader.GetString(assembly.Name)=="RizToolbox.Sdk"&&assembly.Version==new Version(1,0,0,0),"Binary SDK identity changed.");
    bool reference=assembly.GetCustomAttributes().Any(handle=>{var attribute=reader.GetCustomAttribute(handle);if(attribute.Constructor.Kind!=HandleKind.MemberReference)return false;var member=reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);if(member.Parent.Kind!=HandleKind.TypeReference)return false;var type=reader.GetTypeReference((TypeReferenceHandle)member.Parent);return reader.GetString(type.Name)=="ReferenceAssemblyAttribute"&&reader.GetString(type.Namespace)=="System.Runtime.CompilerServices";});
    Check(reference,"Packed DLL is executable implementation rather than a compiler reference.");
}
IEnumerable<(string Name,string Id)> Entries()=>new[]{("AuthorOne","author.one"),("AuthorTwo","author.two")};
void RestoreAssets()
{
    foreach(var entry in Entries())
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(projects,entry.Name,"obj/project.assets.json")));
        var target=document.RootElement.GetProperty("targets").EnumerateObject().Single().Value.GetProperty($"RizToolbox.Sdk/{SdkInfo.Version}");
        Check(target.GetProperty("compile").TryGetProperty("ref/net6.0/RizToolbox.Sdk.dll",out _),"Consumer did not select compiler reference.");
        Check(!target.TryGetProperty("runtime",out var runtime)||!runtime.EnumerateObject().Any(),"Consumer restored SDK runtime assets.");
    }
}
void OutputOwnership()
{
    foreach(var entry in Entries())
    {
        var files=Directory.GetFiles(Path.Combine(projects,entry.Name,"bin/Release/net6.0"),"*.dll").Select(Path.GetFileName).ToArray();
        Check(files.SequenceEqual(new[]{entry.Name+".dll"}),"Template output includes a host or private runtime DLL.");
        Check(Directory.GetFiles(Path.Combine(mods,entry.Id),"*.dll").Select(Path.GetFileName).SequenceEqual(new[]{entry.Name+".dll"}),"Packaged template contains SDK/host copies.");
    }
}
void PluginReferences()
{
    foreach(var entry in Entries())
    {
        using var pe=new PEReader(File.OpenRead(Path.Combine(mods,entry.Id,entry.Name+".dll")));var reader=pe.GetMetadataReader();
        var references=reader.AssemblyReferences.Select(reader.GetAssemblyReference).ToArray();
        Check(references.Any(r=>reader.GetString(r.Name)=="RizToolbox.Sdk"&&r.Version==new Version(1,0,0,0)),"Template did not compile against stable SDK identity.");
        Check(!references.Any(r=>reader.GetString(r.Name).StartsWith("RizToolbox.")&&reader.GetString(r.Name)!="RizToolbox.Sdk"),"Template references an implementation project.");
    }
}
void PackageLoading()
{
    using var packages=PluginPackageLoader.LoadDirectory(mods,new RecordingLog());Check(packages.Diagnostics.Count==0&&packages.Plugins.Count==2,"Formal loader rejected generated template: "+string.Join(";",packages.Diagnostics.Select(x=>x.Message)));
    Check(packages.Plugins.Select(x=>x.Manifest.Id).OrderBy(x=>x).SequenceEqual(Entries().Select(x=>x.Id)),"Template ID substitution differs from manifest.");
    var contexts=packages.Plugins.Select(p=>AssemblyLoadContext.GetLoadContext(p.GetType().Assembly)).ToArray();
    Check(contexts[0]!=contexts[1]&&contexts.All(x=>x!=AssemblyLoadContext.Default),"Generated packages share a load context.");
    Check(packages.Plugins.All(x=>typeof(IRizPlugin).IsAssignableFrom(x.GetType())),"Host and template SDK identities differ.");
    Check(packages.Plugins.All(x => x.Manifest.MinSdk == SdkInfo.Version && x.Manifest.MaxSdkExclusive == new Version(2, 0, 0)),
        "Generated manifests do not declare the current SDK compatibility range.");
}
void HostCallbacks()
{
    using var f = new HostFixture(mods);
    f.Capabilities.Register<IOfficialChartSelectionService>(UiInputCapabilityIds.OfficialChartSelection, new OfficialChart());
    f.Start();
    Check(f.Log.Messages.Count(x => x.EndsWith(": selected official chart fixture.chart with Play focus unknown", StringComparison.Ordinal)) == 2,
        "Both independently compiled packages did not read the selected chart through the SDK.");
    f.Events.Publish(new GameSceneChanged("menu", "Level"));
    Check(f.Log.Messages.Count(x => x.Contains("scene menu -> Level")) == 2, "Both templates did not receive the scene event exactly once.");
}
void HostDisable(){using var f=new HostFixture(mods);f.Start();Check(f.Host.Disable("author.one").Success,"Disable failed.");f.Events.Publish(new GameSceneChanged("before","after"));Check(f.Log.Messages.Count(x=>x.Contains("author.one: scene before -> after"))==0&&f.Log.Messages.Count(x=>x.Contains("author.two: scene before -> after"))==1,"Disabled template retained a callback or removed its neighbor.");}
void CapabilityRecovery()
{
    using var f=new HostFixture(mods);f.Start();f.Capabilities.SetUnavailable(GameCapabilityIds.Scenes,"fixture withdrawal");
    Check(f.Host.Statuses.All(x=>x.State!=PluginState.Enabled),"Dependent templates stayed enabled after provider withdrawal.");
    f.Capabilities.Register<IGameStateService>(GameCapabilityIds.Scenes,new Scenes());
    Check(f.Host.Statuses.All(x=>x.State==PluginState.Enabled),"Requested templates did not recover with provider.");f.Events.Publish(new GameSceneChanged("old","new"));
    Check(f.Log.Messages.Count(x=>x.Contains("scene old -> new"))==2,"Recovery duplicated or lost subscriptions.");
}
void PackagedErrors()
{
    using var f=new HostFixture(mods);f.Start();
    foreach(var entry in Entries())
    {
        var peer=entry.Id=="author.one"?"author.two":"author.one";
        var services=f.PluginServices(entry.Id);var id=peer+".errors";
        var missing=services.Lookup<IPluginDataService>(new("absent.service",new(1,0)));
        Check(!missing.Available&&missing.Error is {Code:ErrorCode.ServiceUnavailable,CapabilityId:InterPluginCapabilityIds.Services,TargetId:"absent.service",Retryable:true},"Missing provider lost structured context.");
        var incompatible=services.Lookup<IPluginDataService>(new(id,new(2,0)));
        Check(!incompatible.Available&&incompatible.Error?.Code==ErrorCode.ServiceUnavailable&&incompatible.Error.TargetId==id&&!incompatible.Error.Retryable,"An incompatible provider was accepted or misclassified.");
        var lookup=services.Lookup<IPluginDataService>(new(id,new(1,0),new(2,0)));
        Check(lookup.Available&&lookup.Service is not null,"Independent shared service unavailable: "+lookup.Error?.Message);
        var known=lookup.Service!.Invoke(new("known"));
        Check(!known.Success&&known.Value is null&&known.Error==new OperationError(ErrorCode.Conflict,"TargetMissing: a translated explanation","test.structured","note/7",true),"The proxy flattened a declared error or classified its text.");
        var future=lookup.Service.Invoke(new("future"));
        Check(!future.Success&&future.Value is null&&future.Error==new OperationError((ErrorCode)987654,"A future failure","test.future","track/9",true)&&future.Error.KnownCode==ErrorCode.Unknown,"A future error became success or lost its numeric code.");
        Check(f.ProviderCalls(peer)==2,"A lookup unexpectedly entered the provider.");
    }
    Check(f.Host.Statuses.All(x=>x.State==PluginState.Enabled),"A declared operation failure faulted a package.");
    f.Events.Publish(new GameSceneChanged("errors","still-live"));
    Check(f.Log.Messages.Count(x=>x.Contains("scene errors -> still-live"))==2,"Returning failures damaged independent callbacks.");
}
void PackagedScopeErrors()
{
    using var f=new HostFixture(mods);f.Start();var services=f.PluginServices("author.one");
    var query=new ServiceQuery("author.two.errors",new(1,0));
    var rejected=Task.Run(()=>services.Lookup<IPluginDataService>(query)).GetAwaiter().GetResult();
    Check(!rejected.Available&&rejected.Service is null&&rejected.Error is {Code:ErrorCode.WrongThread,CapabilityId:InterPluginCapabilityIds.Services,TargetId:"author.two.errors"},"Worker service lookup threw or omitted its structured context.");
    Check(f.ProviderCalls("author.two")==0,"Worker lookup entered an independent provider.");
    Check(f.Host.Disable("author.one").Success,"Independent consumer did not stop.");
    var stale=services.Lookup<IPluginDataService>(query);
    Check(!stale.Available&&stale.Service is null&&stale.Error is {Code:ErrorCode.ScopeExpired,CapabilityId:InterPluginCapabilityIds.Services,TargetId:"author.two.errors"},"Expired service view threw or survived its scope.");
    Check(f.ProviderCalls("author.two")==0&&f.Host.Statuses.Single(x=>x.Id=="author.two").State==PluginState.Enabled,"Rejecting a stale consumer damaged the provider.");
    Check(f.Host.Enable("author.one").Success,"Consumer reactivation failed.");
    Check(f.PluginServices("author.one").Lookup<IPluginDataService>(query).Service!.Invoke(new("fresh")).Success,"Fresh service view did not recover.");
    Check(services.Lookup<IPluginDataService>(query).Error?.Code==ErrorCode.ScopeExpired&&f.ProviderCalls("author.two")==1,"Reactivation revived the old scope.");
}
void PackagedContractErrors()
{
    using var f=new HostFixture(mods);f.Start();
    foreach(var entry in Entries())
    {
        var error=f.ProbeValue<OperationError>(entry.Id,"PrivateContractFailure");
        Check(error.Code==ErrorCode.ContractIdentityMismatch&&error.CapabilityId==InterPluginCapabilityIds.Services&&error.TargetId==entry.Id+".private"&&!error.Retryable,"A private package interface lost its exact identity rejection.");
        Check(!f.PluginServices(entry.Id).Lookup<IPluginDataService>(new(entry.Id+".private",new(1,0))).Available,"A rejected private provider was registered.");
    }
    Check(f.Host.Statuses.All(x=>x.State==PluginState.Enabled),"A rejected contract faulted its package.");
}
void PackagedFaultIsolation()
{
    using var f=new HostFixture(mods);f.Start();var consumer=f.PluginServices("author.two");
    var bad=consumer.Lookup<IPluginDataService>(new("author.one.errors",new(1,0))).Service!;
    Exception? fault=null;try{bad.Invoke(new("fault"));}catch(Exception error){fault=error;}
    Check(fault is not null&&f.ProviderCalls("author.one")==1,"A real provider fault was suppressed or retried.");
    Check(f.Host.Statuses.Single(x=>x.Id=="author.one").State==PluginState.Faulted&&f.Host.Statuses.Single(x=>x.Id=="author.two").State==PluginState.Enabled,"A provider fault was attributed to the wrong package.");
    var withdrawn=consumer.Lookup<IPluginDataService>(new("author.one.errors",new(1,0)));
    Check(!withdrawn.Available&&withdrawn.Error is {Code:ErrorCode.ServiceUnavailable,TargetId:"author.one.errors",Retryable:true},"Faulted provider retained its published service.");
    Check(consumer.Lookup<IPluginDataService>(new("author.two.errors",new(1,0))).Service!.Invoke(new("alive")).Success,"An unrelated provider stopped after the fault.");
    f.Events.Publish(new GameSceneChanged("fault","isolated"));
    Check(f.Log.Messages.Count(x=>x.Contains("author.one: scene fault -> isolated"))==0&&f.Log.Messages.Count(x=>x.Contains("author.two: scene fault -> isolated"))==1,"Fault cleanup removed the wrong package callbacks.");
}
sealed class OfficialChart : IOfficialChartSelectionService
{
    public Result<OfficialChartSelectionSnapshot> ReadCurrent()
        => Result<OfficialChartSelectionSnapshot>.Ok(new("fixture.chart", 1, 2, 3, PlayButton: null));
}
sealed class Scenes:IGameStateService {public OperationResult<GameStateSnapshot> GetSnapshot()=>OperationResult<GameStateSnapshot>.Ok(new("MainMenu",null,null,false));}
sealed class RecordingLog:ILogger {public List<string> Messages{get;}=new();public void Log(LogLevel level,string message,Exception? exception=null)=>Messages.Add(message);}
sealed class HostFixture:IDisposable
{
    public RecordingLog Log{get;}=new();public MainThreadDispatcher Main{get;}=new();public EventHub Events{get;}public CapabilityRegistry Capabilities{get;}=new(new("test","test","test","test"));public PluginHost Host{get;}public InterPluginServiceRegistry Services{get;}private readonly PluginLoadResult _packages;
    public HostFixture(string mods){Events=new(Log);Capabilities.Register<IGameStateService>(GameCapabilityIds.Scenes,new Scenes());Services=new(Main,Log);Capabilities.RegisterScoped<IPluginServices>(InterPluginCapabilityIds.Services,context=>Services.ForPlugin(context));Host=new(Capabilities,Main,Events,Log);Services.ProviderFaulted+=(id,error)=>Host.ReportPluginFault(id,error);_packages=PluginPackageLoader.LoadDirectory(mods,Log);if(_packages.Diagnostics.Count!=0)throw new InvalidDataException("Template package load failed.");foreach(var p in _packages.Plugins)if(!Host.Register(p).Success)throw new InvalidDataException("Template registration failed.");}
    public T ProbeValue<T>(string id,string property){var plugin=_packages.Plugins.Single(x=>x.Manifest.Id==id);var probe=plugin.GetType().Assembly.GetType(plugin.GetType().Namespace+".DistributionProbe",throwOnError:true)!;return (T)probe.GetProperty(property,BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;}
    public IPluginServices PluginServices(string id)=>ProbeValue<IPluginServices>(id,"Services");
    public int ProviderCalls(string id)=>ProbeValue<int>(id,"ProviderCalls");
    public void Start(){foreach(var p in _packages.Plugins)if(!Host.Enable(p.Manifest.Id).Success)throw new InvalidDataException("Template activation failed.");}
    public void Dispose(){Host.Dispose();Services.Dispose();_packages.Dispose();Main.Dispose();}
}
