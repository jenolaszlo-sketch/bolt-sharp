"""Compare TCP close strategies with two synthetic peers on IPv4 loopback."""

import asyncio
import errno
import json
import platform
import socket
import struct
import sys
import time
from contextlib import ExitStack, closing
from pathlib import Path

RESPONSE = b"synthetic terminal response\n"
MODES = ("immediate", "send-then-drain", "drain-then-close", "send-only")
CASES = (
    "empty", "small", "large", "partial-opaque", "malformed-opaque",
    "continuous-input", "stalled", "never-fin", "early-close", "peer-reset",
    "no-read", "cancel-connection", "stop-listener", "concurrent",
)
TAILS = {"small": b"x" * 32, "large": b"x" * 24576,
         "partial-opaque": b"[ab", "malformed-opaque": b"]][[!",
         "concurrent": b"x" * 32}
REPEATS = 5
CONCURRENT = 6
DRAIN_SECONDS = 0.075
BYTE_LIMIT = 8192
BUFFER_BYTES = 2048
PEER_WAIT = 0.150
TIMEOUT = 1.0


def error_class(error):
    if isinstance(error, ConnectionResetError) or getattr(error, "errno", None) in (
        errno.ECONNRESET, 10054
    ):
        return "reset"
    return f"{type(error).__name__}:{getattr(error, 'errno', None)}"


def endpoint():
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setblocking(False)
    return sock


class Accounting:
    def __init__(self):
        self.active = self.peak = self.accepted = self.finished = 0

    def accept(self):
        self.active += 1
        self.accepted += 1
        self.peak = max(self.peak, self.active)

    def finish(self):
        self.active -= 1
        self.finished += 1


async def server_side(sock, mode, ready, write_gate, draining, counts):
    loop = asyncio.get_running_loop()
    result = {"drained": 0, "response_writes": 0, "terminal": False, "scratch_bytes": 0}
    start = time.monotonic()
    counts.accept()
    try:
        if await loop.sock_recv(sock, 1) != b"T":
            raise RuntimeError("Synthetic trigger missing")
        result["terminal"] = True
        start = time.monotonic()
        ready.set()
        await write_gate.wait()
        await loop.sock_sendall(sock, RESPONSE)
        result["response_writes"] = 1
        if mode in ("send-then-drain", "send-only"):
            sock.shutdown(socket.SHUT_WR)
        if mode in ("send-then-drain", "drain-then-close"):
            buffer = bytearray(BUFFER_BYTES)
            result["scratch_bytes"] = len(buffer)
            draining.set()
            async with asyncio.timeout(DRAIN_SECONDS):
                while result["drained"] < BYTE_LIMIT:
                    request = min(len(buffer), BYTE_LIMIT - result["drained"])
                    size = await loop.sock_recv_into(sock, memoryview(buffer)[:request])
                    if size == 0:
                        result["server_exit"] = "peer-eof"
                        break
                    result["drained"] += size
                else:
                    result["server_exit"] = "byte-cap"
        else:
            result["server_exit"] = "immediate"
    except TimeoutError:
        result["server_exit"] = "deadline"
    except asyncio.CancelledError:
        result["server_exit"] = "cancelled"
    except OSError as error:
        result["server_exit"] = error_class(error)
    finally:
        sock.close()
        result["terminal_to_socket_close_ms"] = round((time.monotonic() - start) * 1000, 3)
        counts.finish()
    return result


async def client_side(sock, case):
    loop = asyncio.get_running_loop()
    observed = bytearray()
    if case == "no-read":
        await asyncio.sleep(PEER_WAIT)
        return "not-read", observed
    sent_fin = case in ("empty", "small", "large", "partial-opaque", "malformed-opaque", "concurrent")
    if case in ("continuous-input", "stalled"):
        try:
            for _ in range(16 if case == "continuous-input" else 1):
                await asyncio.sleep(0.006 if case == "continuous-input" else 0.012)
                await loop.sock_sendall(sock, b"x" * (1024 if case == "continuous-input" else 1))
        except OSError:
            pass
    await asyncio.sleep(0.015)
    try:
        while len(observed) <= len(RESPONSE):
            data = await loop.sock_recv(sock, len(RESPONSE) + 1 - len(observed))
            if not data:
                if not sent_fin and case != "never-fin":
                    try:
                        sock.shutdown(socket.SHUT_WR)
                    except OSError:
                        pass
                return "eof", observed
            observed.extend(data)
    except OSError as error:
        return error_class(error), observed
    return "extra", observed


async def trial(client, server, mode, case, counts, listener_cancel, listener_started):
    loop = asyncio.get_running_loop()
    ready, write_gate, draining = (asyncio.Event() for _ in range(3))
    with closing(client), closing(server):
        job = asyncio.create_task(server_side(server, mode, ready, write_gate, draining, counts))
        try:
            await loop.sock_sendall(client, b"T" + TAILS.get(case, b""))
            if case in ("empty", "small", "large", "partial-opaque", "malformed-opaque", "concurrent"):
                client.shutdown(socket.SHUT_WR)
            await ready.wait()
            if case in ("early-close", "peer-reset"):
                if case == "peer-reset":
                    linger = struct.pack("HH" if sys.platform == "win32" else "ii", 1, 0)
                    client.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, linger)
                client.close()
            write_gate.set()
            if mode in ("send-then-drain", "drain-then-close"):
                if case == "cancel-connection":
                    await draining.wait()
                    job.cancel()
                if case == "stop-listener":
                    listener_started.set()
                    await listener_cancel.wait()
                    job.cancel()
            if case in ("early-close", "peer-reset"):
                event, received = "peer-closed-first", b""
            else:
                event, received = await client_side(client, case)
            result = await job
        finally:
            if not job.done():
                job.cancel()
                await job
    return {"mode": mode, "case": case, "client_end": event, "observed_hex": received.hex(),
            "exact_once_observed": received == RESPONSE, **result}


async def run_group(mode, case):
    count = CONCURRENT if case == "concurrent" else 1
    counts = Accounting()
    loop = asyncio.get_running_loop()
    listener_cancel, listener_started = asyncio.Event(), asyncio.Event()
    with closing(endpoint()) as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen(count)
        async with asyncio.timeout(TIMEOUT):
            with ExitStack() as sockets:
                pairs = []
                for _ in range(count):
                    client = sockets.enter_context(closing(endpoint()))
                    await loop.sock_connect(client, listener.getsockname())
                    server, _ = await loop.sock_accept(listener)
                    sockets.enter_context(closing(server))
                    pairs.append((client, server))
                async with asyncio.TaskGroup() as tasks:
                    jobs = [tasks.create_task(
                        trial(client, server, mode, case, counts, listener_cancel, listener_started)
                    ) for client, server in pairs]
                    if case == "stop-listener" and mode in ("send-then-drain", "drain-then-close"):
                        await listener_started.wait()
                        listener.close()
                        listener_cancel.set()
                rows = [job.result() for job in jobs]
    assert counts.accepted == counts.finished == count and counts.active == 0
    for row in rows:
        assert row["terminal"] and row["response_writes"] <= 1 and row["drained"] <= BYTE_LIMIT
        assert row["client_end"] != "extra"
    return rows, counts


async def run():
    meta = {"platform": platform.platform(), "python": platform.python_version(),
            "response_hex": RESPONSE.hex(), "duration_budget_ms": int(DRAIN_SECONDS * 1000),
            "byte_budget": BYTE_LIMIT, "scratch_bytes": BUFFER_BYTES,
            "repeat": REPEATS, "concurrent_group_size": CONCURRENT}
    records = []
    for mode in MODES:
        for case in CASES:
            for iteration in range(1, REPEATS + 1):
                rows, counts = await run_group(mode, case)
                records.append({"mode": mode, "case": case, "iteration": iteration,
                                "accepted": counts.accepted, "finished": counts.finished,
                                "active_after": counts.active, "peak_active": counts.peak, "connections": rows})
    return {"environment": meta, "groups": records}


if __name__ == "__main__":
    output = json.dumps(asyncio.run(asyncio.wait_for(run(), 45)), separators=(",", ":"))
    if len(sys.argv) == 2:
        Path(sys.argv[1]).write_bytes(output.encode("ascii") + b"\n")
    else:
        print(output)
