using System.Collections;
using System.Text.Json;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

// Original Q2 probes; no producer or Q1 harness dependency. All usage is sequential/single-owner.
static class Program
{
    static readonly List<object> Trace = new();
    static readonly List<Check> Checks = new();
    static readonly BoltMessageCodec Codec = new();
    const string ProviderDescription = "The operation provider failed while executing the request.";
    const string RowDescription = "A result row does not match the announced result fields.";
    static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] pairs) =>
        new(pairs.Select(x => new KeyValuePair<string, PackStreamValue?>(x.Key, x.Value)).ToArray());
    static RunRequest Run() => new("Q2 synthetic request", Map(), Map());
    static HelloRequest Hello() => new(Map(("user_agent", new PackStreamString("q2/1")),
        ("bolt_agent", Map(("product", new PackStreamString("q2/1"))))));
    static LogonRequest Logon() => new(Map(("scheme", new PackStreamString("none"))), BoltAuthScheme.None);
    static void Assert(string id, bool condition, string expectation, string actual) =>
        Checks.Add(new(id, condition, expectation, actual));
    static string Seq(BoltSessionResult? r) => r is null ? "(void)" : string.Join(",", r.Responses.Select(x => x switch {
        SuccessResponse => "SUCCESS", FailureResponse => "FAILURE", RecordResponse => "RECORD", IgnoredResponse => "IGNORED", _ => "?" }));
    static object Metadata(PackStreamMap m) => m.Entries.ToDictionary(x => x.Key, x => Value(x.Value));
    static object? Value(PackStreamValue? v) => v switch {
        PackStreamString s => s.Value, PackStreamInteger n => n.Value, PackStreamBoolean b => b.Value,
        PackStreamMap m => Metadata(m), PackStreamList l => l.Items.Select(Value).ToArray(), null => null, _ => v.GetType().Name };
    static object Response(BoltResponse r) => new { kind=r.GetType().Name,
        metadata=r switch { FailureResponse f => Metadata(f.Metadata), SuccessResponse s => Metadata(s.Metadata), _ => null },
        wire=Convert.ToHexString(Codec.EncodeResponse(r)) };

    sealed class Context
    {
        public string Id;
        public Provider Provider;
        public BoltConnectionSession Session;
        public Exception? Escaped;
        public Context(string id, Op? op = null, bool authenticate = true, bool noVerifier = false)
        {
            Id = id; Provider = new Provider(op);
            Session = new(Provider, new BoltSessionOptions { Verifier = noVerifier ? null : new Verifier(), MaxRecordsPerResponse = 2 });
            if (authenticate) { Session.Receive(Hello()); Session.Receive(Logon()); }
        }
        public BoltSessionResult? Send(string action, BoltRequest request) => Act(action, () => Session.Receive(request));
        public BoltSessionResult? Act(string action, Func<BoltSessionResult?> call)
        {
            var start = Session.State; var owned = Session.HasActiveOperation; var opens = Provider.Opens;
            BoltSessionResult? result = null; Escaped = null;
            try { result = call(); } catch (Exception ex) { Escaped = ex; }
            Trace.Add(new { scenario=Id, action, starting_state=start.ToString(), owned_before=owned,
                response=Seq(result), responses=result?.Responses.Select(Response).ToArray(), state=Session.State.ToString(),
                should_close=result?.ShouldClose, owned_after=Session.HasActiveOperation,
                exception=Escaped?.GetType().Name, opens=Provider.Opens, opened_during_action=Provider.Opens > opens,
                cleanup_type=Session.LastCleanupFailure?.GetType().Name,
                cleanup_identity=Provider.Operations.FindIndex(o => ReferenceEquals(o.CleanupError, Session.LastCleanupFailure)),
                operations=Provider.Operations.Select(o => new { o.Name, o.DisposeAttempts, o.CleanupSucceeded, o.ResourceLive,
                    o.Reads, o.FieldsReads, o.DisposeObservedState, o.DisposeObservedOwned }).ToArray() });
            return result;
        }
        public void Dispose() => Act("Dispose", () => { Session.Dispose(); return null; });
        public void Disconnect() => Act("Disconnect", () => { Session.Disconnect(); return null; });
        public void Install() { if (Provider.Initial is { } op) op.ObserveSession = Session; Send("RUN", Run()); }
    }
    sealed class Verifier : IBoltCredentialVerifier { public bool Verify(LogonRequest _) => true; }
    sealed class Provider : IBoltOperationProvider
    {
        public Op? Initial; public int Opens; public bool ThrowPrepare; public List<Op> Operations = new();
        public Provider(Op? op) => Initial = op;
        public IBoltOperation? Prepare(RunRequest _)
        {
            Opens++; if (ThrowPrepare) throw new InvalidOperationException("synthetic prepare error");
            if (Initial is null) return null;
            var op = Opens == 1 ? Initial : new Op { Name = "later-" + Opens };
            Operations.Add(op); return op;
        }
    }
    sealed class Op : IBoltOperation
    {
        public string Name = "initial";
        public bool ThrowDispose = true, NullFields, BadFieldElement;
        public int FieldsThrowAt, ThrowReadAt, RowWidth = 1, Rows = 1;
        public int FieldsReads, Reads, DisposeAttempts;
        public bool ResourceLive = true, CleanupSucceeded;
        public Exception CleanupError = new InvalidOperationException("synthetic cleanup error");
        public BoltConnectionSession? ObserveSession;
        public string? DisposeObservedState; public bool? DisposeObservedOwned;
        public IReadOnlyList<string> Fields {
            get { FieldsReads++; if (FieldsReads == FieldsThrowAt) throw new InvalidOperationException("synthetic fields error");
                if (NullFields) return null!;
                return BadFieldElement ? new[] { (string)null! } : new[] { "x" }; }
        }
        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            Reads++; if (Reads == ThrowReadAt) throw new InvalidOperationException("synthetic read error");
            return Reads <= Rows ? Enumerable.Range(0, RowWidth).Select(i => (PackStreamValue?)new PackStreamInteger(i)).ToArray() : null;
        }
        public void Dispose()
        {
            DisposeAttempts++; DisposeObservedState=ObserveSession?.State.ToString(); DisposeObservedOwned=ObserveSession?.HasActiveOperation;
            if (ThrowDispose) throw CleanupError;
            CleanupSucceeded=true; ResourceLive=false;
        }
    }
    sealed class HostileException : Exception { public override string Message => throw new InvalidOperationException("Message getter called"); }
    record Check(string Id, bool Pass, string Expected, string Actual);

    static void TerminalCases()
    {
        foreach (var path in new[] { "Goodbye", "Disconnect", "Dispose", "Goodbye,Disconnect", "Goodbye,Dispose", "Disconnect,Dispose", "Dispose,Dispose", "Goodbye,Goodbye" })
        {
            var op = new Op(); var c = new Context("terminal-" + path, op); c.Install();
            foreach (var action in path.Split(',')) {
                if (action == "Goodbye") { var r=c.Send(action,new GoodbyeRequest()); Assert(c.Id+"-no-response-"+Trace.Count,Seq(r)=="", "no response", Seq(r)); }
                if (action == "Disconnect") c.Disconnect();
                if (action == "Dispose") c.Dispose();
                Assert(c.Id+"-contained-"+Trace.Count, c.Escaped is null && c.Session.IsDefunct, "no exception;Defunct", $"{c.Escaped?.GetType().Name};{c.Session.State}");
            }
            c.Send("late RUN",Run());
            Assert(c.Id+"-late",c.Provider.Opens==1 && (c.Escaped is null || c.Escaped is ObjectDisposedException),"no provider reopening; ObjectDisposed only if disposed",$"opens={c.Provider.Opens};exception={c.Escaped?.GetType().Name}");
            c.Disconnect(); c.Dispose(); c.Dispose();
            Assert(c.Id+"-attempts",op.DisposeAttempts==1 && !c.Session.HasActiveOperation,"exactly one attempted disposal; detached",$"attempts={op.DisposeAttempts};owned={c.Session.HasActiveOperation}");
            Assert(c.Id+"-diagnostic",ReferenceEquals(op.CleanupError,c.Session.LastCleanupFailure),"same cleanup exception",c.Session.LastCleanupFailure?.GetType().Name ?? "null");
            Assert(c.Id+"-detach-before-call",op.DisposeObservedOwned==false,"ownership detached before actual Dispose call",op.DisposeObservedOwned.ToString()!);
        }
        foreach (var badCleanup in new[] { false, true }) {
            var op=new Op { NullFields=true, ThrowDispose=badCleanup }; var c=new Context("null-fields-"+badCleanup,op); c.Install();
            Assert(c.Id,c.Escaped is null && c.Session.State==(badCleanup?BoltConnectionState.Defunct:BoltConnectionState.Failed) && op.DisposeAttempts==1 && !c.Session.HasActiveOperation,"contained;Failed if clean, Defunct if cleanup fails;one attempt;no cursor",$"{c.Session.State};attempts={op.DisposeAttempts};exception={c.Escaped?.GetType().Name}");
            c.Send("RESET",new ResetRequest()); c.Send("later RUN",Run()); c.Disconnect(); c.Dispose();
            Assert(c.Id+"-retry",op.DisposeAttempts==1 && (badCleanup ? c.Provider.Opens==1 : c.Provider.Opens==2),"no repeated disposal; only clean branch reusable",$"attempts={op.DisposeAttempts};opens={c.Provider.Opens}");
        }
        var resetOp=new Op(); var reset=new Context("reset-cleanup-failure",resetOp); reset.Install();
        var rr=reset.Send("RESET",new ResetRequest());
        Assert("reset-fail-closed",reset.Escaped is null && reset.Session.IsDefunct && Seq(rr)=="FAILURE" && rr!.ShouldClose,"FAILURE/Defunct/close/no escape",$"{Seq(rr)}/{reset.Session.State}/{rr?.ShouldClose}");
        Assert("reset-transient",resetOp.DisposeObservedState=="Interrupted","Interrupted during disposal",resetOp.DisposeObservedState!);
        reset.Send("late RESET",new ResetRequest()); reset.Dispose(); Assert("reset-once",resetOp.DisposeAttempts==1,"one attempt",resetOp.DisposeAttempts.ToString());
        var fatalOp=new Op(); var fatal=new Context("protocol-primary-plus-cleanup",fatalOp); fatal.Install();
        var fr=fatal.Send("illegal RUN",Run());
        Assert("terminal-primary",fr?.Responses.SingleOrDefault() is FailureResponse f && f.GqlStatus=="08N06" && f.Neo4jCode=="Neo.ClientError.Request.Invalid" && fatal.Session.IsDefunct,"primary protocol 08N06 preserved;Defunct",$"{Seq(fr)}/{fatal.Session.State}");
        fatal.Dispose(); Assert("terminal-primary-once",fatalOp.DisposeAttempts==1,"one attempt",fatalOp.DisposeAttempts.ToString());
        var toxicOp=new Op { CleanupError=new HostileException() }; var toxic=new Context("hostile-exception-diagnostic",toxicOp); toxic.Install(); toxic.Disconnect();
        Assert("diagnostic-no-virtual-access",toxic.Escaped is null && ReferenceEquals(toxicOp.CleanupError,toxic.Session.LastCleanupFailure),"recording does not invoke exception Message",toxic.Escaped?.GetType().Name??"none");
    }

    static void FailureAndRecovery()
    {
        foreach (var mode in new[] { "pull-complete", "discard-complete", "pull-read", "discard-read", "row-shape" }) {
            var op=new Op { ThrowReadAt=mode.EndsWith("read")?2:0, RowWidth=mode=="row-shape"?2:1, Rows=3 };
            if (mode.EndsWith("complete")) op.Rows=1;
            var c=new Context(mode,op); c.Install();
            var r=c.Send(mode,mode.StartsWith("discard")?new DiscardRequest(-1,null):new PullRequest(-1,null));
            var expectedDescription=mode=="row-shape"?RowDescription:ProviderDescription;
            Assert(mode+"-primary",c.Escaped is null && r?.Responses.LastOrDefault() is FailureResponse f && f.Description==expectedDescription && f.GqlStatus==(mode=="row-shape"?"50N42":"50N00") && f.Neo4jCode=="Neo.DatabaseError.General.UnknownError" && c.Session.State==BoltConnectionState.Failed,"primary FAILURE preserved;Failed",$"{Seq(r)}/{c.Session.State}/{(r?.Responses.LastOrDefault() as FailureResponse)?.Description}");
            Assert(mode+"-count",op.DisposeAttempts==1 && !c.Session.HasActiveOperation && ReferenceEquals(c.Session.LastCleanupFailure,op.CleanupError),"attempt once;detached;cleanup diagnostic",$"attempts={op.DisposeAttempts};owned={c.Session.HasActiveOperation}");
            if(mode=="pull-read") Assert(mode+"-prefix",Seq(r)=="RECORD,FAILURE","previous valid RECORD precedes primary FAILURE",Seq(r));
            var ignored=c.Send("work after failure",Run()); Assert(mode+"-ignored",Seq(ignored)=="IGNORED" && c.Provider.Opens==1,"IGNORED;no open",$"{Seq(ignored)};opens={c.Provider.Opens}");
            var rr=c.Send("RESET after incomplete cleanup",new ResetRequest());
            Assert(mode+"-safe-reset",c.Session.IsDefunct && Seq(rr)=="FAILURE","incomplete cleanup must not be claimed recovered: FAILURE/Defunct",$"{Seq(rr)}/{c.Session.State};oldResourceLive={op.ResourceLive}");
            c.Send("RUN after RESET",Run());
            Assert(mode+"-no-overlap",c.Provider.Opens==1,"no new provider operation while prior cleanup remains incomplete",$"opens={c.Provider.Opens};oldResourceLive={op.ResourceLive}");
            c.Disconnect(); c.Dispose(); Assert(mode+"-never-retry",op.DisposeAttempts==1,"no second attempt",op.DisposeAttempts.ToString());
        }
        // Failed -> Disconnect separately from Failed -> RESET.
        var fdOp=new Op { ThrowReadAt=1 }; var fd=new Context("failed-disconnect",fdOp); fd.Install(); fd.Send("PULL",new PullRequest(-1,null)); fd.Disconnect(); fd.Dispose();
        Assert("failed-disconnect-once",fdOp.DisposeAttempts==1 && fd.Session.IsDefunct,"Defunct;one attempt",$"{fd.Session.State};{fdOp.DisposeAttempts}");
        var p=new Context("prepare-failure-before-transfer",new Op()); p.Provider.ThrowPrepare=true; var pr=p.Send("RUN",Run());
        Assert("prepare-failure",p.Escaped is null && Seq(pr)=="FAILURE" && p.Session.State==BoltConnectionState.Failed && p.Provider.Operations.Count==0 && p.Session.LastCleanupFailure is null,"provider retains responsibility for unreturned allocations; FAILURE/Failed;no session cleanup",$"{Seq(pr)}/{p.Session.State};acquired={p.Provider.Operations.Count}"); p.Dispose();
        var nullProvider=new Context("provider-refusal"); var np=nullProvider.Send("RUN",Run());
        Assert("provider-refusal",np?.Responses.SingleOrDefault() is FailureResponse nf && nf.GqlStatus=="50N42" && nullProvider.Session.State==BoltConnectionState.Failed,"unsupported query remains recoverable",$"{Seq(np)}/{nullProvider.Session.State}"); nullProvider.Dispose();
        // Successful cleanup control: recovery remains permitted.
        var cleanOp=new Op { ThrowDispose=false, ThrowReadAt=1 }; var clean=new Context("clean-primary-recovery",cleanOp); clean.Install(); clean.Send("PULL",new PullRequest(-1,null));
        var cr=clean.Send("RESET",new ResetRequest()); clean.Send("RUN after clean recovery",Run());
        Assert("clean-reset-control",Seq(cr)=="SUCCESS" && clean.Provider.Opens==2 && !cleanOp.ResourceLive,"clean failure legitimately recovers",$"{Seq(cr)};opens={clean.Provider.Opens};oldResourceLive={cleanOp.ResourceLive}"); clean.Dispose();
    }

    static void MetadataOwnership()
    {
        foreach (var mode in new[] { "first-getter", "second-getter", "null-element" }) {
            var op=new Op { FieldsThrowAt=mode=="first-getter"?1:mode=="second-getter"?2:0, BadFieldElement=mode=="null-element" };
            var c=new Context("run-metadata-"+mode,op); c.Install();
            Assert(c.Id+"-contained",c.Escaped is null && c.Session.IsDefunct,"metadata primary failure contained;cleanup failure makes Defunct",$"exception={c.Escaped?.GetType().Name};state={c.Session.State};owned={c.Session.HasActiveOperation}");
            Assert(c.Id+"-cleanup",op.DisposeAttempts==1 && ReferenceEquals(c.Session.LastCleanupFailure,op.CleanupError),"one guarded cleanup attempt even before installation",$"attempts={op.DisposeAttempts};diagnostic={c.Session.LastCleanupFailure?.GetType().Name}");
            if(mode=="first-getter") {
                c.Send("RUN again after escaped metadata",Run());
                Assert(c.Id+"-no-reopen",c.Provider.Opens==1,"no new acquisition after failed RUN with unretired operation",$"opens={c.Provider.Opens};oldResourceLive={op.ResourceLive}");
            }
            c.Disconnect(); c.Dispose();
            Assert(c.Id+"-eventual-attempt",op.DisposeAttempts==1,"session cleanup owns every returned operation",$"attempts={op.DisposeAttempts};oldResourceLive={op.ResourceLive}");
        }
    }

    static void Telemetry()
    {
        foreach(var api in new long[] {0,1,2,3,-1,4,9001,long.MaxValue}) {
            var c=new Context("telemetry-"+api); var r=c.Send("TELEMETRY",new TelemetryRequest(api));
            if(api>=0 && api<=3) Assert(c.Id,r?.Responses.SingleOrDefault() is SuccessResponse s && !s.Metadata.Entries.Any() && c.Session.State==BoltConnectionState.Ready,"empty SUCCESS/Ready",$"{Seq(r)}/{c.Session.State}");
            else {
                var f=r?.Responses.SingleOrDefault() as FailureResponse;
                var names=f?.Metadata.Entries.Select(e=>e.Key).OrderBy(x=>x).ToArray();
                Assert(c.Id+"-metadata",f?.GqlStatus=="50N00" && f.Neo4jCode=="Neo.DatabaseError.General.UnknownError" && f.Description=="The telemetry API identifier is not valid for this server." && f.DiagnosticRecord is { } dr && !dr.Entries.Any() && names!.SequenceEqual(new[]{"description","diagnostic_record","gql_status","neo4j_code"}) && c.Session.State==BoltConnectionState.Failed && r!.ShouldClose==false,"exact four-field accepted FAILURE metadata/Failed/open",JsonSerializer.Serialize(f is null?null:Metadata(f.Metadata)));
                var wire=Codec.EncodeResponse(f!); var decoded=Codec.DecodeResponse(wire) as FailureResponse;
                Assert(c.Id+"-wire",decoded?.GqlStatus=="50N00" && decoded.Neo4jCode==f!.Neo4jCode,"B3-A encoded metadata round trip",Convert.ToHexString(wire));
                var ignored=c.Send("RUN in Failed",Run()); Assert(c.Id+"-ignored",Seq(ignored)=="IGNORED" && c.Provider.Opens==0,"IGNORED;no provider",$"{Seq(ignored)};opens={c.Provider.Opens}");
                var reset=c.Send("RESET",new ResetRequest()); Assert(c.Id+"-recovery",Seq(reset)=="SUCCESS" && c.Session.State==BoltConnectionState.Ready && c.Session.LastCleanupFailure is null,"SUCCESS/Ready/no cleanup diagnostic",$"{Seq(reset)}/{c.Session.State}");
            }
            c.Dispose();
        }
        var bytes=Convert.FromHexString("B1548178"); string? category=null;
        try { Codec.DecodeRequest(bytes); } catch(BoltMessageException ex) { category=ex.Category.ToString(); }
        Assert("telemetry-type-boundary",category=="InvalidFieldType","B3-A InvalidFieldType",category??"none");
        var wrong=new Context("telemetry-wrong-type"); var wr=wrong.Act("ReceiveBytes TELEMETRY string",()=>wrong.Session.ReceiveBytes(bytes));
        Assert("telemetry-type-existing-gap",Seq(wr)=="FAILURE" && wrong.Session.IsDefunct && (wr!.Responses[0] as FailureResponse)?.GqlStatus=="08N06","retained Q1 seam gap: FAILURE 08N06/Defunct",$"{Seq(wr)}/{wrong.Session.State}"); wrong.Dispose();
    }

    static void DiagnosticsAndTransport()
    {
        var op=new Op { Rows=0 }; var c=new Context("diagnostic-lifetime",op); c.Install(); c.Send("PULL complete",new PullRequest(-1,null));
        var diagnostic=c.Session.LastCleanupFailure!; diagnostic.Data["reviewer_marker"]="modified";
        Assert("diagnostic-mutable-alias",ReferenceEquals(diagnostic,op.CleanupError) && Equals(c.Session.LastCleanupFailure!.Data["reviewer_marker"],"modified"),"observation: exposes same mutable exception",diagnostic.Data["reviewer_marker"]!.ToString()!);
        c.Send("RESET",new ResetRequest());
        Assert("diagnostic-retained",ReferenceEquals(c.Session.LastCleanupFailure,diagnostic),"most recent failure retained across successful RESET",c.Session.LastCleanupFailure?.GetType().Name??"null");
        c.Send("RUN second operation",Run()); c.Disconnect();
        Assert("diagnostic-latest",ReferenceEquals(c.Session.LastCleanupFailure,c.Provider.Operations.Last().CleanupError) && !ReferenceEquals(diagnostic,c.Session.LastCleanupFailure),"later cleanup failure replaces earlier diagnostic","same-as-latest="+ReferenceEquals(c.Session.LastCleanupFailure,c.Provider.Operations.Last().CleanupError));
        c.Dispose();
        var property=typeof(BoltConnectionSession).GetProperty("LastCleanupFailure")!;
        Assert("diagnostic-accessibility",property.GetMethod!.IsPublic && property.SetMethod!.IsPrivate && property.PropertyType==typeof(Exception),"public Exception getter;private setter",$"getterPublic={property.GetMethod?.IsPublic};setterPrivate={property.SetMethod?.IsPrivate}");
        var sendOp=new Op(); var send=new Context("response-send-failure",sendOp); send.Install(); var r=send.Send("illegal RUN",Run());
        var senderFailed=false; try { foreach(var response in r!.Responses) { _=Codec.EncodeResponse(response); throw new IOException("synthetic sender failure"); } } catch(IOException) { senderFailed=true; send.Disconnect(); }
        Assert("response-send-terminal",senderFailed && send.Session.IsDefunct && sendOp.DisposeAttempts==1,"returned terminal outcome stays Defunct when caller emission fails",$"failed={senderFailed};state={send.Session.State};attempts={sendOp.DisposeAttempts}"); send.Dispose();
    }

    static void Matrix()
    {
        foreach(var state in new[]{"Negotiation","Authentication","Ready","Streaming","Failed","Defunct"})
        foreach(var action in new[]{"RUN","PULL","DISCARD","RESET","GOODBYE","TELEMETRY","Disconnect","Dispose"}) {
            var c=new Context("matrix-"+state+"-"+action,new Op{ThrowDispose=false,Rows=0},authenticate:false);
            if(state!="Negotiation") c.Session.Receive(Hello());
            if(state is "Ready" or "Streaming" or "Failed" or "Defunct") c.Session.Receive(Logon());
            if(state=="Streaming") c.Session.Receive(Run());
            if(state=="Failed") c.Session.Receive(new TelemetryRequest(9001));
            if(state=="Defunct") c.Session.Receive(new GoodbyeRequest());
            BoltSessionResult? r=null;
            if(action=="Disconnect") c.Disconnect(); else if(action=="Dispose") c.Dispose(); else r=c.Send(action,action switch {
                "RUN"=>Run(),"PULL"=>new PullRequest(-1,null),"DISCARD"=>new DiscardRequest(-1,null),"RESET"=>new ResetRequest(),"GOODBYE"=>new GoodbyeRequest(),_=>new TelemetryRequest(1)});
            var expectedState="Defunct"; var expectedResponse="FAILURE";
            if(state=="Defunct" || action=="GOODBYE") expectedResponse="";
            else if(action is "Disconnect" or "Dispose") expectedResponse="(void)";
            else if(state=="Failed" && action!="RESET") {expectedState="Failed";expectedResponse="IGNORED";}
            else if(action=="RESET" && state is "Ready" or "Streaming" or "Failed") {expectedState="Ready";expectedResponse="SUCCESS";}
            else if(state=="Ready" && action=="RUN") {expectedState="Streaming";expectedResponse="SUCCESS";}
            else if(state=="Ready" && action=="TELEMETRY") {expectedState="Ready";expectedResponse="SUCCESS";}
            else if(state=="Streaming" && action is "PULL" or "DISCARD") {expectedState="Ready";expectedResponse="SUCCESS";}
            if(action is "Disconnect" or "Dispose") {expectedResponse="(void)";expectedState="Defunct";}
            Assert(c.Id,c.Escaped is null && c.Session.State.ToString()==expectedState && Seq(r)==expectedResponse,expectedResponse+"/"+expectedState,Seq(r)+"/"+c.Session.State);
            c.Session.Dispose();
        }
        var auth=new Context("no-verifier",authenticate:false,noVerifier:true); auth.Send("HELLO",Hello()); var ar=auth.Send("LOGON",Logon());
        Assert("authentication-fail-closed",auth.Session.IsDefunct && ar?.Responses.SingleOrDefault() is FailureResponse f && f.Neo4jCode=="Neo.ClientError.Security.Unauthorized","no verifier => auth FAILURE/Defunct",$"{Seq(ar)}/{auth.Session.State}"); auth.Dispose();
    }
    public static int Main(string[] args)
    {
        TerminalCases(); FailureAndRecovery(); Telemetry(); DiagnosticsAndTransport(); Matrix(); MetadataOwnership();
        var failed=Checks.Count(x=>!x.Pass);
        var output=new { reviewed_head="801badcea8523372d4ed83f8f2942d80bc0373e1", implementation="454ae005245d6511b672f8962092829839fb7153",
            checks=Checks.Count, failed, traces=Trace.Count, results=Checks, trace=Trace };
        File.WriteAllText(args[0],JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine);
        Console.WriteLine($"checks={Checks.Count} passed={Checks.Count-failed} failed={failed} trace_steps={Trace.Count}");
        foreach(var c in Checks.Where(x=>!x.Pass)) Console.WriteLine($"FAIL {c.Id}: expected={c.Expected};actual={c.Actual}");
        return failed==0?0:4;
    }
}
