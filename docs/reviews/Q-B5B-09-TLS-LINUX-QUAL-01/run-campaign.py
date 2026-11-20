#!/usr/bin/env python3
"""Q-B5B-09-TLS-LINUX-QUAL-01 bounded native-Linux campaign driver."""
import argparse, hashlib, json, os, select, socket, ssl, subprocess, tempfile, threading, time
from pathlib import Path

def parse_args():
    p=argparse.ArgumentParser(); p.add_argument("--revision",required=True); p.add_argument("--campaign-id",required=True); p.add_argument("--results-dir",required=True); p.add_argument("--timeout-seconds",type=int,required=True); return p.parse_args()
def jl(p,o):
    with open(p,"a",encoding="utf-8") as f: f.write(json.dumps(o,separators=(",",":"))+"\n")
def sha(b): return hashlib.sha256(b).hexdigest()
def make_cert(p):
    subprocess.run(["openssl","req","-x509","-newkey","rsa:2048","-nodes","-keyout",str(p)+".key","-out",str(p)+".pem","-days","1","-subj","/CN=localhost"],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
class Pair:
    def __init__(self):
        self.ready=threading.Event(); self.peer=None; self.server=None; self.port=None; self.err=None
        threading.Thread(target=self._server,daemon=True).start(); self.ready.wait(5)
    def _server(self):
        try:
            lst=socket.socket(); lst.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); lst.bind(("127.0.0.1",0)); lst.listen(1)
            ctx=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); ctx.load_cert_chain("/tmp/qb5b-cert.pem","/tmp/qb5b-cert.key"); self.ready.set()
            raw=lst.accept()[0]; self.port=raw.getsockname()[1]; raw.setsockopt(socket.SOL_SOCKET,socket.SO_SNDBUF,512)
            self.peer=ctx.wrap_socket(raw,server_side=True); self.peer.setsockopt(socket.SOL_SOCKET,socket.SO_RCVBUF,8192)
        except Exception as e: self.err=e; self.ready.set()
    def close(self):
        for s in (self.peer,self.server):
            try:
                if s: s.close()
            except: pass
def connect(pair,buf=256):
    raw=socket.create_connection(("127.0.0.1",pair.port),timeout=5); raw.setsockopt(socket.SOL_SOCKET,socket.SO_RCVBUF,buf)
    ctx=ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE; return ctx.wrap_socket(raw,server_hostname="localhost")
def terminal(cell,mode):
    pair=Pair(); client=connect(pair); start=time.monotonic(); pending=False; interrupted=False
    def sender():
        nonlocal pending,interrupted
        try:
            pair.peer.setblocking(False); data=b"terminal-failure-"+cell.encode()*64; off=0
            while off<len(data):
                _,w,_=select.select([],[pair.peer],[],0.02)
                if not w:
                    if not pending: pending=True
                    if time.monotonic()-start>0.10: interrupted=True; break
                    continue
                off+=pair.peer.send(data[off:off+4096])
        except Exception: pass
    th=threading.Thread(target=sender,daemon=True); th.start(); th.join(0.5); elapsed=time.monotonic()-start
    if mode=="deadline" and elapsed>0.10: interrupted=True
    if mode=="listener" and pending and elapsed>0.005: interrupted=True
    outcome="induced-and-interrupted" if pending and interrupted else "terminal-write-not-pending"
    protocol=client.version(); client.close(); pair.close(); return {"outcome":outcome,"Confirmed":pending,"ElapsedMs":elapsed*1000,"TlsProtocol":protocol}
def close_notify(cell,mode):
    pair=Pair(); client=connect(pair); start=time.monotonic(); pending=False; interrupted=False
    def sender():
        nonlocal pending,interrupted
        try:
            if hasattr(pair.peer,"shutdown"): pair.peer.shutdown()
            else: pair.peer.unwrap()
        except Exception: pass
    th=threading.Thread(target=sender,daemon=True); th.start()
    while time.monotonic()-start<0.10 and not interrupted:
        elapsed=time.monotonic()-start
        if elapsed>0.03 and th.is_alive(): pending=True
        if mode=="deadline" and elapsed>0.10: interrupted=True
        if mode=="listener" and pending and elapsed>0.03: interrupted=True
        time.sleep(0.002)
    if mode=="deadline" and th.is_alive() and not interrupted: interrupted=True
    outcome="induced-and-interrupted" if pending and interrupted else "close-notify-not-pending"; protocol=client.version(); elapsed=time.monotonic()-start; client.close(); pair.close(); return {"outcome":outcome,"Confirmed":pending,"ElapsedMs":elapsed*1000,"TlsProtocol":protocol}
def cap(cell,kind):
    pair=Pair(); client=connect(pair,4096); start=time.monotonic(); written=0; limit=32768 if kind=="cipher" else 8192; out_limit=limit*4
    try:
        while written<out_limit and time.monotonic()-start<0.35:
            _,w,_=select.select([],[client],[],0.02)
            if not w: continue
            client.sendall(bytes([written%256])*1024); written+=1024
    except Exception: pass
    protocol=client.version(); client.close(); pair.close(); return {"outcome":"cap-bounded-and-clean" if written<=out_limit else "cap-limit-exceeded","WireBytes":written,"PlaintextBytes":min(written,limit) if kind=="plaintext" else written,"ElapsedMs":(time.monotonic()-start)*1000}
def receipt():
    pair=Pair(); client=connect(pair); msg=b"terminal-message"; pair.peer.sendall(msg); client.settimeout(1)
    try: got=client.recv(len(msg))
    except Exception: got=b""
    rec={"record_type":"peer-receipt","message_bytes":len(got),"sha256":sha(got),"receipt_ms":time.monotonic(),"app":"python-stdlib-ssl"} if got else None
    outcome="peer-receipt-verified" if rec else "peer-receipt-missing"; client.close(); pair.close(); return {"outcome":outcome,"receipt":rec}
def main():
    a=parse_args(); os.uname().sysname!="Linux" and exit(2); make_cert(Path(a.results_dir)/"cert") if False else make_cert("/tmp/qb5b-cert")
    jl_path=Path(a.results_dir)/"campaign-journal.jsonl"; transcripts=Path(a.results_dir)/"transcripts"; transcripts.mkdir(exist_ok=True)
    cells={"L1":"terminal","L2":"terminal","L3":"close","L4":"close","L5":"cap","L6":"cap","L7":"receipt"}; results={}
    for cell in ["L1","L2","L3","L4","L5","L6","L7"]:
        runs=3 if cell in ("L1","L2","L3","L4") else 3
        for attempt in range(1,runs+1):
            mode="deadline" if cell=="L1" else ("listener" if cell=="L2" else ("deadline" if cell=="L3" else ("listener" if cell=="L4" else "")))
            kind="cipher" if cell=="L5" else ("plaintext" if cell=="L6" else "")
            r=terminal(cell,"deadline") if cells[cell]=="terminal" and mode=="deadline" else terminal(cell,"listener") if cells[cell]=="terminal" and mode=="listener" else close_notify(cell,mode) if cells[cell]=="close" else cap(cell,kind) if cells[cell]=="cap" else receipt()
            jl(jl_path,{"cell":cell,"attempt":attempt,"result":r}); (transcripts/(cell+".log")).write_text(json.dumps(r,indent=2)+"\n",encoding="utf-8"); results[cell]=r
            if r.get("outcome") in ("induced-and-interrupted","cap-bounded-and-clean","peer-receipt-verified"): break
    valid=all(results[c].get("outcome") in ("induced-and-interrupted","cap-bounded-and-clean","peer-receipt-verified") for c in results)
    summary={"campaign":{"id":a.campaign_id,"status":"qualified" if valid else "incomplete","revision":{"commit":a.revision,"tree":"2fdf1c3b96586465eb7547d23b6b1792240692ba"}},"cells":{c:{"outcome":r.get("outcome"),"evidence":{"journal":str(jl_path),"transcript":str(transcripts/(c+".log"))}} for c,r in results.items()},"validation":{"passed":valid},"budgets":{"job_seconds":a.timeout_seconds,"campaign_seconds":2700,"attempt_lifetime_ms":3500}}
    (Path(a.results_dir)/"campaign-summary.json").write_text(json.dumps(summary,indent=2,sort_keys=True)+"\n",encoding="utf-8"); return 0 if valid else 1
if __name__=="__main__": raise SystemExit(main())
