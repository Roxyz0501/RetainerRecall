# Third-party references and dependencies

Retainer Listing Helper (InternalName RetainerRecall) is authored by Roxyz0501. Third-party projects retain their own authorship.

- Dalamud: https://github.com/goatcorp/Dalamud — goatcorp and contributors, AGPL-3.0. Host plugin API and host-provided ImGui bindings, referenced at build/runtime, not redistributed in this ZIP.
- FFXIVClientStructs: https://github.com/aers/FFXIVClientStructs — aers and contributors, MIT. Public structure definitions and UI methods are used through the host-provided assembly. No custom signature scans or packet definitions. The MIT notice is included below. Version 0.2 no longer directly invokes market movement functions.
- Marketbuddy: https://github.com/PunishXIV/Marketbuddy (original https://github.com/Chalkos/Marketbuddy) — Chalkos, NightmareXIV and contributors, Apache-2.0. References: positioning an overlay relative to RetainerSellList, saved UseMaxStackSize/MaximumStackSize settings, public Lock/Unlock/IsLocked IPC. No implementation code or artwork is copied or distributed.
- AutoRetainer: https://github.com/PunishXIV/AutoRetainer — NightmareXIV, kawaii and contributors, BSD-3-Clause. Reference: QuickSellItems observes OpenForItemSlot after the original function completes, then selects an enabled ordinary inventory-context-menu entry by localized text. No source implementation is copied; no dependency is introduced.
- ECommons: https://github.com/NightmareXIV/ECommons — NightmareXIV and contributors, MIT. ContextMenu entry layout/native-entry validation, RetainerSell numeric-input callback contracts and registered button-event dispatch were adapted for the guarded UI flow. The MIT notice is included below. No ECommons DLL is bundled.
- Dagobert: https://github.com/SHOEGAZEssb/Dagobert — SHOEGAZEssb and contributors, AGPL-3.0. Reference material for the public client UI callback contract that opens a RetainerSellList row context menu. No implementation code was copied or bundled.
- Dalamud.NET.Sdk / DalamudPackager: goatcorp build tooling; not shipped as a runtime dependency.

No other plugin or third-party runtime DLL is bundled or required. The plugin depends on the Dalamud host and its FFXIVClientStructs bindings. The icon is an original AI-generated asset for this project.

## FFXIVClientStructs MIT notice

MIT License

Copyright (c) 2021-2023 aers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## ECommons MIT notice

MIT License

Copyright (c) 2023 NightmareXIV

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.