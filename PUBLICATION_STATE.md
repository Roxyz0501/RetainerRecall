# Publication state

## 2026-10-08 version 0.2.0.2 preparation

- User-reported native crash traced to ListingGamePort.Confirm -> AddonRetainerSell.ReceiveEvent. The fifth argument (AtkEventData) was omitted; native access violation read address 7, consistent with the null input's modifier byte.
- NativeButton now supplies fully initialized input data for the registered ButtonClick event. Button owner null checks precede IsEnabled dereferences.
- Added 12 isolated native ABI boundary checks. An isolated copy with the previous omitted argument fails the new regression test; the corrected implementation passes. Existing 90 managed checks pass.
- No raw crash logs, dumps, item details, machine identifiers or local paths are included in publication.
- Release target: https://github.com/Roxyz0501/RetainerRecall/releases/tag/v0.2.0.2
- ZIP target: https://github.com/Roxyz0501/RetainerRecall/releases/download/v0.2.0.2/RetainerRecall-0.2.0.2.zip
- Corrected in-game operation remains unverified. Full-armoury recall issue remains unresolved. Publication pending final Release build and package verification.

## 2026-10-08 version 0.2.0.1 publication

- Delay range lowered to 0.1–30 seconds for listing and recall; acknowledgement and UI transition waits retained.
- Player-side listing scans bags and all 11 supported armoury equipment compartments, and can start from either. Equipped items and soul crystals remain excluded.
- Shortcut target observation moved from ContextMenu PostSetup to after the original OpenForItemSlot function, following the referenced AutoRetainer entry route; native localized-menu selection remains guarded.
- Recall acknowledgement now includes armoury contents; start failures and stop/completion reasons are printed to chat. The reported full-armoury stop is not reproduced or conclusively diagnosed; resolution is unverified.
- Release target: https://github.com/Roxyz0501/RetainerRecall/releases/tag/v0.2.0.1
- ZIP target: https://github.com/Roxyz0501/RetainerRecall/releases/download/v0.2.0.1/RetainerRecall-0.2.0.1.zip
- Clean Release build (zero warnings/errors), 90 managed checks and five isolated UI/command/persistence checks passed. Native in-game hook/armoury behavior remains unverified.
- Published from source commit `8c70bb1`. Classification, author, optional dependencies, original icon and third-party notices retained. Public repository/icon HTTP 200 and image/png verified; downloaded ZIP matches SHA-256 `7A43B67D03354E02882B22EE527A62F5D397A5FB9CA695AFDB23058469C9273B`.
- Anonymous commit-pinned and normal main shared index verified at registration commit `98d10e097dcd96852f744340d9d9afbef1172f9d`; six entries, with all five other plugins preserved.

## 2026-10-08 version 0.2.0.0 publication

- Display name: Retainer Listing Helper; primary command `/retainerlisting`, legacy `/retainerrecall` retained.
- InternalName, dedicated source/release repository and icon URL remain RetainerRecall for existing updates/configuration.
- Adds session-only, item-ID keyed final listing prices (NQ/HQ shared), configurable modifier + right-click, delayed batch listing and optional Marketbuddy quantity-setting reads/IPC locking.
- Listing and recall now both use ordinary enabled context-menu entries. Direct market movement calls removed.
- Release target: https://github.com/Roxyz0501/RetainerRecall/releases/tag/v0.2.0.0
- ZIP target: https://github.com/Roxyz0501/RetainerRecall/releases/download/v0.2.0.0/RetainerRecall-0.2.0.0.zip
- 81 managed checks passed; isolated settings rendering, new/legacy commands, enable toggle, JSON config roundtrip and command disposal checked. Installed game Addon rows 99/958/976 checked offline.
- Native in-game listing/recall, shortcut triggering, cross-plugin coexistence and wire-level equivalence remain unverified and disclosed.
- Optional Marketbuddy integration only; no required external plugin or bundled third-party runtime DLL.
- GitHub release published from source commit `3f85c4c`; anonymous repository/icon HTTP 200 (image/png) and release ZIP verified. Shared index registration complete.
- ZIP SHA-256: `98E28252C72E2E43F6852C5B7FD5338E129CBB1FB41026D36431D14644D740FA`.
- Shared commit: `df395dcf0697c2069e7788aa3cc35b01a0f40dc4`; anonymous commit-pinned and normal main index verified with new display name/version and four other entries preserved. The normal URL initially returned cached 0.1.0.0; after revalidation, an ordinary request without extra headers also returned 0.2.0.0.

Updated: 2026-10-08 JST

- Classification: new standalone plugin, explicitly selected by the user at task start.
- Author: Roxyz0501.
- Version: 0.1.0.0, initial public preview published on GitHub and registered in the shared index.
- Source: https://github.com/Roxyz0501/RetainerRecall
- Release: https://github.com/Roxyz0501/RetainerRecall/releases/tag/v0.1.0.0
- ZIP: https://github.com/Roxyz0501/RetainerRecall/releases/download/v0.1.0.0/RetainerRecall-0.1.0.0.zip
- Shared index: https://raw.githubusercontent.com/Roxyz0501/DalamudPluginRepo/main/repo.json
- Icon: https://raw.githubusercontent.com/Roxyz0501/RetainerRecall/main/images/icon.png
- Clean Release build: zero warnings/errors; 27 managed state-machine checks passed.
- Native in-game retrieval and UI placement remain unverified, disclosed in README and release notes.
- No additional plugin dependencies; requires Dalamud API 15 and host-provided FFXIVClientStructs.
- Public source excludes generated binaries, caches, local paths and personal data. Release ZIP includes DLL, manifest, deps manifest, original 512x512 icon, README and license/third-party notices only.
- Anonymous repository/icon/ZIP access verified; repository and icon HTTP 200, icon image/png, downloaded release ZIP matches local package.
- ZIP SHA-256: `9A1C65AF1FB8D6AE79A44C3AE24540E51F7AB455F8E0C958A9A218C32C702511`.
- Release source commit: `0d751a0`.
- Shared registration commit: `287dc436531e03fe5751d96091b5f78ef356d175`; anonymous main and commit-pinned index both verified with version 0.1.0.0. All four previous entries retained.
