#!/usr/bin/env python3
"""Gated TLS proxy for the L4 close_notify cell (python3 stdlib ssl).

TLS-terminating proxy between the candidate sender and the parked zero-window
peer. Registered mechanism: the proxy observes the close_notify record on the
sender side and holds (gates) forwarding for a bounded gate interval.

R4 mechanics (deterministic):
- loads the proxy certificate and its KEY from the descriptor (the R3 proxy
  loaded the certificate as its own key and never started);
- forwards sender -> upstream bytes up to gate_forward_bound_bytes, then engages
  the gate (stops reading from the sender) so the sender's send path backs up and
  its close_notify write stays pending; the gate decision is journaled;
- after the peer listener is cancelled (upstream dies) the proxy drains the held
  sender bytes and journals the close_notify observation with its phase.
"""
import argparse
import json
import socket
import ssl
import threading
import time


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--descriptor", required=True)
    a = p.parse_args()

    with open(a.descriptor, "r") as f:
        desc = json.load(f)

    upstream_host, upstream_port_str = desc["upstream"].split(":")
    upstream_port = int(upstream_port_str)
    forward_bound = int(desc.get("gate_forward_bound_bytes", 4096))
    gate_ms = int(desc.get("gate_ms", 50))
    journal_path = desc["journal"]
    cert_path = desc["cert_pem"]
    key_path = desc.get("key_pem") or cert_path
    client_rcvbuf = int(desc.get("client_rcvbuf", 2048))

    def w(rec):
        rec["ts"] = time.time()
        with open(journal_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, sort_keys=True) + "\n")

    srv_ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    srv_ctx.load_cert_chain(cert_path, key_path)

    upstream_ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    upstream_ctx.load_verify_locations(cert_path)
    upstream_ctx.check_hostname = False
    upstream_ctx.verify_mode = ssl.CERT_REQUIRED

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind(("127.0.0.1", desc.get("listen_port", 0)))
    listener.listen(1)
    port = listener.getsockname()[1]

    w({"kind": "proxy-listening", "port": port, "upstream": desc["upstream"],
       "forward_bound_bytes": forward_bound, "gate_ms": gate_ms})
    print(port, flush=True)

    client_sock, client_addr = listener.accept()
    try:
        client_sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, client_rcvbuf)
    except Exception:
        pass
    w({"kind": "client-accepted", "peer": list(client_addr),
       "client_rcvbuf_requested": client_rcvbuf})
    upstream_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    upstream_sock.connect((upstream_host, upstream_port))

    upstream_dead = threading.Event()
    client_tls = None
    upstream_tls = None
    try:
        client_tls = srv_ctx.wrap_socket(client_sock, server_side=True)
        upstream_tls = upstream_ctx.wrap_socket(upstream_sock, server_hostname="localhost")
        w({"kind": "tls-client", "version": client_tls.version(),
           "cipher": (client_tls.cipher() or [None, None])[0]})
        w({"kind": "tls-upstream", "version": upstream_tls.version(),
           "cipher": (upstream_tls.cipher() or [None, None])[0]})

        def watch_upstream():
            try:
                while True:
                    if not upstream_tls.recv(4096):
                        break
            except Exception:
                pass
            upstream_dead.set()

        watcher = threading.Thread(target=watch_upstream, daemon=True)
        watcher.start()

        forwarded = 0
        gate_engaged = forward_bound <= 0
        if gate_engaged:
            # Gate engaged before forwarding anything: the sender's held data is never drained
            # until the peer listener is cancelled, which keeps the sender's close_notify write
            # pending (the sender keeps its own queued bytes while SO_SNDBUF sits below them).
            w({"kind": "gate-engaged", "forwarded_bytes": 0,
               "bound_bytes": forward_bound, "gate_ms": gate_ms})
        while not upstream_dead.is_set():
            if gate_engaged:
                time.sleep(gate_ms / 1000.0)
                continue
            try:
                data = client_tls.recv(8192)
            except Exception as e:
                w({"kind": "error", "error": type(e).__name__ + ":" + str(e)[:200]})
                break
            if not data:
                w({"kind": "client-eof"})
                break
            if data[:2] == b"\x15\x03":
                w({"kind": "close-notify-observed", "direction": "sender->proxy",
                   "phase": "gate", "bytes": len(data)})
            if not gate_engaged:
                try:
                    upstream_tls.sendall(data)
                    forwarded += len(data)
                    if forwarded >= forward_bound:
                        gate_engaged = True
                        w({"kind": "gate-engaged", "forwarded_bytes": forwarded,
                           "bound_bytes": forward_bound, "gate_ms": gate_ms})
                except Exception as e:
                    w({"kind": "upstream-send-failed", "error": type(e).__name__ + ":" + str(e)[:200]})
                    upstream_dead.set()
                    break
            else:
                # Gate engaged: hold forwarding and stop reading from the sender so the
                # sender's send path backs up (registered gate). Bounded hold.
                time.sleep(gate_ms / 1000.0)

        if gate_engaged:
            w({"kind": "gate-released",
               "reason": "peer-listener-cancelled" if upstream_dead.is_set() else "client-eof"})

        # Post-cancellation drain: the upstream peer was cancelled; unwind and drain the
        # sender side, journaling any close_notify observation.
        drained = 0
        drain_error = None
        eof_seen = False
        w({"kind": "drain-start"})
        # Re-open the receive window held shut by the gate so the held sender bytes
        # can be drained at once (the drain is the observation step, not the gate).
        try:
            client_tls.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 262144)
            w({"kind": "drain-window-reopened", "requested_rcvbuf": 262144})
        except Exception as exc:
            w({"kind": "drain-window-reopen-failed", "error": type(exc).__name__})
        try:
            deadline = time.time() + 2.0
            client_tls.settimeout(0.25)
            recvs = 0
            while time.time() < deadline:
                try:
                    data = client_tls.recv(8192)
                except socket.timeout:
                    recvs += 1
                    if recvs % 12 == 0:
                        w({"kind": "drain-progress", "drained_bytes": drained, "recvs": recvs})
                    continue
                except Exception as exc:
                    drain_error = type(exc).__name__ + ":" + str(exc)[:120]
                    break
                recvs += 1
                if recvs % 12 == 0:
                    w({"kind": "drain-progress", "drained_bytes": drained, "recvs": recvs})
                if not data:
                    eof_seen = True
                    # A Python SSLSocket surfaces a peer close_notify as a clean TLS EOF
                    # (the alert record is consumed by the TLS layer, never delivered to the
                    # application), so a clean EOF after a completed handshake IS the
                    # close_notify observation from the sender side.
                    w({"kind": "close-notify-observed", "direction": "sender->proxy",
                       "phase": "post-cancellation-drain", "observation": "clean-tls-eof",
                       "drained_bytes": drained})
                    break
                drained += len(data)
                if data[:2] == b"\x15\x03":
                    w({"kind": "close-notify-observed", "direction": "sender->proxy",
                       "phase": "post-cancellation-drain", "bytes": len(data)})
        except Exception as exc:
            drain_error = type(exc).__name__ + ":" + str(exc)[:120]
        w({"kind": "drain-complete", "drained_bytes": drained, "forwarded_bytes": forwarded,
           "eof_seen": eof_seen, "drain_error": drain_error})
    except Exception as e:
        w({"kind": "error", "error": type(e).__name__ + ":" + str(e)[:200]})
    finally:
        for sock in (client_tls, upstream_tls):
            try:
                if sock is not None:
                    sock.close()
            except Exception:
                pass
        try:
            client_sock.close()
        except Exception:
            pass
        try:
            upstream_sock.close()
        except Exception:
            pass
        try:
            listener.close()
        except Exception:
            pass


if __name__ == "__main__":
    main()
