# Adds two test divisions to a WARNO mod, to check in the game what EasyNDF's Divisions tab will rely on:
#   1. a new division appears in the division list, with no other list to edit;
#   2. a high deck-code number works (Test A uses 500, Test B uses 30000);
#   3. a division's name is read from the mod's INTERFACE_OUTGAME.csv;
#   4. a mod's own emblem picture loads (Test A has its own; Test B keeps the 3rd Armored's).
#
# Both are copies of the 3rd Armored Division (same units and costs), under new names.
# Run it once, in the folder of a NEW test mod (the folder with GenerateMod.bat), then run GenerateMod.bat.
# Every file it changes is copied first, next to itself, as <name>.before-test; running it again does nothing.

$ErrorActionPreference = "Stop"
$mod = Get-Location
$modName = Split-Path $mod -Leaf
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Path-In([string] $relative) { Join-Path $mod $relative }

if (-not (Test-Path (Path-In "GenerateMod.bat"))) { throw "Run this in your test mod's folder: the one with GenerateMod.bat." }

$divisionsFile = Path-In "GameData\Generated\Gameplay\Decks\Divisions.ndf"
$rulesFile = Path-In "GameData\Generated\Gameplay\Decks\DivisionRules.ndf"
$costsFile = Path-In "GameData\Generated\Gameplay\Decks\DivisionCostMatrix.ndf"
$serializerFile = Path-In "GameData\Generated\Gameplay\Decks\DeckSerializer.ndf"
$texturesFile = Path-In "GameData\Generated\UserInterface\Textures\DivisionTextures.ndf"
$textsFile = Path-In "GameData\Localisation\$modName\INTERFACE_OUTGAME.csv"
$emblemFile = Path-In "GameData\Assets\2D\Interface\UseOutGame\Division\Emblem\EZ_Test.png"

foreach ($file in $divisionsFile, $rulesFile, $costsFile, $serializerFile, $texturesFile, $textsFile) {
    if (-not (Test-Path $file)) { throw "Not found: $file" }
}

# a file's text and how it is stored, so it is written back the same way (byte order mark, line ends)
function Read-Ndf([string] $file) {
    $bytes = [IO.File]::ReadAllBytes($file)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [IO.File]::ReadAllText($file)
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    return @{ Text = $text; HasBom = $hasBom; Newline = $newline }
}

function Write-Ndf([string] $file, $read, [string] $text) {
    if (-not (Test-Path "$file.before-test")) { Copy-Item $file "$file.before-test" }
    [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($read.HasBom)))
}

# one top-level entry, from its first line to the line that closes it at the start of a line
function Get-Block([string] $text, [string] $firstLine, [string] $closer) {
    $pattern = "(?ms)^" + [Regex]::Escape($firstLine) + "\r?\n.*?^" + [Regex]::Escape($closer)
    $match = [Regex]::Match($text, $pattern)
    if (-not $match.Success) { throw "Not found: $firstLine" }
    return $match.Value
}

# adds a line just before the "]" that closes the MAP named after a "<name> = MAP" line
function Add-ToMap([string] $text, [string] $mapStart, [string] $line, [string] $newline) {
    $at = $text.IndexOf($mapStart)
    if ($at -lt 0) { throw "Not found: $mapStart" }
    $close = [Regex]::Match($text.Substring($at), "(?m)^    \]")
    if (-not $close.Success) { throw "No end found for: $mapStart" }
    $insertAt = $at + $close.Index
    return $text.Substring(0, $insertAt) + $line + $newline + $text.Substring($insertAt)
}

$divisions = Read-Ndf $divisionsFile
if ($divisions.Text.Contains("EZ_Test_A_multi")) { Write-Host "The test divisions are already in this mod. Nothing changed."; return }

$tests = @(
    @{ Letter = "A"; Token = "EZDIVTEST1"; Name = "EasyNDF Test A (deck number 500)"; Number = 500; Order = "90.0"; OwnEmblem = $true },
    @{ Letter = "B"; Token = "EZDIVTEST2"; Name = "EasyNDF Test B (deck number 30000)"; Number = 30000; Order = "91.0"; OwnEmblem = $false }
)
$source = "Descriptor_Deck_Division_US_3rd_Arm_multi"

# 1. the divisions, copied from the 3rd Armored's
$divisionBlock = Get-Block $divisions.Text "export $source is TDeckDivisionDescriptor" ")"
$text = $divisions.Text.TrimEnd() + $divisions.Newline
foreach ($test in $tests) {
    $name = "EZ_Test_$($test.Letter)_multi"
    $copy = $divisionBlock.Replace($source, "Descriptor_Deck_Division_$name")
    $copy = $copy.Replace("MatrixCostName_US_3rd_Arm_multi", "MatrixCostName_$name")
    $copy = [Regex]::Replace($copy, "DescriptorId = GUID:\{[^}]*\}", "DescriptorId = GUID:{$([guid]::NewGuid())}")
    $copy = [Regex]::Replace($copy, "CfgName = '[^']*'", "CfgName = '$name'")
    $copy = [Regex]::Replace($copy, "DivisionName = '[^']*'", "DivisionName = '$($test.Token)'")
    $copy = [Regex]::Replace($copy, "DescriptionHintTitleToken = '[^']*'", "DescriptionHintTitleToken = '$($test.Token)'")
    $copy = [Regex]::Replace($copy, "InterfaceOrder = [0-9.]+", "InterfaceOrder = $($test.Order)")
    if ($test.OwnEmblem) { $copy = [Regex]::Replace($copy, 'EmblemTexture(\s*)= "[^"]*"', 'EmblemTexture$1= "Texture_Division_Emblem_EZ_Test"') }
    $text += $copy + $divisions.Newline
}
Write-Ndf $divisionsFile $divisions $text

# 2. their units, copied from the 3rd Armored's rule
$rules = Read-Ndf $rulesFile
$ruleBlock = Get-Block $rules.Text "${source}_Rule is TDeckDivisionRule" ")"
$text = $rules.Text.TrimEnd() + $rules.Newline
foreach ($test in $tests) { $text += $ruleBlock.Replace($source, "Descriptor_Deck_Division_EZ_Test_$($test.Letter)_multi") + $rules.Newline }
Write-Ndf $rulesFile $rules $text

# 3. their activation-point costs, copied from the 3rd Armored's
$costs = Read-Ndf $costsFile
$costBlock = Get-Block $costs.Text "MatrixCostName_US_3rd_Arm_multi is MAP" "]"
$text = $costs.Text.TrimEnd() + $costs.Newline
foreach ($test in $tests) { $text += $costBlock.Replace("MatrixCostName_US_3rd_Arm_multi", "MatrixCostName_EZ_Test_$($test.Letter)_multi") + $costs.Newline }
Write-Ndf $costsFile $costs $text

# 4. their deck-code numbers
$serializer = Read-Ndf $serializerFile
$text = $serializer.Text
foreach ($test in $tests) {
    $text = Add-ToMap $text "DivisionIds = MAP" "        (Descriptor_Deck_Division_EZ_Test_$($test.Letter)_multi, $($test.Number))," $serializer.Newline
}
Write-Ndf $serializerFile $serializer $text

# 5. Test A's own emblem: the texture, its entry in the bank of division textures, and the picture
$textures = Read-Ndf $texturesFile
$nl = $textures.Newline
$texture = "Texture_Division_Emblem_EZ_Test is TUIResourceTexture_Common$nl($nl    FileName = `"GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/EZ_Test.png`"$nl)$nl"
$text = $textures.Text.Replace("DivisionAdditionalTextureBank is", $texture + "DivisionAdditionalTextureBank is")
$text = Add-ToMap $text "Textures = MAP" "        (`"Texture_Division_Emblem_EZ_Test`", MAP [(~/ComponentState/Normal, ~/Texture_Division_Emblem_EZ_Test)])," $nl
Write-Ndf $texturesFile $textures $text
New-Item -ItemType Directory -Force -Path (Split-Path $emblemFile) | Out-Null
Copy-Item (Join-Path $here "EZ_Test.png") $emblemFile -Force

# 6. their names, in the mod's out-of-game texts
$texts = Read-Ndf $textsFile
$text = $texts.Text.TrimEnd() + $texts.Newline
foreach ($test in $tests) { $text += "$($test.Token);$($test.Name)" + $texts.Newline }
Write-Ndf $textsFile $texts $text

Write-Host "Done: two test divisions added to $modName. Now run GenerateMod.bat, and check them in the game (see README.txt)."
