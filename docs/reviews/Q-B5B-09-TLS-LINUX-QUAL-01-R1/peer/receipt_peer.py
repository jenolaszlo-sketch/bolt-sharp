#!/usr/bin/env python3
import argparse
import hashlib
import json
import socket
import ssl
import sys
import time

def read_exactly(sock, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("closed during read")
        buf.extend(chunk)
    return bytes(buf)

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--descriptor", required=True)
    a = p.parse_args()

    with open(a.descriptor, "r") as f:
        desc = json.load(f)

    endpoint = desc["endpoint"]
    host, port_str = endpoint.split(":")
    port = int(port_str)
    cert_path = desc["cert_pem"]
    receipt_path = desc["receipt_journal"]
    timeout_ms = desc.get("timeout_ms", 8000)

    def w(rec):
        rec["ts"] = time.time()
        with open(receipt_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, sort_keys=True) + "\n")

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    ctx.load_verify_locations(cert_path)
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_REQUIRED

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.connect((host, port))
    tls = ctx.wrap_socket(sock, server_hostname=host)

    w({"kind": "peer-start", "cell": desc.get("cell"), "endpoint": endpoint})
    w({"kind": "tls", "version": tls.version(), "cipher": tls.cipher()[0] if tls.cipher() else None})

    handshake = bytes.fromhex(desc["handshake_hex"])
    selected_hex = desc["selected_hex"]
    requests = [bytes.fromhex(h) for h in desc["requests_hex"]]
    expected_bytes = desc["expected_message_bytes"]
    expected_sha256 = desc["expected_sha256"]

    tls.sendall(handshake)
    selected = read_exactly(tls, 4)
    if selected.hex() != selected_hex:
        w({"kind": "error", "error": "unexpected handshake selection"})
        return

    for req in requests:
        tls.sendall(req)

    message = b""
    start = time.time()
    while True:
        try:
            chunk = tls.recv(8192)
            if not chunk:
                break
            message += chunk
            if len(message) >= expected_bytes:
                break
        except ssl.SSLEOFError:
            break
        except Exception:
            break
        if (time.time() - start) * 1000 > timeout_ms:
            break

    receipt_ms = int((time.time() - start) * 1000)

    message_hex = message.hex()
    sha256 = hashlib.sha256(message).hexdigest()

    w({
        "record_type": "peer-receipt",
        "cell": desc.get("cell"),
        "attempt": desc.get("attempt"),
        "message_bytes": len(message),
        "message_hex": message_hex,
        "sha256": sha256,
        "receipt_ms": receipt_ms,
        "app": desc.get("app", "python3-stdlib-ssl-peer"),
        "journal": receipt_path
    })

    unread = 0
    try:
        while True:
            chunk = tls.recv(8192)
            if not chunk:
                break
            unread += len(chunk)
    except Exception:
        pass

    w({
        "record_type": "peer-eof",
        "cell": desc.get("cell"),
        "attempt": desc.get("attempt"),
        "unread_at_end": unread,
        "tls_version": tls.version(),
        "tls_cipher": tls.cipher()[0] if tls.cipher() else None
    })

    tls.close()
    sock.close()

if __name__ == "__main__":
    main()