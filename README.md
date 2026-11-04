# BoltSharp

**An independent .NET implementation of the Bolt wire protocol.**

BoltSharp develops reusable building blocks for communicating using Bolt, with a focus on enabling independently developed services and databases to expose Bolt-compatible server endpoints.

> **Independent project:** BoltSharp is not affiliated with, sponsored by, endorsed by, or maintained by Neo4j. Compatibility is an engineering objective, not a claim of official certification or complete Neo4j feature support.

## Status and scope

**Under active development.** The repository contains work on PackStream serialization, graph value representations, and protocol components. Full Bolt server functionality and interoperability with any particular driver or Neo4j Browser version should **not** be assumed from the project's name or roadmap.

The initial research selected **Bolt 5.8 / PackStream 1.0 provisionally**. This is a target, **not verified end-to-end compatibility**. Refer to [the protocol compatibility target](docs/compatibility-target.md) for the version analysis and unverified assumptions.

The intended scope includes:

- PackStream serialization and deserialization.
- Bolt wire framing and protocol message processing.
- Protocol negotiation and session handling.
- Server-side interoperability with existing Bolt clients.
- Reproducible, version-specific conformance and integration testing.

BoltSharp is **not** a graph database, query execution engine, or Cypher implementation. A service built on BoltSharp remains responsible for storage, query semantics, access control, and application behavior.

## Build and test

Use a compatible .NET SDK to build and test the solution:

```sh
dotnet build BoltSharp.slnx
dotnet test BoltSharp.slnx
```

Passing local tests does not, by itself, establish compatibility with third-party clients. Any interoperability claim should state the client version, negotiated protocol version, transport settings, exercised behavior, and test evidence.

## License and third-party rights

**BoltSharp's original code is licensed under the [Apache License, Version 2.0](LICENSE).** Subject to its conditions, Apache 2.0 permits modification, redistribution, and commercial use of covered code.

The **Bolt protocol documentation published by Neo4j is a different work**. The [Neo4j Bolt documentation repository](https://github.com/neo4j/docs-bolt) identifies its documentation license as **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 (CC BY-NC-SA 4.0)**. That documentation license does not automatically become the license of independently written protocol implementation code. Conversely, BoltSharp's Apache license does not grant rights to copy Neo4j's documentation or code, or waive possible third-party patent, trademark, or other restrictions.

For the detailed licensing boundaries, original-implementation guidelines, trademark notices, and commercial-use qualifications, read **[Legal, Licensing, and Compatibility Notes](docs/legal-and-compatibility.md)**.

## Compatibility and trademarks

References to **Neo4j**, **Cypher**, or related products are for technical identification and interoperability discussion only. Neo4j and Cypher are trademarks or registered trademarks of Neo4j, Inc. or its subsidiaries.

A Bolt-compatible endpoint does not necessarily provide Neo4j database semantics or Cypher support. Features and driver versions are supported only to the extent established by reproducible tests.

## Project references

- [Bolt protocol documentation](https://neo4j.com/docs/bolt/current/)
- [Protocol compatibility research](docs/compatibility-target.md)
- [Legal and compatibility notes](docs/legal-and-compatibility.md)
- [Apache License 2.0](LICENSE)

## Contributions

Contributions, reproducible bug reports, and interoperability probes are welcome. Prefer original implementation, version-bounded claims, recorded evidence, and clear third-party license attribution.
