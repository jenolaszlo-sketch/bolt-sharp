"""Independent Q2 structural assertions and byte comparison; no message bodies are stored."""
import hashlib, json, pathlib, struct, xml.etree.ElementTree as ET
import importlib.util

ROOT = pathlib.Path.cwd()
E = ROOT / 'docs/reviews/evidence/B0-A-TRACE-Q2'
spec = importlib.util.spec_from_file_location('runner', ROOT / 'docs/reviews/probes/B0-A-TRACE-Q2/Run-Qualification.py')
runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(runner)
summary = {'handshake_files': [], 'modes': {}, 'trx': [], 'driver_package': {}}

for mode in ['manifest', 'legacy']:
    for suffix in ['client-handshake', 'server-response']:
        name = f'driver-{mode}-{suffix}.hex'
        fresh = (E / 'authentic-reproduction' / name).read_bytes()
        original = runner.git('cat-file', 'blob', runner.HEAD+':docs/reviews/evidence/B0-A-TRACE-01/'+name)
        assert fresh == original
        summary['handshake_files'].append({'file': name, 'file_bytes_identical': True,
            'sha256': runner.digest(fresh), 'wire_length': len(bytes.fromhex(fresh.decode()))})
    client = bytes.fromhex((E / 'authentic-reproduction' / f'driver-{mode}-client-handshake.hex').read_text())
    server = bytes.fromhex((E / 'authentic-reproduction' / f'driver-{mode}-server-response.hex').read_text())
    assert client[:4] == bytes.fromhex('6060B017')
    assert [client[i:i+4].hex() for i in range(4,20,4)] == ['000001ff','00080805','00020404','00000003']
    if mode == 'manifest':
        assert len(client) == 25 and client[20:] == bytes.fromhex('0000080500')
        assert server == bytes.fromhex('000001ff010000080500')
        # Single-byte VarInts: one range, zero advertised/selected capabilities.
        assert server[4] == 1 and server[5:9] == bytes.fromhex('00000805') and server[9] == client[24] == 0
    else:
        assert len(client) == 20 and server == bytes.fromhex('00000805')
    d = json.loads((E / 'authentic-reproduction' / f'driver-{mode}-trace.json').read_bytes())
    listener, post, driver = d['listener'], d['T04_first_post_handshake'], d['driver_observation']
    assert listener['outcome'] == 'ACCEPTED' and listener['selected_version'] == '5.8'
    assert listener['negotiated_capabilities'] == '0x0'
    assert post['status'] == 'MESSAGE_READ' and post['framing_valid']
    assert post['structure_marker_hex'] == '0xB1' and post['message_signature_hex'] == '0x01'
    assert post['message_body_length'] == 155
    second = post['additional_messages'][0]
    assert second['status'] == 'MESSAGE_READ' and second['message_body_length'] == 15
    assert second['structure_marker_hex'] == '0xB1' and second['message_signature_hex'] == '0x6A'
    assert post['bytes_read_after_handshake'] == (155+4)+(15+4) == 178
    assert not post['raw_post_handshake_body_persisted']
    assert driver['exception_type'] == 'Neo4j.Driver.ServiceUnavailableException'
    assert not driver['completed_successfully'] and not driver['timed_out']
    summary['modes'][mode] = {'selected_version': '5.8', 'capability_subset': '0x0',
        'first_message': {'marker': '0xB1', 'signature': '0x01', 'body_bytes': 155},
        'second_message': {'marker': '0xB1', 'signature': '0x6A', 'body_bytes': 15},
        'bytes_after_handshake': 178, 'failure': driver['exception_type'],
        'pipelining_basis': 'second framed message observed before any post-handshake server write; listener source sends no HELLO/LOGON response',
        'raw_post_handshake_body_persisted': False}
assets = json.loads((ROOT / 'tests/BoltSharp.DriverIntegrationTests/obj/project.assets.json').read_bytes())
assert 'Neo4j.Driver/6.4.0' in assets['libraries']
for folder in assets['packageFolders']:
    dll = pathlib.Path(folder) / 'neo4j.driver/6.4.0/lib/net8.0/Neo4j.Driver.dll'
    if dll.exists():
        summary['driver_package'] = {'resolved': 'Neo4j.Driver/6.4.0', 'dll': str(dll), 'sha256': runner.digest(dll.read_bytes())}; break
assert summary['driver_package']
for trx in sorted((E / 'trx').glob('*.trx')): summary['trx'].append(runner.parse_trx(trx))
(E / 'independent-results-audit.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'handshake_files': summary['handshake_files'], 'modes': summary['modes'],
    'trx': [{'file': t['path'], 'counts': t['counts']} for t in summary['trx']]}, indent=2))
