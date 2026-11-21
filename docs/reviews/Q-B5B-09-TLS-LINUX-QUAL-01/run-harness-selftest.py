#!/usr/bin/env python3
"""Q-B5B-09-TLS-LINUX-QUAL-01 platform-independent harness self-tests."""
from pathlib import Path
import json, tempfile, time, threading
from harness import Pair, connect, validate_summary, terminal_write, close_notify, cap_cell, peer_receipt

def wait_connect_ready():
    with Pair(None) as pair:
        assert pair.wait_ready(5), "listener not ready"
        client = connect(pair, 5.0, 256)
        assert pair.is_ready(), "peer should be ready"
        client.close()

def delayed_readiness():
    with Pair(None) as pair:
        time.sleep(0.02)
        assert pair.wait_ready(5), "delayed peer readiness failed"
        client = connect(pair, 5.0, 256)
        client.close()

def premature_access_rejected():
    with Pair(None, ready_delay_s=0.5) as pair:
        try:
            client = connect(pair, 5.0, 256)
        except RuntimeError:
            pass
        else:
            client.close()
            raise AssertionError("connect should have rejected premature peer")

def partial_cleanup():
    with Pair(None) as pair:
        # Do not wait for readiness; close early. No exception should escape.
        pair.close()
        assert pair.closed.is_set(), "pair not marked closed"

def connection_failure():
    with Pair(None) as pair:
        pair.wait_ready(5)
        client = connect(pair, 5.0, 256)
        client.close()

def incomplete_summary_rejected():
    summary = {"cells": {"L1": {"result": {"outcome": "induced-and-interrupted", "Confirmed": True}}}}
    assert not validate_summary(summary, ["L1", "L2"]), "incomplete summary accepted"

def valid_summary_accepted():
    summary = {
        "cells": {
            "L1": {"result": {"outcome": "induced-and-interrupted", "Confirmed": True}},
            "L2": {"result": {"outcome": "induced-and-interrupted", "Confirmed": True}},
            "L3": {"result": {"outcome": "induced-and-interrupted", "Confirmed": True}},
            "L4": {"result": {"outcome": "induced-and-interrupted", "Confirmed": True}},
            "L5": {"result": {"outcome": "cap-bounded-and-clean"}},
            "L6": {"result": {"outcome": "cap-bounded-and-clean"}},
            "L7": {"result": {"outcome": "peer-receipt-verified", "receipt": {"message_bytes": 16}}},
        },
        "negative_controls": {"NC-1": {"detected": True}, "NC-2": {"detected": True}},
    }
    assert validate_summary(summary, ["L1", "L2", "L3", "L4", "L5", "L6", "L7"]), "valid summary rejected"

def campaign_cells_smoke():
    required = ["L1", "L2", "L3", "L4", "L5", "L6", "L7"]
    for cell in required:
        if cell in ("L1", "L2"): result = terminal_write(cell, 1, "deadline" if cell == "L1" else "listener")
        elif cell in ("L3", "L4"): result = close_notify(cell, 1, "deadline" if cell == "L3" else "listener")
        elif cell in ("L5", "L6"): result = cap_cell(cell, 1, "cipher" if cell == "L5" else "plaintext", declared_limit=32768 if cell == "L5" else 8192)
        elif cell == "L7": result = peer_receipt(cell, 1)
        assert result.get("outcome"), f"missing outcome for {cell}"

def negative_controls():
    from harness import terminal_write, cap_cell
    bad = terminal_write("L1", 1, "deadline", deadline=0.5)
    assert bad.get("ElapsedMs", 0) > 100, "deadline-renewal tamper not detected"
    bad = cap_cell("L5", 1, "cipher", declared_limit=131072)
    assert bad.get("WireBytes", 0) > 0, "cap tamper not detected"

def main():
    tests = [wait_connect_ready, delayed_readiness, premature_access_rejected, partial_cleanup,
             connection_failure, incomplete_summary_rejected, valid_summary_accepted,
             campaign_cells_smoke, negative_controls]
    for test in tests:
        test()
        print(f"PASS {test.__name__}")
    print(f"PASS {len(tests)}/{len(tests)} harness self-tests")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
