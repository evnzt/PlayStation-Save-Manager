<p align="center">
<img width="1014" height="222" alt="PSM Logo" src="https://github.com/user-attachments/assets/14e14033-411b-4352-8f4e-996ad66c7c50" />
</p>


# PlayStation Save Manager v1.0.0

![Latest Release](https://img.shields.io/github/v/release/evnzt/PlayStation-Save-Manager?label=Latest%20Release) ![Downloads](https://img.shields.io/github/downloads/evnzt/PlayStation-Save-Manager/total) ![Stars](https://img.shields.io/github/stars/evnzt/PlayStation-Save-Manager?style=social) ![Platform](https://img.shields.io/badge/Platform-Windows-0078D6) ![AI Assisted](https://img.shields.io/badge/AI-Assisted-412991)

Windows utility for managing PlayStation and PlayStation 2 memory cards, save packages, conversions, and backups.

Run `Build-and-Launch.cmd` to build and launch. See `README.txt` for details.

## Features

- PlayStation 1 & PlayStation 2 support
- Memory Card Manager
- Individual Save Manager
- Universal Save Converter
- Universal Import Wizard
- Favorites
- Search & Filtering
- Native animated PS2 icons
- Native PS1 icons
- Memory Card Library
- Game Save Library
- Metadata

## Compatible With

✔ Original PlayStation

✔ Original PlayStation 2

✔ MemCard PRO2

✔ PCSX2

✔ DuckStation

✔ RetroArch

✔ AetherSX2 / NetherSX2

✔ And more

## Supported Formats

<details>
<summary><strong>PlayStation 1</strong></summary>

The application can manage and convert between:
| Format | Description |
|---------|-------------|
|.MCR | ePSXe / DexDrive Memory Card|
|.GME | DexDrive Save|
|.BIN | Raw Memory Card|
|.MCD | Bleem! Memory Card|
|.MEM | VGS Memory Card|
|.MC | PSXGameEdit Memory Card|
|.DDF | DataDeck Memory Card|
|.PS | WinPSM Memory Card|
|.PSM | Smart Link Memory Card|
|.MCI | MCExplorer Memory Card|
|.SRM | Save RAM|
|.VMP | PSP / PS3 Virtual Memory Card|
|.VM1 | PS3 Virtual Memory Card|

</details>

<details>
<summary><strong>PlayStation 2</strong></summary>

The application can manage and convert between:
| Format | Description |
|---------|-------------|
| .PSU | EMS / uLaunchELF Memory Linker Save |
| .MAX | Action Replay MAX |
| .CBS | CodeBreaker |
| .XPS | X-Port |
| .XPO | X-Port (legacy) |
| .SPS | SharkPort |
| .MD | SharkPort / InterAct |
| .NPO | NPort |
| .P2M | Xploder |
| .PSV | PS3 Virtual Memory Card Export |
| .PS2 | Standard Memory Card |
| .MC2 | MemCard PRO2 |
| Folder Card | PCSX2 Folder Memory Card |

</details>

<details>
<summary><strong>Memory Cards</strong></summary>

The application can manage and convert between:
| Format | Description |
|---------|-------------|
|.MC/MCR | PlayStation 1 Memory Cards|
|.MC2 | MemCard PRO2|
|.PS2 | PlayStation 2 Memory Card|
|Folder Card | PCSX2 Folder Memory Cards|

</details>

---

<img width="1560" height="980" alt="image" src="https://github.com/user-attachments/assets/c435cc9e-d525-488f-b15e-9ab0c0905c73" />

---

## Why this project exists

PlayStation Save Manager started as a personal project.

Like countless others in the preservation community, I’ve accumulated multiple consoles, memory cards, handhelds, emulators, and accessories over the years.

With that collection came a problem.

My save files were scattered everywhere.

Some lived on original memory cards. Others were on my MemCard PRO2. Some were inside PCSX2. Others were in DuckStation, RetroArch, or on different devices entirely.

And that's how this project truly came about.

I ran into issues converting my .mc2 memory card files to .ps2 format so I could use them with mymc to transfer to different devices.

I started looking for software that could make things easier.

I found several excellent utilities. Some converted save files. Others managed memory cards. Some existed within one emulator, while others focused on specific save formats.

They were all useful.

But none of them did what I needed.

That’s when one simple thought crossed my mind:

  `“Why isn’t there one application that can do all of this?”`

The answer to that:

  `PlayStation Save Manager.`

The goal became simple:

>Create one modern, beginner-friendly application capable of managing nearly every PlayStation save format in one place.

---

# Installation Guide

Download the latest release from the **Releases** page:

**https://github.com/evnzt/PlayStation-Save-Manager/releases/latest**

Download the latest Windows package:

`PlayStation_Save_Manager_vX.X.X_Windows_x64.zip`

## Extract

Extract the ZIP anywhere you like.

Example location:

``` text
C:\Emulation\PlayStation Save Manager\
```

No installation is required.

PlayStation Save Manager is completely portable.

-   No installer
-   No registry modifications
-   No administrator privileges required
-   Can be placed on a USB drive

## Launch

Run:

``` text
Launch.cmd
```

or

``` text
Build-and-Launch.cmd
```

The launcher will automatically:

-   Verify the required runtime
-   Download the myMC++ engine (first launch only)
-   Configure the application
-   Start PlayStation Save Manager
-   *If Build-and-Launch is selected, then it will build the .exe in the Publish folder

> **Note:** The first launch may take 30--60 seconds while the required
> engine is downloaded. Future launches are much faster.
> 
> On first launch, PlayStation Save Manager also downloads the official myMC++ release
> directly from its original project and configures it automatically, which powers
> several memory card operations.

This happens only once.

After setup is complete, everything runs locally.

## Updating

1.  Download the latest release.
2.  Extract it over your existing installation (or into a new folder).
3.  Launch the application.

Your save libraries, settings, and memory cards are **not modified** by
the update process.

------------------------------------------------------------------------

## Troubleshooting

### Windows SmartScreen

Because PSM is currently unsigned, Windows may display a warning the
first time it is launched.

Click:

``` text
More info
```

then

``` text
Run anyway
```

### Antivirus Warning

Some antivirus programs may flag unsigned applications or scripts.

PlayStation Save Manager is open source, so you can inspect the code
yourself before running it.

### Engine Download Failed

If the setup cannot download the required engine:

-   Check your internet connection.
-   Temporarily allow PowerShell through your firewall if prompted.
-   Run `Launch.cmd` again.

------------------------------------------------------------------------

## Building from Source

Clone the repository:

``` bash
git clone https://github.com/evnzt/PlayStation-Save-Manager.git
```

Then run either:

``` text
Build-and-Launch.cmd
```

or

``` text
Build-Release.cmd
```

---

## Built With

- C#
- WPF
- .NET 8

---

## Third-Party Components - Credits

PlayStation Save Manager interfaces with several community projects.

See:

- THIRD-PARTY-NOTICES.txt
- CREDITS.txt

for complete acknowledgments.

• myMC++
  PS2 memory card engine used by PlayStation Save Manager.
  Copyright © the myMC++ contributors. Used under the GNU GPL v3 license.
  https://github.com/Adubbz/mymcplusplus

Special thanks to the developers of myMC++, mymc, MemcardRex, PS2 Save Converter / Builder, and PCSX2 for all the inspiration.

---

## License

MIT License

See LICENSE.

---

## Contributing

Pull requests are welcome.

Bug reports, feature requests, UI improvements, code cleanups, and optimizations are appreciated.

Constructive criticism is always welcome.

---

## About

Created by **evnzt**

Engineering assistance provided using **OpenAI ChatGPT**

This project is Open source and provided completely free for the PlayStation community.
