#!/usr/bin/env python3
"""Registered zero-window peer mechanism (python3 stdlib ssl).

Accepts exactly one TLS connection, then parks its receive direction after the
handshake: it never reads application data, so the kernel receive window closes
and the sender's terminal write (or close_notify) stays pending. Journals every
event so the harness record can reference the peer-side observation.

Cross-platform: the FIONREAD sampler is best-effort (POSIX ioctl); on platforms
without fcntl/termios the peer still parks and journals fionread=-1. The parked
mechanism itself never depends on the sampler.
"""
import argparse
import json
import socket
import ssl
import time


def fionread(sock):
    try:
        import fcntl  # POSIX only
        import termios
        import struct
        return struct.unpack("i", fcntl.ioctl(sock.fileno(), termios.FIONREAD, b"\0\0\0\0"))[0]
    except Exception:
        return -1


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--cert", required=True)
    p.add_argument("--key", required=True)
    p.add_argument("--journal", required=True)
    p.add_argument("--rcvbuf", type=int, default=512)
    p.add_argument("--park-ms", type=int, default=5000)
    a = p.parse_args()

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, a.rcvbuf)
    srv.bind(("127.0.0.1", 0))
    srv.listen(1)
    port = srv.getsockname()[1]

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(a.cert, a.key)

    def w(rec):
        rec["ts"] = time.time()
        with open(a.journal, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, sort_keys=True) + "\n")

    w({"kind": "listening", "port": port, "requested_rcvbuf": a.rcvbuf,
       "actual_rcvbuf": srv.getsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF)})
    print(port, flush=True)

    conn, addr = srv.accept()
    conn.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, a.rcvbuf)
    w({"kind": "accepted", "peer": list(addr),
       "actual_rcvbuf": conn.getsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF)})

    try:
        tls = ctx.wrap_socket(conn, server_side=True)
        w({"kind": "tls", "version": tls.version(),
           "cipher": tls.cipher()[0] if tls.cipher() else None})

        # Park: no application reads from here on. Sample FIONREAD for the record only.
        deadline = time.time() + a.park_ms / 1000.0
        started = time.time()
        samples = 0
        while time.time() < deadline:
            if samples % 20 == 0:
                w({"kind": "parked", "fionread": fionread(tls),
                   "elapsed_ms": (time.time() - started) * 1000.0})
            samples += 1
            time.sleep(0.01)

        w({"kind": "park-end", "fionread": fionread(tls)})
        try:
            tls.close()
        except Exception:
            pass
    except Exception as e:
        w({"kind": "error", "error": type(e).__name__ + ":" + str(e)[:200]})
    finally:
        try:
            conn.close()
        except Exception:
            pass
        srv.close()


if __name__ == "__main__":
    main()
