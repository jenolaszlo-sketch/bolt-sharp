#!/usr/bin/env python3
import argparse
import json
import select
import socket
import ssl
import sys
import time

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--descriptor", required=True)
    a = p.parse_args()

    with open(a.descriptor, "r") as f:
        desc = json.load(f)

    upstream_host, upstream_port_str = desc["upstream"].split(":")
    upstream_port = int(upstream_port_str)
    listen_port = desc.get("listen_port", 0)
    gate_ms = desc.get("gate_ms", 50)
    journal_path = desc["journal"]
    cert_path = desc["cert_pem"]
    cert_sha256 = desc.get("cert_sha256")

    def w(rec):
        rec["ts"] = time.time()
        with open(journal_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, sort_keys=True) + "\n")

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(cert_path, cert_path)

    upstream_ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    upstream_ctx.load_verify_locations(cert_path)
    upstream_ctx.check_hostname = False
    upstream_ctx.verify_mode = ssl.CERT_REQUIRED

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind(("127.0.0.1", listen_port))
    listener.listen(1)
    actual_port = listener.getsockname()[1]

    w({"kind": "proxy-listening", "port": actual_port, "upstream": desc["upstream"]})
    print(actual_port, flush=True)

    client_sock, client_addr = listener.accept()
    w({"kind": "client-accepted", "peer": list(client_addr)})

    upstream_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    upstream_sock.connect((upstream_host, upstream_port))

    try:
        client_tls = ctx.wrap_socket(client_sock, server_side=True)
        upstream_tls = upstream_ctx.wrap_socket(upstream_sock, server_hostname="localhost")

        w({"kind": "tls-upstream", "version": upstream_tls.version(), "cipher": upstream_tls.cipher()[0] if upstream_tls.cipher() else None})
        w({"kind": "tls-client", "version": client_tls.version(), "cipher": client_tls.cipher()[0] if client_tls.cipher() else None})

        gate_until = time.time() + gate_ms / 1000.0
        close_notify_seen = False

        while True:
            rlist, _, _ = select.select([client_tls, upstream_tls], [], [], 0.01)
            for sock in rlist:
                if sock is client_tls:
                    data = client_tls.recv(8192)
                    if not data:
                        w({"kind": "client-eof"})
                        upstream_tls.close()
                        return
                    if b"\x15\x03" in data[:5] and not close_notify_seen:
                        close_notify_seen = True
                        w({"kind": "close-notify-observed", "direction": "client->upstream", "gate_ms": gate_ms})
                        if time.time() < gate_until:
                            time.sleep(gate_ms / 1000.0)
                    upstream_tls.sendall(data)
                elif sock is upstream_tls:
                    data = upstream_tls.recv(8192)
                    if not data:
                        w({"kind": "upstream-eof"})
                        client_tls.close()
                        return
                    if b"\x15\x03" in data[:5] and not close_notify_seen:
                        close_notify_seen = True
                        w({"kind": "close-notify-observed", "direction": "upstream->client", "gate_ms": gate_ms})
                        if time.time() < gate_until:
                            time.sleep(gate_ms / 1000.0)
                    client_tls.sendall(data)
    except Exception as e:
        w({"kind": "error", "error": type(e).__name__ + ":" + str(e)[:200]})
    finally:
        try:
            client_sock.close()
        except Exception:
            pass
        try:
            upstream_sock.close()
        except Exception:
            pass
        listener.close()

if __name__ == "__main__":
    main()