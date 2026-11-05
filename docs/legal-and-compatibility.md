# Legal, licensing, and compatibility notes

This document explains the licensing boundaries and interoperability claims intended for BoltSharp. It is project guidance, not legal advice or a guarantee of freedom from third-party rights.

## 1. BoltSharp's license

BoltSharp is distributed under the [Apache License, Version 2.0](../LICENSE). This license governs the original material released by the BoltSharp contributors under that license, subject to its terms.

Apache 2.0 permits use, modification, distribution, and commercial use of covered code, provided its conditions are satisfied. In particular, recipients must preserve required license and attribution notices. The Apache 2.0 patent grant is limited to patent claims licensable by contributors under the license; it is **not** a patent license from Neo4j or any unrelated third party.

Dependencies, third-party contributions, and separately licensed materials may carry their own obligations.

## 2. Bolt protocol documentation is separately licensed

Neo4j publishes the Bolt wire protocol documentation at:

- [Bolt Protocol documentation](https://neo4j.com/docs/bolt/current/)
- [Documentation source repository](https://github.com/neo4j/docs-bolt)
- [Documentation repository license](https://github.com/neo4j/docs-bolt/blob/main/LICENSE.txt)

At the time these notes were written, the published `neo4j/docs-bolt` repository identified its documentation license as **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)**.

This license covers the copyright-protected documentation and adaptations within its scope. In general, a license on the expression of a protocol specification does not, by itself, dictate the license of independently written software implementing the protocol. That distinction is not a blanket legal clearance: rights in copied source or text, patents, trademarks, contractual restrictions, and applicable law must be considered separately.

**BoltSharp's Apache 2.0 license does not relicense Neo4j's documentation.** Do not copy or adapt substantial parts of that documentation into BoltSharp's distributable code, tests, or documentation without an appropriate legal basis and compliance with applicable terms.

## 3. Independent implementation and provenance

BoltSharp's intended development approach is interoperability through original implementation and reproducible testing, not redistribution of Neo4j server code.

Contributors should:

- Write original code and original explanatory documentation.
- Treat external protocol documents as references; avoid copying their expressive text, examples, or implementation source into this repository.
- Record relevant reference URLs, protocol versions, and test evidence for compatibility decisions.
- Keep third-party files and dependencies clearly identified, with their applicable notices.
- Raise questions before including external code, large examples, test fixtures, or documentation whose license is unclear.

These practices support provenance but do not independently establish legal non-infringement.

## 4. Compatibility is a technical claim, not an endorsement

BoltSharp seeks to support the Bolt wire protocol used by Neo4j drivers, so other databases and services can expose compatible endpoints.

**Bolt-compatible** means an implementation of specified wire-level behavior. It does **not** imply:

- Affiliation with, sponsorship by, certification from, or endorsement by Neo4j.
- Compatibility with every Bolt protocol version or Neo4j driver release.
- Implementation of the Neo4j database engine or all Neo4j features.
- Implementation of the Cypher query language or equivalent query semantics.
- Support for Neo4j Browser, routing, TLS configurations, or authentication modes unless explicitly verified.

Compatibility statements should cite the protocol revision, client/library version, configuration, tested workflow, and reproducible results. Planned support and specification-based expectations must be separated from observed behavior.

See [the compatibility target](compatibility-target.md) for the initial version-selection research and its stated unknowns. That research is not itself proof of successful driver or Browser interoperability.

## 5. Names and trademarks

Neo4j and Cypher are trademarks or registered trademarks of Neo4j, Inc. or its subsidiaries. They are used here only to identify the relevant technology and describe intended compatibility.

BoltSharp is independently developed and is **not affiliated with, sponsored by, endorsed by, or maintained by Neo4j**. Do not use Neo4j logos, branding, or product naming in ways that imply otherwise.

See [Neo4j's trademark policy](https://neo4j.com/legal-terms/) for the current policy.

## 6. Commercial use and remaining questions

The Apache 2.0 license allows commercial use of BoltSharp code covered by that license. This does **not** guarantee that every possible Bolt-compatible product, packaging choice, third-party component, or distribution model is free of other intellectual-property or contractual constraints.

Before commercial release, review third-party dependencies and notices, source provenance, branding, applicable patent concerns, and any material copied or adapted from external specifications. Seek qualified legal review where material risk remains.

## References

- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- [Creative Commons BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)
- [Neo4j Bolt Protocol documentation](https://neo4j.com/docs/bolt/current/)
- [Neo4j Bolt documentation license](https://github.com/neo4j/docs-bolt/blob/main/LICENSE.txt)
- [Neo4j legal terms and trademark policy](https://neo4j.com/legal-terms/)
