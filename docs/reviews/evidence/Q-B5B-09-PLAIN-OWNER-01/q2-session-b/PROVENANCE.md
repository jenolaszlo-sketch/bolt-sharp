# Q2 Session Package Provenance

Materialized byte-exactly from pinned Git commits by
`extract-q2-packages.py`. Source payload bytes come from `git cat-file blob`;
no working-tree file was read and no history was rewritten.

## Session A - first independent Q2 qualification (`q2-session-a/`)

- Commit: `96a1a3cd0f059b554c6e7ab2ce099ef29b161ace`
- Tree: `ae77886a82561fb844c5cc5c874d833cdf1c0fdf`
- Source package: `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2`
- Source report: `docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md`
- Files restored: 49
- Total bytes: 4815887

The source package's own `.gitattributes` is preserved byte-exactly as
`.gitattributes.session-a`. The governing `.gitattributes` in this
directory is an owner guard containing `* -text`, so the preserved bytes
are byte-stable across checkouts and are never rewritten by a re-add.

| Restored path | Git blob | Bytes | SHA-256 |
|---|---|---|---|
| `.gitattributes.session-a` | `fa1385d99a319b43c06f5309d1aae9fdd3adea46` | 8 | `705fd4d6451a31d36b3df7de96f83f30ac976c9b4a6d1e51671d8e2f33e2d0da` |
| `README.md` | `25a566f490482c8167006d4e53bc1275f3cd6bd7` | 4618 | `71d85d2335eda24ff160654647db68813c874fa801d272960adb5c8de26fbd68` |
| `build-production.log` | `7602fe2bf27d5a33ae31fcaac2a371351d717ab7` | 85 | `bf9f530a76fec3836dbd908f64816e7ebb9f9f9ba475d9caac8db788befa84af` |
| `build-solution.log` | `3de91dfc33553ef573f6ac0a941f761fa8424180` | 1459 | `5675da621fe6c4dc6d9572ada83abe897f1c3a22a35b843b107565b7c37ebf45` |
| `campaign/baseline-f01-win.json` | `e4c746f8ab010f36848e48f75c2f216c1f446161` | 909 | `812ff11b5e4f71cf74fcc0b21b933db5a9edefaee7818a992f69352f2176df72` |
| `campaign/baseline-f01-wsl2.json` | `4983c9a7a5fd28379a02a349cc3bf12ec18b0221` | 856 | `a5e32c6f90a7296f086b045246a78c3cefd6fc0d81fac368b574e975f865c959` |
| `campaign/baseline-obs-win.json` | `12c9a08016f43d9095a96b80b26962e471cef7ed` | 1059 | `7330e029eef22e74af345181ce8eadf0a2aa6b3bdfa803a93ea7a873bb789302` |
| `campaign/baseline-obs-wsl2.json` | `3cfd7528e124f224eed29032ad6aa7e94d2608e7` | 1005 | `e85272c93aba9c9bffc80d9cb7e250c0ddcaaf6e7d6eeb5e82ca917432576b89` |
| `campaign/nc08-probe-f01.json` | `27dc1f1e62b1cd1715e17af11810d2dfad5a5cee` | 1895 | `4fe1c74d08f4912e869f8d28abdf239b8e5519c506e54e4562b2b3b6ffc8f08c` |
| `campaign/r1-all-win.json` | `c43e52ba5fe4c6e4900ba99773e236a6df8d623b` | 12783 | `143cc89b79889493da25fa0ef623a5b802c9b79378a403495728bb54853841fe` |
| `campaign/r1-all-wsl2.json` | `bbd17c1d80da2f5ac3dd7e0209471d1607622c5e` | 25288 | `105a3616eba4881e38a4284ed073ebf579393b20c91cfeed58cf8115c1d42625` |
| `campaign/r1-backpressure-win.json` | `9913ed1dfc928bd9168b5bc3ccda5ecc17670f25` | 4004 | `64f104e4803623a8ed3d6b8bc2d889c2f2996b55f88372f56c3fdfb7154823af` |
| `campaign/r1-f01-win.json` | `2a9d84232ffe585bedb0e487519a23224858609a` | 1818 | `d7e47d8d4b48beffbb3999973a92c58f8c86fbbd6b77a687559be9dbe385f33d` |
| `campaign/r1-f01-wsl2.json` | `aa8e508d2ec7049038b7710c4770b5d6b819badd` | 1727 | `cbfaee617bb67848ad15b4e8dd5ab8e9a2f04f296bec607eda9c10ea105f1abb` |
| `campaign/r1-nc-f01-f01-win.json` | `cb0b12e78f758a7b1710c62bdddf9b6b70b9232a` | 1819 | `79fbdee81f671403d25a68e807f86740e5977d857570d83e41b6cc2ec54582d6` |
| `campaign/r1-obs-win.json` | `678a76040dd4c589f90ac709597cb7321d12bf1d` | 5162 | `0d0f87efc6deb3e451e24485a4aa04014be98af0ea73814e7275fe2b71bce23d` |
| `campaign/r1-obs-wsl2.json` | `780e43817637660a0dde62889ac8d68f51c6db96` | 4957 | `36861e7faf4dfa99796aa850bfa0606592ee2b322c6f4517408f01d11cff0692` |
| `core-tests.trx` | `110a3605b68953874efaf0638413b98d3acf7cfb` | 1732317 | `786e932fb512d397572ffa95615581d1b0a15b1ca4c99e803a24c54211a4ae6e` |
| `driver-integration.trx` | `2cca75ae62df60e53b7ed846f2cf09f797822b64` | 140491 | `1c73ad23c49c809295d7b276387bbd9c154c4da92835215533258ea740874eae` |
| `driver-interop.trx` | `221f65308a852d10407b9e2f7f177ac8e036e4e2` | 65966 | `42df2ad8efef700aab7a9bcb97568f153a5db3e17757fc320d0b312816e3386f` |
| `format-check.log` | `9b3bdc9987c67ecf17571a76f76fce519d72394c` | 3151 | `bea9bee92a1aeeee5a7c35e63e8f95f76fc6bed4c29ea491f3941024eaac80d1` |
| `independence-statement.txt` | `e8e89ec9e8c8b5707663296d560ed6b78342bc71` | 1116 | `22a55a6fabff40d9c127d71ead6519c24e21cb5a8b5671180ccd04d1dc92b120` |
| `integration.trx` | `b2410fb19798325180b999948c9c469706792eaf` | 28810 | `24b4b71b64892d2bf8a7fb361a91e79cc0d2198ca81f46df650194f7b04a43a4` |
| `manifest.json` | `2696c4be750abe2d8fa867b4a52b86041fdd0525` | 14075 | `748b9ca2cd2852069c7bc54e268e9a8bb568a32b523fde1706b36ef086512a0a` |
| `negative-controls-assessment.md` | `b04c97d5fe7ef1d1112ed12712faf9c43b93feba` | 4290 | `0a9467529853b75075b623b1e34a48e13bd324c881737a097f971c12470b0dee` |
| `negative-controls/nc-01.log` | `fe4a548205073e77c6e4307ade966a9c1329aa0b` | 117549 | `9ec722a1776757e1eda26a76a5c6df4a357340d864ce04d05e0269cc04a6d88d` |
| `negative-controls/nc-01.trx` | `67c5a82d39fa980e68e626324d8be03003f1c993` | 19295 | `e6cff7e4b8f3edbcce1dfd1ef97c8a959978e36cfb03d8f5023c537c74279c88` |
| `negative-controls/nc-02.log` | `182e7d6d68e9b9626ced371d1f3ae868166174b8` | 111679 | `bc7840718d68cd1607399998344022234db5d93389141c47f4be860c693e26bd` |
| `negative-controls/nc-02.trx` | `08a0b861b12dac17775c53ee78ccb09861024a03` | 5695 | `362ba0dffabfbf7f2ba21b580b0dc82003d53d16f4813f0c99d54b5416702c57` |
| `negative-controls/nc-03.log` | `f62f1c75ede871c30acabedaa6db79f701e94b9e` | 54903 | `d0a1a67c42285de20d73246474af755c751274f44524cf7faaaa895bd5496ac5` |
| `negative-controls/nc-03.trx` | `2b124ff67588bce308d9221d36841cbb31a96435` | 5064 | `aaaec8159546b7712288fb6efc44de3074e7ab2d385c6915603405089bfcbdaa` |
| `negative-controls/nc-04.log` | `f4b214405fc2366a31771f2c8a840349157a859d` | 54807 | `cbc83b3cb2048a557d89cc05aca35ce49c6b154c29b59356cb7e41919c294d0f` |
| `negative-controls/nc-04.trx` | `bed021e86f665d4cd831c6acb6678038c27ed63a` | 4938 | `88ace22bcdfba526ac131b0968ead5e1c2021e33d49727b3734e1f40b73169b2` |
| `negative-controls/nc-05.log` | `b3ffe43b28629461d68af656ef48eda2a09a2fe7` | 55372 | `12736f9d96050ef6910ebdab11a024b01f3be26ef6ee1860b01bddd4f0d1daf8` |
| `negative-controls/nc-05.trx` | `487a6a6b4de70efd3620f351b9bd860fd26675d2` | 12479 | `40ce8b7153345f346d1b59f3c22cce813be477040357398e23a964475ce16682` |
| `negative-controls/nc-06.log` | `235e451703f899117a0edefdc8e9418b3c2a76d6` | 55199 | `1a95a54c864a9f0bc0a5c09589bf0f302b996a6e6a0124ddd638227faaf2ad1f` |
| `negative-controls/nc-06.trx` | `fb69021a2b3a86687f1db453b6298e3a7acc7dd7` | 8104 | `5fc0e919fc411840a42526791a758d7165327975d2b123139ad6c73d7e6a380a` |
| `negative-controls/nc-07.log` | `bfa0922e04e7a7e394a864ec129fca265b1fd356` | 54871 | `ab909f5176350dcf4c95e90b82919088041d116392073891035711151247c70d` |
| `negative-controls/nc-07.trx` | `cf2189687b4e168013c53e715df9494af8e7f727` | 4998 | `9e064d691848905bae7f8f8b978f534a92e266a9297ccbd1e42ecd784a30222a` |
| `negative-controls/nc-08.log` | `6c675cd7ea53c363d5e303b74e5d7303bfa7f2ea` | 142176 | `55e29b84b54140d363886142f4ae98f057a1fb6b3973282104081269f150885f` |
| `negative-controls/nc-08.trx` | `343ced882aab020e43b14d3bc61e25c1bfb20ff3` | 77079 | `24a552879bc7dd6bfb99823882a1e4f4346271f8435c07b024965a681ee8d63e` |
| `negative-controls/nc-09.log` | `3a8fe408fbb16b6d8e7daa1b49eb7ed2fa2139d0` | 66156 | `b4c34f46f3734e4f1856250de7c46c2a87015c9ed87c74c4d8c86adb32dcfb5b` |
| `negative-controls/nc-09.trx` | `7265fbdf9ab54eb8fd0ca6e0f41d6a57bcac84c3` | 54837 | `7159737119f489796d87da8610544bc7fd50050c1834b552e22e12d5a03b9265` |
| `negative-controls/run_controls.py` | `01e1435f4c70861ab53dba97922f3b8d0f51c759` | 9977 | `8b0750600c403998489ce4356f7055b75775f719688cc9f84d6bff1e4a8e9ca2` |
| `recovery-audit.md` | `55a80275c29abc28c401c9a075ece1e3996dfb20` | 5842 | `23d2a45537eceb2dcb2e745ecaec01e07eb1a377041a12e4e214ab6a5b0a8639` |
| `verify-evidence.py` | `567b4d78afa4c48a60f4ec507c5a23a8ae3f80c5` | 8625 | `fa66a5020755ade8e0aa0a8bef7d49e2ca2e93a5d5c7044e2b55b9abf82025d0` |
| `wsl2-core-tests.trx` | `0a923cdff5b8d0993d26c8615dbb3a594103501e` | 1739018 | `d8d402cee5b9dbd4e3f331e63e9d3aa456edc3d347caf8acc63127e119452139` |
| `wsl2-driver-interop.trx` | `eb113f8aa0a50cc837a6253599eda1a4932c36c2` | 66036 | `b0dd3adf5f8d560107451249ebfc0f9440a8a8961b6e8c7e06e2eb8b26bfe596` |
| `report/Q-B5B-09-PLAIN-Q2-qualification.md` | `ea57e39b1fbb77912fb155ba1e3e0130b3918875` | 21520 | `51a6435bd26ed87adc77a91ff37f14321a96ac03e199f85fde5e41c89141d92d` |

## Session B - parallel independent Q2 qualification (`q2-session-b/`)

- Commit: `3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f`
- Tree: `400cc968a989ed6fb67a5688bc4900bfb81265e4`
- Source package: `docs/reviews/evidence/Q-B5B-09-PLAIN-Q2`
- Source report: `docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md`
- Files restored: 54
- Total bytes: 4790110

The source package's own `.gitattributes` is preserved byte-exactly as
`.gitattributes.session-b`. The governing `.gitattributes` in this
directory is an owner guard containing `* -text`, so the preserved bytes
are byte-stable across checkouts and are never rewritten by a re-add.

| Restored path | Git blob | Bytes | SHA-256 |
|---|---|---|---|
| `.gitattributes.session-b` | `cb8b6f301b2497f3499cddbecd49a2a92353aadc` | 91 | `1f7e830b29337175e7a181cdfc25ed3de5e59c2443531682126188477f405264` |
| `README.md` | `ba9222429bec5e3e4b6c246fabcf1466ed630452` | 2963 | `b94b561c1eebf0913ea8fb5b58df1f355faf6d3e9828c37d1b70605e1ab5ea2b` |
| `build-production.log` | `51316414717addd3fd154dec4aa5e09857f9a249` | 77 | `060bba9f268ab4bced0dba5d702e4b0a12cc33b0a36200f997da4ef64e3e69b2` |
| `build-solution.log` | `2ad091842ba57dec95c6a83506b146ea35878b64` | 175 | `5411e312dc2870667dfcac4e1ae172c1d193066d8f2f43fb01a4a48a5a9db753` |
| `campaign/baseline-f01-win.json` | `3cf81f006417b832737c64eeb31b214da2f903d9` | 619 | `8da18b696922f610d5e9f7ccf323e2a6a4a714c6b117fff2ddd5b77aadf943fc` |
| `campaign/baseline-f01-wsl.json` | `76cf6b733457b22aca9d045254e1948d4969213c` | 624 | `f4571372995847cc5f6724a4f4577a24a247655c5430223bb33aa48a6fd69041` |
| `campaign/baseline-f01-wsl2.json` | `4983c9a7a5fd28379a02a349cc3bf12ec18b0221` | 856 | `a5e32c6f90a7296f086b045246a78c3cefd6fc0d81fac368b574e975f865c959` |
| `campaign/baseline-obs-win.json` | `12c9a08016f43d9095a96b80b26962e471cef7ed` | 1059 | `7330e029eef22e74af345181ce8eadf0a2aa6b3bdfa803a93ea7a873bb789302` |
| `campaign/baseline-obs-wsl2.json` | `3cfd7528e124f224eed29032ad6aa7e94d2608e7` | 1005 | `e85272c93aba9c9bffc80d9cb7e250c0ddcaaf6e7d6eeb5e82ca917432576b89` |
| `campaign/nc08-probe-f01.json` | `27dc1f1e62b1cd1715e17af11810d2dfad5a5cee` | 1895 | `4fe1c74d08f4912e869f8d28abdf239b8e5519c506e54e4562b2b3b6ffc8f08c` |
| `campaign/r1-all-win.json` | `6ea1158436c4ca8b20f7ab58ef41bffa4571f114` | 725 | `2a0e5559e5681f6b476076fb8c39473aed542b6ac0f0be7c08482707c5fc81ed` |
| `campaign/r1-all-wsl.json` | `2f324bbdb724588e0c28e236a0df36e30bf75660` | 722 | `dbcbf72e315b19729d3a4f73d983498a8b45c83a6f24e198838612bc19e2c7d3` |
| `campaign/r1-all-wsl2.json` | `bbd17c1d80da2f5ac3dd7e0209471d1607622c5e` | 25288 | `105a3616eba4881e38a4284ed073ebf579393b20c91cfeed58cf8115c1d42625` |
| `campaign/r1-backpressure-win.json` | `34a8f89a30d7a528aa5b86cc39191dac2ef6cd71` | 1552 | `69715af625181476716b2e0b1ce8636f5240cd20693b4c710a963210faf645da` |
| `campaign/r1-backpressure-wsl.json` | `878d093c916f481c80f8fcd11ace3c422e6c0d3f` | 1549 | `aab160df857670770023bd5408599188e3c51579ee992e3b5afd5409b34efad2` |
| `campaign/r1-f01-win.json` | `89dbd7ec209462274c70310b0f44fa3b40206676` | 629 | `64b6335a5264b03d7e25400c78ba6d801a95ff0343679eb7d38333b5d792b482` |
| `campaign/r1-f01-wsl.json` | `4409c3aa2b3f44f5bb755f61ac1d6a4110a034f6` | 634 | `cb8747dd2e81f696b7a6b6f3df6d0bcf4ac95fd85bdf95aa0ed2f7eb2c49a10b` |
| `campaign/r1-f01-wsl2.json` | `aa8e508d2ec7049038b7710c4770b5d6b819badd` | 1727 | `cbfaee617bb67848ad15b4e8dd5ab8e9a2f04f296bec607eda9c10ea105f1abb` |
| `campaign/r1-nc-f01-f01-win.json` | `cb0b12e78f758a7b1710c62bdddf9b6b70b9232a` | 1819 | `79fbdee81f671403d25a68e807f86740e5977d857570d83e41b6cc2ec54582d6` |
| `campaign/r1-obs-win.json` | `678a76040dd4c589f90ac709597cb7321d12bf1d` | 5162 | `0d0f87efc6deb3e451e24485a4aa04014be98af0ea73814e7275fe2b71bce23d` |
| `campaign/r1-obs-wsl2.json` | `780e43817637660a0dde62889ac8d68f51c6db96` | 4957 | `36861e7faf4dfa99796aa850bfa0606592ee2b322c6f4517408f01d11cff0692` |
| `core-tests.trx` | `110a3605b68953874efaf0638413b98d3acf7cfb` | 1732317 | `786e932fb512d397572ffa95615581d1b0a15b1ca4c99e803a24c54211a4ae6e` |
| `driver-integration.trx` | `2cca75ae62df60e53b7ed846f2cf09f797822b64` | 140491 | `1c73ad23c49c809295d7b276387bbd9c154c4da92835215533258ea740874eae` |
| `driver-interop.trx` | `221f65308a852d10407b9e2f7f177ac8e036e4e2` | 65966 | `42df2ad8efef700aab7a9bcb97568f153a5db3e17757fc320d0b312816e3386f` |
| `format-check.log` | `d71b8c03037f504a5591a72eb2477ad0aeaa4523` | 277 | `529dc6f6040b58e057c399302b1c8c04a25fb973fcbbc2a02f8722359139288a` |
| `independence-statement.txt` | `fec06f11a53f5049fb85fde2d401964f90b9ccc2` | 876 | `63997ffde8bd6420aeb3e2e60e32bb18086c2d108f271bae5819a87a5cde109b` |
| `integration.trx` | `b2410fb19798325180b999948c9c469706792eaf` | 28810 | `24b4b71b64892d2bf8a7fb361a91e79cc0d2198ca81f46df650194f7b04a43a4` |
| `manifest.json` | `0dd5fd6c1cbf19f3431d25b4c6ce6eb0b8004576` | 5948 | `10e82cabf0ffc61ea8493b93d8032f1a24ee0b72be6ab679585fcbb4167f7506` |
| `negative-controls-assessment.md` | `c3b4a9e10b00fab2cf124ba3f2b232c17d28e26e` | 3777 | `e58620293aca802e0e0b8c2ff78ae306fcad028b136692ffe1b003b511d805d8` |
| `negative-controls/nc-01.log` | `fe4a548205073e77c6e4307ade966a9c1329aa0b` | 117549 | `9ec722a1776757e1eda26a76a5c6df4a357340d864ce04d05e0269cc04a6d88d` |
| `negative-controls/nc-01.trx` | `67c5a82d39fa980e68e626324d8be03003f1c993` | 19295 | `e6cff7e4b8f3edbcce1dfd1ef97c8a959978e36cfb03d8f5023c537c74279c88` |
| `negative-controls/nc-02.log` | `182e7d6d68e9b9626ced371d1f3ae868166174b8` | 111679 | `bc7840718d68cd1607399998344022234db5d93389141c47f4be860c693e26bd` |
| `negative-controls/nc-02.trx` | `08a0b861b12dac17775c53ee78ccb09861024a03` | 5695 | `362ba0dffabfbf7f2ba21b580b0dc82003d53d16f4813f0c99d54b5416702c57` |
| `negative-controls/nc-03.log` | `f62f1c75ede871c30acabedaa6db79f701e94b9e` | 54903 | `d0a1a67c42285de20d73246474af755c751274f44524cf7faaaa895bd5496ac5` |
| `negative-controls/nc-03.trx` | `2b124ff67588bce308d9221d36841cbb31a96435` | 5064 | `aaaec8159546b7712288fb6efc44de3074e7ab2d385c6915603405089bfcbdaa` |
| `negative-controls/nc-04.log` | `f4b214405fc2366a31771f2c8a840349157a859d` | 54807 | `cbc83b3cb2048a557d89cc05aca35ce49c6b154c29b59356cb7e41919c294d0f` |
| `negative-controls/nc-04.trx` | `bed021e86f665d4cd831c6acb6678038c27ed63a` | 4938 | `88ace22bcdfba526ac131b0968ead5e1c2021e33d49727b3734e1f40b73169b2` |
| `negative-controls/nc-05.log` | `b3ffe43b28629461d68af656ef48eda2a09a2fe7` | 55372 | `12736f9d96050ef6910ebdab11a024b01f3be26ef6ee1860b01bddd4f0d1daf8` |
| `negative-controls/nc-05.trx` | `487a6a6b4de70efd3620f351b9bd860fd26675d2` | 12479 | `40ce8b7153345f346d1b59f3c22cce813be477040357398e23a964475ce16682` |
| `negative-controls/nc-06.log` | `235e451703f899117a0edefdc8e9418b3c2a76d6` | 55199 | `1a95a54c864a9f0bc0a5c09589bf0f302b996a6e6a0124ddd638227faaf2ad1f` |
| `negative-controls/nc-06.trx` | `fb69021a2b3a86687f1db453b6298e3a7acc7dd7` | 8104 | `5fc0e919fc411840a42526791a758d7165327975d2b123139ad6c73d7e6a380a` |
| `negative-controls/nc-07.log` | `bfa0922e04e7a7e394a864ec129fca265b1fd356` | 54871 | `ab909f5176350dcf4c95e90b82919088041d116392073891035711151247c70d` |
| `negative-controls/nc-07.trx` | `cf2189687b4e168013c53e715df9494af8e7f727` | 4998 | `9e064d691848905bae7f8f8b978f534a92e266a9297ccbd1e42ecd784a30222a` |
| `negative-controls/nc-08.log` | `6c675cd7ea53c363d5e303b74e5d7303bfa7f2ea` | 142176 | `55e29b84b54140d363886142f4ae98f057a1fb6b3973282104081269f150885f` |
| `negative-controls/nc-08.trx` | `343ced882aab020e43b14d3bc61e25c1bfb20ff3` | 77079 | `24a552879bc7dd6bfb99823882a1e4f4346271f8435c07b024965a681ee8d63e` |
| `negative-controls/nc-09.log` | `3a8fe408fbb16b6d8e7daa1b49eb7ed2fa2139d0` | 66156 | `b4c34f46f3734e4f1856250de7c46c2a87015c9ed87c74c4d8c86adb32dcfb5b` |
| `negative-controls/nc-09.trx` | `7265fbdf9ab54eb8fd0ca6e0f41d6a57bcac84c3` | 54837 | `7159737119f489796d87da8610544bc7fd50050c1834b552e22e12d5a03b9265` |
| `negative-controls/run_controls.py` | `01e1435f4c70861ab53dba97922f3b8d0f51c759` | 9977 | `8b0750600c403998489ce4356f7055b75775f719688cc9f84d6bff1e4a8e9ca2` |
| `r1-nc-f01-mutation.patch` | `6406321774fc382ef970cf656c007c062cd1f7f2` | 8183 | `691a2715c59314c8b50bc0044c24abae94e84d8b8123082687d33f799500d344` |
| `recovery-audit.md` | `55a80275c29abc28c401c9a075ece1e3996dfb20` | 5842 | `23d2a45537eceb2dcb2e745ecaec01e07eb1a377041a12e4e214ab6a5b0a8639` |
| `verify-evidence.py` | `fe442f474e75d2252e6a4eef34ea98152984663b` | 3261 | `b4c4f1fb5c791fee35c2dc3ff10ba6f0b33d4bcce867e492c99aece08fb0f9d5` |
| `wsl2-core-tests.trx` | `0a923cdff5b8d0993d26c8615dbb3a594103501e` | 1739018 | `d8d402cee5b9dbd4e3f331e63e9d3aa456edc3d347caf8acc63127e119452139` |
| `wsl2-driver-interop.trx` | `eb113f8aa0a50cc837a6253599eda1a4932c36c2` | 66036 | `b0dd3adf5f8d560107451249ebfc0f9440a8a8961b6e8c7e06e2eb8b26bfe596` |
| `report/Q-B5B-09-PLAIN-Q2-qualification.md` | `d59ed1e5838aa19faa045fce9a708c9aabdd1bcf` | 20002 | `b2691296783c68d0c6a30b50aa0e931640e4d0b8cbb2d7abdd6a06de3f0e00c2` |
