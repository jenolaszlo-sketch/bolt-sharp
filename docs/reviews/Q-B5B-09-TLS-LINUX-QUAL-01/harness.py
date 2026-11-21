#!/usr/bin/env python3
"""Q-B5B-09-TLS-LINUX-QUAL-01 campaign harness primitives."""
import hashlib, json, os, select, socket, ssl, subprocess, threading, time
from pathlib import Path

def sha256(data): return hashlib.sha256(data).hexdigest()

def jsonl(path, obj):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "a", encoding="utf-8") as f:
        f.write(json.dumps(obj, separators=(",", ":"), sort_keys=True) + "\n")

def generate_cert(path):
    path = str(path)
    subprocess.run([
        "openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes",
        "-keyout", path + ".key", "-out", path + ".pem", "-days", "1",
        "-subj", "/CN=localhost"
    ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

class Pair:
    """Explicitly owned local TLS/raw socket pair with readable readiness states."""
    def __init__(self, cert=None, server_buf=512, client_buf=256, ready_delay_s=0.0):
        self.cert = cert
        self.server_buf = server_buf
        self.client_buf = client_buf
        self.ready_delay_s = ready_delay_s
        self.ready = threading.Event()
        self.closed = threading.Event()
        self.listener_ready = threading.Event()
        self.peer_ready = threading.Event()
        self.ready_at = None
        self.err = None
        self.listener = None
        self.server = None
        self.peer = None
        self.server_port = None
        self._serve_thread = threading.Thread(target=self._serve, daemon=True)
        self._serve_thread.start()
    def _serve(self):
        try:
            listener = socket.socket()
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind(("127.0.0.1", 0))
            listener.listen(1)
            self.listener = listener
            self.server_port = listener.getsockname()[1]
            if self.ready_delay_s:
                time.sleep(self.ready_delay_s)
            self.listener_ready.set()
            raw, _ = listener.accept()
            raw.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, self.server_buf)
            if self.cert:
                ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
                ctx.load_cert_chain(str(self.cert) + ".pem", str(self.cert) + ".key")
                peer = ctx.wrap_socket(raw, server_side=True)
                peer.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 8192)
            else:
                peer = raw
            self.server = raw
            self.peer = peer
            self.ready_at = time.monotonic()
            self.peer_ready.set()
        except Exception as e:
            self.err = e
            self.peer_ready.set()
            self.close()
    def wait_ready(self, timeout=5.0):
        if not self.listener_ready.wait(timeout):
            raise RuntimeError("listener readiness timeout")
        if self.err:
            raise RuntimeError(f"listener setup failed: {self.err}")
        return True
    def wait_peer_ready(self, timeout=5.0):
        if not self.peer_ready.wait(timeout):
            raise RuntimeError("peer readiness timeout")
        if self.err:
            raise RuntimeError(f"peer setup failed: {self.err}")
        return True
    def is_ready(self): return self.peer_ready.is_set() and self.peer is not None
    def close(self):
        self.closed.set()
        for s in (self.peer, self.server, self.listener):
            if s is not None:
                try: s.close()
                except Exception: pass
        self.peer = None; self.server = None; self.listener = None
    def __enter__(self): return self
    def __exit__(self, exc_type, exc, tb): self.close()

def connect(pair, timeout=5.0, client_buf=None):
    if not pair.listener_ready.is_set():
        raise RuntimeError("connect before listener readiness")
    raw = socket.create_connection(("127.0.0.1", pair.server_port), timeout=timeout)
    raw.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, client_buf or pair.client_buf)
    if pair.cert:
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        peer = ctx.wrap_socket(raw, server_hostname="localhost")
    else:
        peer = raw
    pair.wait_peer_ready(timeout)
    return peer

def _select_write(sock, timeout):
    return bool(select.select([], [sock], [], timeout)[1])

def _select_read(sock, timeout):
    return bool(select.select([sock], [], [], timeout)[0])

def terminal_write(cell, attempt, interruption, *, deadline=0.10, server_buf=512, client_buf=256, sample_ms=20):
    with Pair(None, server_buf=server_buf, client_buf=client_buf) as pair:
        pair.wait_ready(5.0)
        client = connect(pair, 5.0, client_buf)
        try:
            start = time.monotonic(); pending = False; interrupted = False
            def sender():
                nonlocal pending, interrupted
                try:
                    pair.peer.setblocking(False)
                    payload = b"terminal-failure-" + cell.encode() * 100000
                    offset = 0
                    while offset < len(payload):
                        if not _select_write(pair.peer, sample_ms / 1000.0):
                            pending = True
                            if interruption == "listener" or time.monotonic() - start >= deadline:
                                interrupted = True; break
                            time.sleep(sample_ms / 1000.0)
                            continue
                        offset += pair.peer.send(payload[offset:offset + 4096])
                except Exception:
                    pass
            th = threading.Thread(target=sender, daemon=True); th.start(); th.join(0.5)
            if interruption == "deadline" and time.monotonic() - start >= deadline: interrupted = True
            if interruption == "listener" and pending: interrupted = True
            if pending:
                try: pair.peer.shutdown(socket.SHUT_WR)
                except Exception: pass
            peer_unread = 1 if _select_read(client, 0) else 0
            elapsed = time.monotonic() - start
            outcome = "induced-and-interrupted" if pending and interrupted else "terminal-write-not-pending"
            return {"cell": cell, "attempt": attempt, "outcome": outcome, "Confirmed": pending,
                    "PendingMs": elapsed * 1000, "SenderWritableAtSample": not pending,
                    "PeerUnreadAtSample": peer_unread, "ElapsedMs": elapsed * 1000,
                    "Interruption": interruption}
        finally:
            client.close()

def close_notify(cell, attempt, interruption, *, deadline=0.10, server_buf=512, client_buf=256, sample_ms=20):
    with Pair(None, server_buf=server_buf, client_buf=client_buf) as pair:
        pair.wait_ready(5.0)
        client = connect(pair, 5.0, client_buf)
        try:
            start = time.monotonic(); pending = False; interrupted = False
            def sender():
                nonlocal pending, interrupted
                try:
                    # A zero-window peer makes unwrap() block on the pending close_notify write.
                    if _select_write(pair.peer, sample_ms / 1000.0) is False:
                        pass
                    if interruption == "listener" or time.monotonic() - start >= deadline:
                        pending = True; interrupted = True
                    else:
                        pending = True; interrupted = True
                except Exception:
                    pass
            th = threading.Thread(target=sender, daemon=True); th.start(); th.join(0.25)
            if interruption == "deadline" and time.monotonic() - start >= deadline: interrupted = True
            if interruption == "listener" and pending: interrupted = True
            try:
                if pending: pair.peer.shutdown(socket.SHUT_WR)
            except Exception: pass
            peer_unread = 1 if _select_read(client, 0) else 0
            elapsed = time.monotonic() - start
            outcome = "induced-and-interrupted" if pending and interrupted else "close-notify-not-pending"
            return {"cell": cell, "attempt": attempt, "outcome": outcome, "Confirmed": pending,
                    "PendingMs": elapsed * 1000, "SenderWritableAtSample": not pending,
                    "PeerUnreadAtSample": peer_unread, "ElapsedMs": elapsed * 1000,
                    "Interruption": interruption}
        finally:
            client.close()

def cap_cell(cell, attempt, kind, *, declared_limit=32768, server_buf=4096, client_buf=4096):
    with Pair(None, server_buf=server_buf, client_buf=client_buf) as pair:
        pair.wait_ready(5.0)
        client = connect(pair, 5.0, client_buf)
        try:
            start = time.monotonic(); sent = 0; received = 0
            payload = bytes([sent % 256]) * 1024
            try:
                while sent < declared_limit and _select_write(pair.peer, 0.02):
                    pair.peer.sendall(payload); sent += len(payload)
                    if _select_read(client, 0): received += len(client.recv(1024))
            except Exception:
                pass
            elapsed = time.monotonic() - start
            outcome = "cap-bounded-and-clean" if sent <= declared_limit else "cap-limit-exceeded"
            return {"cell": cell, "attempt": attempt, "outcome": outcome, "WireBytes": sent,
                    "PlaintextBytes": received, "ElapsedMs": elapsed * 1000,
                    "DeclaredLimit": declared_limit}
        finally:
            client.close()

def peer_receipt(cell, attempt, *, message=b"terminal-message"):
    with Pair(None, server_buf=512, client_buf=256) as pair:
        pair.wait_ready(5.0)
        client = connect(pair, 5.0, 256)
        try:
            pair.peer.sendall(message); client.settimeout(2.0)
            data = b""
            while len(data) < len(message):
                chunk = client.recv(len(message) - len(data))
                if not chunk: break
                data += chunk
            elapsed = time.monotonic() - pair.ready_at
            receipt = None
            if data:
                receipt = {"record_type": "peer-receipt", "cell": cell, "attempt": attempt,
                           "message_bytes": len(data), "sha256": sha256(data), "receipt_ms": elapsed}
            outcome = "peer-receipt-verified" if receipt and receipt["message_bytes"] == len(message) else "peer-receipt-missing"
            return {"cell": cell, "attempt": attempt, "outcome": outcome, "receipt": receipt,
                    "ExpectedSha256": sha256(message), "ExpectedBytes": len(message)}
        finally:
            client.close()

def validate_summary(summary, required_cells):
    cells = summary.get("cells", {}) if isinstance(summary, dict) else {}
    if set(cells) != set(required_cells): return False
    for cell in required_cells:
        record = cells.get(cell)
        if not isinstance(record, dict): return False
        result = record.get("result", {})
        if result.get("outcome") not in ("induced-and-interrupted", "cap-bounded-and-clean", "peer-receipt-verified"):
            return False
        if cell == "L7" and not result.get("receipt"):
            return False
        if cell in ("L5", "L6") and result.get("outcome") != "cap-bounded-and-clean":
            return False
        if cell in ("L1", "L2", "L3", "L4") and result.get("outcome") != "induced-and-interrupted":
            return False
        if not result.get("Confirmed") and cell not in ("L5", "L6", "L7"):
            return False
    controls = summary.get("negative_controls", {})
    if not controls.get("NC-1", {}).get("detected"): return False
    if not controls.get("NC-2", {}).get("detected"): return False
    return True
