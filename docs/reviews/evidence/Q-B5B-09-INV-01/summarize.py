import collections
import json
import sys
from pathlib import Path

for file in sys.argv[1:]:
    data = json.loads(Path(file).read_bytes())
    cases = collections.defaultdict(list)
    for group in data["groups"]:
        assert group["accepted"] == group["finished"] == len(group["connections"])
        assert group["active_after"] == 0
        cases[group["mode"], group["case"]].extend(group["connections"])
    print(file, data["environment"], len(data["groups"]), sum(map(len,cases.values())))
    for (mode, case), rows in cases.items():
        ends = dict(collections.Counter(row["client_end"] for row in rows))
        exits = dict(collections.Counter(row["server_exit"] for row in rows))
        print(f"{mode:16} {case:18} {sum(r['exact_once_observed'] for r in rows):2}/{len(rows):2} {str(ends):42} {str(exits):34} "
              f"{max(r['terminal_to_socket_close_ms'] for r in rows):8.3f}ms {max(r['drained'] for r in rows):4}B")
