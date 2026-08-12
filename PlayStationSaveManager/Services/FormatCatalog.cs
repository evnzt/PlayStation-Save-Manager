using System;
using System.IO;

namespace PlayStationSaveManager.Services;

public static class FormatCatalog
{
    public const string Ps1MemoryCardFilter =
        "PS1 Memory Cards|*.bin;*.ddf;*.gme;*.mc;*.mcd;*.mci;*.mcr;*.mem;*.ps;*.psm;*.sav;*.srm;*.vgs;*.vm1;*.vmc;*.vmp|" +
        "pSX / AdriPSX Memory Card (*.bin)|*.bin|" +
        "DataDeck Memory Card (*.ddf)|*.ddf|" +
        "DexDrive Memory Card (*.gme)|*.gme|" +
        "PSXGame Edit Memory Card (*.mc)|*.mc|" +
        "Bleem! Memory Card (*.mcd)|*.mcd|" +
        "MCExplorer Memory Card (*.mci)|*.mci|" +
        "ePSXe / PSEmu Pro Memory Card (*.mcr)|*.mcr|" +
        "VGS / Connectix Memory Card (*.mem)|*.mem|" +
        "WinPSM Memory Card (*.ps)|*.ps|" +
        "Smart Link Memory Card (*.psm)|*.psm|" +
        "SAV Memory Card (*.sav)|*.sav|" +
        "RetroArch / Libretro Memory Card (*.srm)|*.srm|" +
        "VGS / Connectix Memory Card (*.vgs)|*.vgs|" +
        "PS3 Virtual Memory Card (*.vm1)|*.vm1|" +
        "Virtual Memory Card (*.vmc)|*.vmc|" +
        "PSP Virtual Memory Card (*.vmp)|*.vmp|" +
        "All files|*.*";

    public const string Ps1IndividualSaveFilter =
        "PS1 Individual Saves|*.mcb;*.mcs;*.mcx;*.pda;*.ps1;*.psv;*.psx;*.raw|" +
        "PS1 Individual Save - MCB • Smart Link (*.mcb)|*.mcb|" +
        "PS1 Individual Save - MCS • PSXGameEdit (*.mcs)|*.mcs|" +
        "PS1 Individual Save - MCX • Datel (*.mcx)|*.mcx|" +
        "PS1 Individual Save - PDA • Datel (*.pda)|*.pda|" +
        "PS1 Individual Save - PS1 • Memory Juggler (*.ps1)|*.ps1|" +
        "PS1 Individual Save - PSV • PS3 Virtual Save (*.psv)|*.psv|" +
        "PS1 Individual Save - PSX • X-Port / AR / GameShark (*.psx)|*.psx|" +
        "PS1 Individual Save - RAW (*.raw)|*.raw|" +
        "All files|*.*";

    public const string Ps1SaveExportFilter =
        "PS1 Individual Save - MCB • Smart Link (*.mcb)|*.mcb|" +
        "PS1 Individual Save - MCS • PSXGameEdit (*.mcs)|*.mcs|" +
        "PS1 Individual Save - MCX • Datel (*.mcx)|*.mcx|" +
        "PS1 Individual Save - PDA • Datel (*.pda)|*.pda|" +
        "PS1 Individual Save - PS1 • Memory Juggler (*.ps1)|*.ps1|" +
        "PSM PlayStation Save Package (*.ps1save)|*.ps1save|" +
        "PS1 Individual Save - PSV • PS3 Virtual Save (*.psv)|*.psv|" +
        "PS1 Individual Save - PSX • X-Port / AR / GameShark (*.psx)|*.psx|" +
        "PS1 Individual Save - RAW (*.raw)|*.raw|" +
        "pSX / AdriPSX Memory Card (*.bin)|*.bin|" +
        "DataDeck Memory Card (*.ddf)|*.ddf|" +
        "DexDrive Memory Card (*.gme)|*.gme|" +
        "PSXGame Edit Memory Card (*.mc)|*.mc|" +
        "Bleem! Memory Card (*.mcd)|*.mcd|" +
        "MCExplorer Memory Card (*.mci)|*.mci|" +
        "ePSXe / PSEmu Pro Memory Card (*.mcr)|*.mcr|" +
        "VGS / Connectix Memory Card (*.mem)|*.mem|" +
        "WinPSM Memory Card (*.ps)|*.ps|" +
        "Smart Link Memory Card (*.psm)|*.psm|" +
        "SAV Memory Card (*.sav)|*.sav|" +
        "RetroArch / Libretro Memory Card (*.srm)|*.srm|" +
        "VGS / Connectix Memory Card (*.vgs)|*.vgs|" +
        "PS3 Virtual Memory Card (*.vm1)|*.vm1|" +
        "Virtual Memory Card (*.vmc)|*.vmc|" +
        "PSP Virtual Memory Card (*.vmp)|*.vmp";

    public const string SupportedMemoryCardFilter =
        "All Supported Memory Cards|*.bin;*.ddf;*.gme;*.mc;*.mc2;*.mcd;*.mci;*.mcr;*.mem;*.ps;*.ps2;*.psm;*.sav;*.srm;*.vgs;*.vm1;*.vm2;*.vmc;*.vmp|" +
        "BIN Memory Card (PS1 / PS2) (*.bin)|*.bin|" +
        "DataDeck Memory Card (*.ddf)|*.ddf|" +
        "DexDrive Memory Card (*.gme)|*.gme|" +
        "PSXGame Edit Memory Card (*.mc)|*.mc|" +
        "MemCard PRO2 Memory Card (*.mc2)|*.mc2|" +
        "MCD Memory Card (PS1 / PS2) (*.mcd)|*.mcd|" +
        "MCExplorer Memory Card (*.mci)|*.mci|" +
        "ePSXe / PSEmu Pro Memory Card (*.mcr)|*.mcr|" +
        "VGS / Connectix Memory Card (*.mem)|*.mem|" +
        "WinPSM Memory Card (*.ps)|*.ps|" +
        "PCSX2 Memory Card (*.ps2)|*.ps2|" +
        "Smart Link Memory Card (*.psm)|*.psm|" +
        "SAV Memory Card (*.sav)|*.sav|" +
        "RetroArch / Libretro Memory Card (*.srm)|*.srm|" +
        "VGS / Connectix Memory Card (*.vgs)|*.vgs|" +
        "PS3 Virtual Memory Card (*.vm1)|*.vm1|" +
        "PS2 Virtual Memory Card (*.vm2)|*.vm2|" +
        "VMC Memory Card (PS1 / PS2) (*.vmc)|*.vmc|" +
        "PSP Virtual Memory Card (*.vmp)|*.vmp|" +
        "All files|*.*";

    public const string Ps2MemoryCardFilter =
        "PS2 Memory Cards|*.bin;*.mc2;*.mcd;*.ps2;*.vm2;*.vmc|" +
        "PS2 BIN Memory Card (*.bin)|*.bin|" +
        "MemCard PRO2 Memory Card (*.mc2)|*.mc2|" +
        "PS2 MCD Memory Card (*.mcd)|*.mcd|" +
        "PCSX2 Memory Card (*.ps2)|*.ps2|" +
        "PS2 Virtual Memory Card (*.vm2)|*.vm2|" +
        "PS2 VMC Memory Card (*.vmc)|*.vmc|" +
        "All files|*.*";

    public const string Ps2SaveExportFilter =
        "PS2 Individual Save - CBS • CodeBreaker (*.cbs)|*.cbs|" +
        "PS2 Individual Save - MAX • Action Replay MAX (*.max)|*.max|" +
        "PS2 Individual Save - PSU • EMS / uLaunchELF (*.psu)|*.psu|" +
        "PS2 Individual Save - PSV • PS3 Virtual Save (*.psv)|*.psv|" +
        "PS2 Individual Save - SPS • SharkPort (*.sps)|*.sps|" +
        "PS2 Individual Save - XPS • X-Port / Xploder (*.xps)|*.xps|" +
        "PS2 BIN Memory Card (*.bin)|*.bin|" +
        "MemCard PRO2 Memory Card (*.mc2)|*.mc2|" +
        "PS2 MCD Memory Card (*.mcd)|*.mcd|" +
        "PCSX2 Memory Card (*.ps2)|*.ps2|" +
        "PS2 Virtual Memory Card (*.vm2)|*.vm2|" +
        "PS2 VMC Memory Card (*.vmc)|*.vmc|" +
        "PCSX2 Folder Card (*.foldercard)|*.foldercard";

    public const string Ps2PackageImportFilter =
        "PS2 Packaged Saves|*.cbs;*.max;*.psu;*.psv;*.sps;*.xps|" +
        "PS2 Individual Save - CBS • CodeBreaker (*.cbs)|*.cbs|" +
        "PS2 Individual Save - MAX • Action Replay MAX (*.max)|*.max|" +
        "PS2 Individual Save - PSU • EMS / uLaunchELF (*.psu)|*.psu|" +
        "PS2 Individual Save - PSV • PS3 Virtual Save (*.psv)|*.psv|" +
        "PS2 Individual Save - SPS • SharkPort (*.sps)|*.sps|" +
        "PS2 Individual Save - XPS • X-Port / Xploder (*.xps)|*.xps|" +
        "All files|*.*";

    public const string SaveLibraryImportFilter =
        "PlayStation Saves|*.cbs;*.max;*.mcb;*.mcs;*.mcx;*.pda;*.ps1;*.ps1save;*.psu;*.psv;*.psx;*.raw;*.sps;*.xps|" +
        "PS2 Individual Save - CBS • CodeBreaker (*.cbs)|*.cbs|" +
        "PS2 Individual Save - MAX • Action Replay MAX (*.max)|*.max|" +
        "PS1 Individual Save - MCB • Smart Link (*.mcb)|*.mcb|" +
        "PS1 Individual Save - MCS • PSXGameEdit (*.mcs)|*.mcs|" +
        "PS1 Individual Save - MCX • Datel (*.mcx)|*.mcx|" +
        "PS1 Individual Save - PDA • Datel (*.pda)|*.pda|" +
        "PS1 Individual Save - PS1 • Memory Juggler (*.ps1)|*.ps1|" +
        "PSM PlayStation Save Package (*.ps1save)|*.ps1save|" +
        "PS2 Individual Save - PSU • EMS / uLaunchELF (*.psu)|*.psu|" +
        "PlayStation Virtual Save - PSV (*.psv)|*.psv|" +
        "PS1 Individual Save - PSX • X-Port / AR / GameShark (*.psx)|*.psx|" +
        "PS1 Individual Save - RAW (*.raw)|*.raw|" +
        "PS2 Individual Save - SPS • SharkPort (*.sps)|*.sps|" +
        "PS2 Individual Save - XPS • X-Port / Xploder (*.xps)|*.xps|" +
        "All files|*.*";

    public const string SupportedPlayStationFilter =
        "All Supported PlayStation Saves / Cards|*.bin;*.cbs;*.ddf;*.gme;*.max;*.mc;*.mc2;*.mcb;*.mcd;*.mci;*.mcr;*.mcs;*.mcx;*.mem;*.pda;*.ps;*.ps1;*.ps1save;*.ps2;*.psm;*.psu;*.psv;*.psx;*.raw;*.sav;*.sps;*.srm;*.vgs;*.vm1;*.vm2;*.vmc;*.vmp;*.xps|" +
        "PS1 Individual Saves|*.mcb;*.mcs;*.mcx;*.pda;*.ps1;*.psv;*.psx;*.raw|" +
        "PS1 Memory Cards|*.bin;*.ddf;*.gme;*.mc;*.mcd;*.mci;*.mcr;*.mem;*.ps;*.psm;*.sav;*.srm;*.vgs;*.vm1;*.vmc;*.vmp|" +
        "PS2 Packaged Saves|*.cbs;*.max;*.psu;*.psv;*.sps;*.xps|" +
        "PS2 Memory Cards|*.bin;*.mc2;*.mcd;*.ps2;*.vm2;*.vmc|" +
        "Memory Card - BIN (PS1 / PS2) (*.bin)|*.bin|" +
        "PS2 Individual Save - CBS • CodeBreaker (*.cbs)|*.cbs|" +
        "PS1 Memory Card - DDF • DataDeck (*.ddf)|*.ddf|" +
        "PS1 Memory Card - GME • DexDrive (*.gme)|*.gme|" +
        "PS2 Individual Save - MAX • Action Replay MAX (*.max)|*.max|" +
        "PS1 Memory Card - MC • PSXGame Edit (*.mc)|*.mc|" +
        "PS2 Memory Card - MC2 • MemCard PRO2 (*.mc2)|*.mc2|" +
        "PS1 Individual Save - MCB • Smart Link (*.mcb)|*.mcb|" +
        "Memory Card - MCD (PS1 / PS2) (*.mcd)|*.mcd|" +
        "PS1 Memory Card - MCI • MCExplorer (*.mci)|*.mci|" +
        "PS1 Memory Card - MCR • ePSXe / PSEmu Pro (*.mcr)|*.mcr|" +
        "PS1 Individual Save - MCS • PSXGameEdit (*.mcs)|*.mcs|" +
        "PS1 Individual Save - MCX • Datel (*.mcx)|*.mcx|" +
        "PS1 Memory Card - MEM • VGS / Connectix (*.mem)|*.mem|" +
                        "PS1 Individual Save - PDA • Datel (*.pda)|*.pda|" +
        "PS1 Memory Card - PS • WinPSM (*.ps)|*.ps|" +
        "PS1 Individual Save - PS1 • Memory Juggler (*.ps1)|*.ps1|" +
        "PSM PlayStation Save Package (*.ps1save)|*.ps1save|" +
        "PS2 Memory Card - PS2 • PCSX2 (*.ps2)|*.ps2|" +
        "PS1 Memory Card - PSM • Smart Link (*.psm)|*.psm|" +
        "PS2 Individual Save - PSU • EMS / uLaunchELF (*.psu)|*.psu|" +
        "PlayStation Virtual Save - PSV (PS1 / PS2) (*.psv)|*.psv|" +
        "PS1 Individual Save - PSX • X-Port / AR / GameShark (*.psx)|*.psx|" +
        "PS1 Individual Save - RAW (*.raw)|*.raw|" +
        "PS1 Memory Card - SAV (*.sav)|*.sav|" +
        "PS2 Individual Save - SPS • SharkPort (*.sps)|*.sps|" +
        "PS1 Memory Card - SRM • RetroArch / Libretro (*.srm)|*.srm|" +
        "PS1 Memory Card - VGS • VGS / Connectix (*.vgs)|*.vgs|" +
        "PS1 Memory Card - VM1 • PS3 Virtual Memory Card (*.vm1)|*.vm1|" +
        "PS2 Memory Card - VM2 • Virtual Memory Card (*.vm2)|*.vm2|" +
        "Memory Card - VMC (PS1 / PS2) (*.vmc)|*.vmc|" +
        "PS1 Memory Card - VMP • PSP Virtual Memory Card (*.vmp)|*.vmp|" +
        "PS2 Individual Save - XPS • X-Port / Xploder (*.xps)|*.xps|" +
        "All files|*.*";

    public static string GetPs1CardTypeName(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".bin" => "pSX / AdriPSX Memory Card",
            ".ddf" => "DataDeck Memory Card",
            ".gme" => "DexDrive Memory Card",
            ".mc" => "PSXGame Edit Memory Card",
            ".mcd" => "Bleem! Memory Card",
            ".mci" => "MCExplorer Memory Card",
            ".mcr" => "ePSXe / PSEmu Pro Memory Card",
            ".mem" or ".vgs" => "VGS / Connectix Memory Card",
            ".ps" => "WinPSM Memory Card",
            ".psm" => "Smart Link Memory Card",
            ".sav" => "SAV Memory Card",
            ".srm" => "RetroArch / Libretro Memory Card",
            ".vm1" => "PS3 Virtual Memory Card",
            ".vmc" => "Virtual Memory Card",
            ".vmp" => "PSP Virtual Memory Card",
            _ => "Standard PS1 Memory Card"
        };

    public static string GetPs2CardTypeName(string path)
    {
        if (Directory.Exists(path))
            return "PCSX2 Folder Memory Card";

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".bin" => "PS2 BIN Memory Card",
            ".mc2" => "MemCard PRO2 Memory Card",
            ".mcd" => "PS2 MCD Memory Card",
            ".ps2" => "PCSX2 Memory Card",
            ".vm2" => "PS2 Virtual Memory Card",
            ".vmc" => "PS2 VMC Memory Card",
            _ => "Standard PS2 Memory Card"
        };
    }
}
