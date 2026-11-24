#!/usr/bin/env python3
"""Independent receipt peer for the L7 cell (python3 stdlib ssl).

Cooperative peer application: handshakes, pipelines the registered request
sequence, then reads the production stream until EOF. The stream is parsed with
the Bolt chunk framing ([2-byte chunk length][payload] ... [0x0000] message
terminator) so the peer identifies the actual terminal message instead of
guessing its position, and records a receipt (framed message bytes + sha256 +
receipt_ms). The runner validates that receipt against the campaign's expected
terminal-message digest and byte count.
"""
import argparse
import hashlib
import json
import socket
import ssl
import time


def read_exactly(sock, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("closed during read")
        buf.extend(chunk)
    return bytes(buf)


def split_messages(buf):
    """Split a Bolt stream into framed messages.

    Returns (start, end) pairs; each span covers the complete framed message
    including its terminating 0x0000 chunk, matching Wire.Frame(payload)
    ([2-byte chunk length][payload][0x0000]).
    """
    spans = []
    i = 0
    n = len(buf)
    start = None
    while i + 2 <= n:
        ln = (buf[i] << 8) | buf[i + 1]
        if ln == 0:
            if start is not None:
                spans.append((start, i + 2))
                start = None
            i += 2
            continue
        if i + 2 + ln > n:
            break
        if start is None:
            start = i
        i += 2 + ln
    return spans


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
    expected_bytes = desc["expected_message_bytes"]
    expected_sha256 = desc["expected_sha256"]

    def w(rec):
        rec["ts"] = time.time()
        with open(receipt_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, sort_keys=True) + "\n")

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    ctx.load_verify_locations(cert_path)
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_REQUIRED

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(timeout_ms / 1000.0)
    sock.connect((host, port))
    tls = ctx.wrap_socket(sock, server_hostname=host)

    w({"kind": "peer-start", "cell": desc.get("cell"), "endpoint": endpoint})
    w({"kind": "tls", "version": tls.version(),
       "cipher": tls.cipher()[0] if tls.cipher() else None})

    handshake = bytes.fromhex(desc["handshake_hex"])
    selected_hex = desc["selected_hex"]
    requests = [bytes.fromhex(h) for h in desc["requests_hex"]]

    tls.sendall(handshake)
    selected = read_exactly(tls, 4)
    if selected.hex() != selected_hex:
        w({"kind": "error", "error": "unexpected handshake selection"})
        return

    for req in requests:
        tls.sendall(req)

    received = bytearray()
    start = time.time()
    while True:
        try:
            chunk = tls.recv(65536)
            if not chunk:
                break
            received.extend(chunk)
        except socket.timeout:
            break
        except ssl.SSLEOFError:
            break
        except Exception:
            break
        if (time.time() - start) * 1000 > timeout_ms:
            break

    receipt_ms = int((time.time() - start) * 1000)
    tls_version = tls.version()
    tls_cipher = tls.cipher()[0] if tls.cipher() else None

    spans = split_messages(bytes(received))
    message_lengths = [e - s for s, e in spans]
    if not spans:
        w({"kind": "error", "error": "no complete Bolt message parsed from the peer stream",
           "received_bytes": len(received)})
        return

    # The terminal message is the last complete framed message production wrote;
    # with the pipelined terminal request sequence that is the terminal failure.
    tstart, tend = spans[-1]
    message = bytes(received[tstart:tend])
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
        "journal": receipt_path,
        "tls_version": tls_version,
        "tls_cipher": tls_cipher,
        "total_received_bytes": len(received),
        "message_count": len(spans),
        "message_lengths": message_lengths,
        "message_span_start": tstart,
        "expected_message_bytes": expected_bytes,
        "expected_sha256": expected_sha256,
    })

    # Drain anything after the terminal message (the peer must have read it all).
    unread = 0
    try:
        while True:
            chunk = tls.recv(65536)
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
        "tls_version": tls_version,
        "tls_cipher": tls_cipher
    })

    tls.close()
    sock.close()


if __name__ == "__main__":
    main()